using System.Text.Json;
using System.Text.RegularExpressions;
using Lumora.Application.Abstractions.Llm;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.UseCases.Conversations;
using Lumora.Application.UseCases.StructuredFacts;

namespace Lumora.Application.UseCases.Chat;

public sealed class ChatUseCases(
    ILlmChatService llmChatService,
    IStructuredFactUseCases structuredFactUseCases,
    IConversationUseCases conversationUseCases,
    ChatModelOptions modelOptions,
    TimeProvider timeProvider) : IChatUseCases
{
    private const int MaximumMessageLength = 2_000;
    private const int MaximumIntentResponseLength = 8_192;
    private const int MaximumBirthdaySearchResults = 200;
    private const int RecentMessageCount = 2;
    private const string BirthdayFactType = "Person.Birthday";

    private const string SystemPrompt = """
        You are a strict intent extractor for a local personal-data application.
        Return one JSON object only, with exactly these top-level keys: "intent" and "arguments".
        Allowed intents:
        - "SAVE_BIRTHDAY": arguments may contain "personName", "year", "month", "day".
        - "QUERY_BIRTHDAY": arguments may contain "personName".
        - "QUERY_BIRTHDAYS_THIS_MONTH": arguments must be empty.
        - "UNKNOWN": arguments must be empty.
        Treat an explicit statement of a person's birthday as SAVE_BIRTHDAY, even without a save verb.
        Examples:
        User: "홍길동의 생일은 5월 10일이야"
        Output: {"intent":"SAVE_BIRTHDAY","arguments":{"personName":"홍길동","month":5,"day":10}}
        User: "홍길동 생일 알려줘"
        Output: {"intent":"QUERY_BIRTHDAY","arguments":{"personName":"홍길동"}}
        User: "이번 달 생일자 조회해줘"
        Output: {"intent":"QUERY_BIRTHDAYS_THIS_MONTH","arguments":{}}
        User: "Alex's birthday is May 10"
        Output: {"intent":"SAVE_BIRTHDAY","arguments":{"personName":"Alex","month":5,"day":10}}
        User: "When is Alex's birthday?"
        Output: {"intent":"QUERY_BIRTHDAY","arguments":{"personName":"Alex"}}
        User: "List birthdays this month"
        Output: {"intent":"QUERY_BIRTHDAYS_THIS_MONTH","arguments":{}}
        User: "안녕하세요"
        Output: {"intent":"UNKNOWN","arguments":{}}
        Use JSON null or omit any value that is not explicitly provided or is ambiguous.
        Never invent a person, year, month, day, stored fact, or missing value.
        Do not answer the user, explain your reasoning, call tools, or output SQL.
        Understand Korean and English. Choose UNKNOWN for unsupported requests or unclear intent.
        """;

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8
    };
    private static readonly Regex KoreanBirthdayDateRegex = new(
        @"(?:(?<year>\d{4})\s*년\s*)?(?<month>\d{1,2})\s*월\s*(?<day>\d{1,2})\s*일?",
        RegexOptions.CultureInvariant);
    private static readonly Regex NumericBirthdayDateRegex = new(
        @"(?:(?<year>\d{4})[-/.])?(?<month>\d{1,2})[-/.](?<day>\d{1,2})(?:[-/.](?<yearAfter>\d{4}))?",
        RegexOptions.CultureInvariant);
    private static readonly Regex EnglishBirthdayDateRegex = new(
        @"\b(?<month>January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)\s+(?<day>\d{1,2})(?:,?\s+(?<year>\d{4}))?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task<ChatTurnResult> SendAsync(
        TenantUserScope scope,
        ChatTurnCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command);
        var messages = new List<LlmChatMessage>
        {
            new(LlmMessageRole.System, SystemPrompt)
        };
        if (command.ConversationId is { } conversationId)
        {
            var history = await conversationUseCases.GetRecentMessagesAsync(
                scope,
                conversationId,
                RecentMessageCount,
                cancellationToken);
            messages.AddRange(history.Select(message => new LlmChatMessage(
                message.Role == Lumora.Domain.Entities.MessageRole.User
                    ? LlmMessageRole.User
                    : LlmMessageRole.Assistant,
                message.Content)));
        }

        messages.Add(new LlmChatMessage(LlmMessageRole.User, command.Message));

        var response = await llmChatService.CompleteAsync(
            new LlmChatRequest(
                modelOptions.ModelName,
                messages,
                new LlmGenerationOptions(
                    Temperature: 0,
                    MaximumOutputTokens: 256,
                    ContextLength: 4_096,
                    ThinkingEnabled: false,
                    JsonFormat: true)),
            cancellationToken);

        if (!string.Equals(response.FinishReason, "stop", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(response.Content) ||
            response.Content.Length > MaximumIntentResponseLength ||
            !TryParseIntent(response.Content, out var intent))
        {
            throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
        }

        var result = await ExecuteIntentAsync(scope, command, intent, cancellationToken);
        var savedConversationId = await conversationUseCases.SaveExchangeAsync(
            scope,
            command.ConversationId,
            command.Message,
            result.Response,
            cancellationToken);
        return new ChatTurnResult(savedConversationId, result.Response, true);
    }

    private async Task<IntentResult> ExecuteIntentAsync(
        TenantUserScope scope,
        ChatTurnCommand command,
        ExtractedIntent intent,
        CancellationToken cancellationToken)
    {
        switch (intent.Name)
        {
            case "UNKNOWN":
                return new IntentResult(UnsupportedResponse(command.Language));
            case "SAVE_BIRTHDAY":
                return await SaveBirthdayAsync(scope, command.Message, command.Language, intent, cancellationToken);
            case "QUERY_BIRTHDAY":
                return await QueryBirthdayAsync(scope, command.Message, command.Language, intent, cancellationToken);
            case "QUERY_BIRTHDAYS_THIS_MONTH":
                return await QueryBirthdaysThisMonthAsync(
                    scope,
                    command.Message,
                    command.Language,
                    cancellationToken);
            default:
                throw new LlmProviderException(LlmProviderErrorKind.InvalidResponse);
        }
    }

    private async Task<IntentResult> SaveBirthdayAsync(
        TenantUserScope scope,
        string userMessage,
        string language,
        ExtractedIntent intent,
        CancellationToken cancellationToken)
    {
        var name = NormalizePersonName(intent.PersonName);
        if (name is null)
        {
            return new IntentResult(
                language == "en" ? "Whose birthday should I save?" : "누구의 생일을 저장할까요?");
        }

        if (intent.Month is null || intent.Day is null)
        {
            return new IntentResult(
                language == "en"
                    ? $"Please provide {name}'s birthday month and day."
                    : $"{name}님의 생일 월과 일을 알려 주세요.");
        }

        if (!ContainsBirthdayReference(userMessage) ||
            !ContainsBirthdayDateForPerson(
                userMessage,
                name,
                intent.Year,
                intent.Month.Value,
                intent.Day.Value))
        {
            return new IntentResult(
                language == "en"
                    ? "Please restate the birthday with the person's name and the exact month and day."
                    : "이름과 생일의 월·일을 포함해 다시 말씀해 주세요.");
        }

        if (!IsValidBirthday(intent.Year, intent.Month.Value, intent.Day.Value))
        {
            return new IntentResult(
                language == "en"
                    ? "That date is not valid. Please provide the birthday month and day again."
                    : "유효하지 않은 날짜입니다. 생일의 월과 일을 다시 확인해 주세요.");
        }

        try
        {
            var fact = await structuredFactUseCases.CreateAsync(
                scope,
                new CreateStructuredFactCommand(
                    BirthdayFactType,
                    new StructuredFactValue(name, Year: intent.Year, Month: intent.Month, Day: intent.Day)),
                cancellationToken);
            return new IntentResult(
                language == "en"
                    ? $"Saved {FormatDate(fact.Value, language)} as {name}'s birthday."
                    : $"{name}님의 생일을 {FormatDate(fact.Value, language)}로 저장했습니다.");
        }
        catch (DuplicateStructuredFactException)
        {
            return new IntentResult(
                language == "en"
                    ? $"{name}'s birthday is already saved with that date."
                    : $"{name}님의 생일은 이미 같은 날짜로 저장되어 있습니다.");
        }
        catch (ConflictingStructuredFactException)
        {
            return new IntentResult(
                language == "en"
                    ? $"{name} already has a different saved birthday. This chat will not change it."
                    : $"{name}님은 다른 생일이 이미 저장되어 있어 이번 요청에서는 변경하지 않았습니다.");
        }
    }

    private async Task<IntentResult> QueryBirthdayAsync(
        TenantUserScope scope,
        string userMessage,
        string language,
        ExtractedIntent intent,
        CancellationToken cancellationToken)
    {
        var name = NormalizePersonName(intent.PersonName);
        if (name is null)
        {
            return new IntentResult(
                language == "en" ? "Whose birthday should I look up?" : "누구의 생일을 조회할까요?");
        }

        if (!ContainsBirthdayReference(userMessage) ||
            !ContainsLookupIntent(userMessage) ||
            !userMessage.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            return new IntentResult(UnsupportedResponse(language));
        }

        var facts = await structuredFactUseCases.SearchAsync(
            scope,
            new StructuredFactSearch(FactType: BirthdayFactType, Subject: name),
            new PageRequest(1, 1),
            cancellationToken);
        var fact = facts.Items.FirstOrDefault();
        var response = fact is null
            ? language == "en"
                ? $"There is no saved birthday for {name}."
                : $"{name}님의 생일은 저장된 정보가 없습니다."
            : language == "en"
                ? $"{name}'s birthday is {FormatDate(fact.Value, language)}."
                : $"{name}님의 생일은 {FormatDate(fact.Value, language)}입니다.";
        return new IntentResult(response);
    }

    private async Task<IntentResult> QueryBirthdaysThisMonthAsync(
        TenantUserScope scope,
        string userMessage,
        string language,
        CancellationToken cancellationToken)
    {
        if (!ContainsBirthdayReference(userMessage) || !ContainsCurrentMonthIntent(userMessage))
        {
            return new IntentResult(UnsupportedResponse(language));
        }

        var month = timeProvider.GetLocalNow().Month;
        var facts = await structuredFactUseCases.SearchAsync(
            scope,
            new StructuredFactSearch(FactType: BirthdayFactType, BirthdayMonth: month),
            new PageRequest(1, MaximumBirthdaySearchResults),
            cancellationToken);
        var values = facts.Items
            .Select(fact => $"{fact.Value.Subject} ({FormatDate(fact.Value, language)})")
            .ToArray();
        string response;
        if (values.Length == 0)
        {
            response = language == "en"
                ? $"No birthdays are saved for {month} this month."
                : $"이번 달({month}월)에 등록된 생일자가 없습니다.";
        }
        else
        {
            var separator = language == "en" ? ", " : ", ";
            var suffix = facts.TotalCount > values.Length
                ? language == "en"
                    ? $" and {facts.TotalCount - values.Length} more."
                    : $" 외 {facts.TotalCount - values.Length}명"
                : string.Empty;
            response = language == "en"
                ? $"Birthdays this month ({month}): {string.Join(separator, values)}{suffix}"
                : $"이번 달({month}월) 생일자: {string.Join(separator, values)}{suffix}";
        }

        return new IntentResult(response);
    }

    private static bool TryParseIntent(string content, out ExtractedIntent intent)
    {
        intent = default;
        try
        {
            using var document = JsonDocument.Parse(content, JsonOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasOnlyUniqueProperties(root, ["intent", "arguments"]) ||
                !root.TryGetProperty("intent", out var intentElement) ||
                intentElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("arguments", out var arguments) ||
                arguments.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var name = intentElement.GetString();
            switch (name)
            {
                case "SAVE_BIRTHDAY":
                    if (!HasOnlyUniqueProperties(arguments, ["personName", "year", "month", "day"]) ||
                        !TryGetOptionalString(arguments, "personName", out var personName) ||
                        !TryGetOptionalInt32(arguments, "year", out var year) ||
                        !TryGetOptionalInt32(arguments, "month", out var month) ||
                        !TryGetOptionalInt32(arguments, "day", out var day))
                    {
                        return false;
                    }

                    intent = new ExtractedIntent(name, personName, year, month, day);
                    return true;
                case "QUERY_BIRTHDAY":
                    if (!HasOnlyUniqueProperties(arguments, ["personName"]) ||
                        !TryGetOptionalString(arguments, "personName", out var queryName))
                    {
                        return false;
                    }

                    intent = new ExtractedIntent(name, queryName, null, null, null);
                    return true;
                case "QUERY_BIRTHDAYS_THIS_MONTH":
                case "UNKNOWN":
                    if (!HasOnlyUniqueProperties(arguments, []))
                    {
                        return false;
                    }

                    intent = new ExtractedIntent(name, null, null, null, null);
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasOnlyUniqueProperties(JsonElement element, params string[] allowedProperties)
    {
        var allowed = new HashSet<string>(allowedProperties, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetOptionalString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryGetOptionalInt32(JsonElement element, string propertyName, out int? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var number))
        {
            return false;
        }

        value = number;
        return true;
    }

    private static string? NormalizePersonName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim();
        return normalized.Length <= 512 && !normalized.Any(char.IsControl)
            ? normalized
            : null;
    }

    private static bool IsValidBirthday(int? year, int month, int day)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1)
        {
            return false;
        }

        return day <= DateTime.DaysInMonth(year ?? 2000, month);
    }

    private static bool ContainsBirthdayDateForPerson(
        string message,
        string personName,
        int? year,
        int month,
        int day)
    {
        foreach (var (match, yearGroup) in FindBirthdayDates(message))
        {
            if (!TryMatchDate(match, yearGroup, month, day, year))
            {
                continue;
            }

            var clauseStart = FindClauseStart(message, match.Index);
            var clause = message[clauseStart..match.Index];
            if (clause.Contains(personName, StringComparison.OrdinalIgnoreCase) &&
                ContainsBirthdayReference(clause))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<(Match Match, string YearGroup)> FindBirthdayDates(string message)
    {
        foreach (Match match in KoreanBirthdayDateRegex.Matches(message))
        {
            yield return (match, "year");
        }

        foreach (Match match in NumericBirthdayDateRegex.Matches(message))
        {
            yield return (match, match.Groups["year"].Success ? "year" : "yearAfter");
        }

        foreach (Match match in EnglishBirthdayDateRegex.Matches(message))
        {
            yield return (match, "year");
        }
    }

    private static bool TryMatchDate(
        Match match,
        string yearGroup,
        int month,
        int day,
        int? year)
    {
        var parsedMonth = int.TryParse(match.Groups["month"].Value, out var numericMonth)
            ? numericMonth
            : TryParseEnglishMonth(match.Groups["month"].Value, out var englishMonth)
                ? englishMonth
                : 0;
        if (parsedMonth == 0 ||
            !int.TryParse(match.Groups["day"].Value, out var parsedDay) ||
            parsedMonth != month ||
            parsedDay != day)
        {
            return false;
        }

        var parsedYear = match.Groups[yearGroup].Success &&
            int.TryParse(match.Groups[yearGroup].Value, out var yearNumber)
                ? yearNumber
                : (int?)null;
        return parsedYear == year;
    }

    private static int FindClauseStart(string message, int endIndex)
    {
        if (endIndex == 0)
        {
            return 0;
        }

        var startIndex = 0;
        foreach (var separator in new[] { ',', '，', ';', '；', '.', '。', '\n', '\r' })
        {
            startIndex = Math.Max(startIndex, message.LastIndexOf(separator, endIndex - 1));
        }

        foreach (var separator in new[] { "이고", "그리고", " and " })
        {
            startIndex = Math.Max(
                startIndex,
                message.LastIndexOf(separator, endIndex - 1, StringComparison.OrdinalIgnoreCase));
        }

        return startIndex;
    }

    private static bool TryParseEnglishMonth(string month, out int monthNumber)
    {
        monthNumber = month[..3].ToLowerInvariant() switch
        {
            "jan" => 1,
            "feb" => 2,
            "mar" => 3,
            "apr" => 4,
            "may" => 5,
            "jun" => 6,
            "jul" => 7,
            "aug" => 8,
            "sep" => 9,
            "oct" => 10,
            "nov" => 11,
            "dec" => 12,
            _ => 0
        };
        return monthNumber != 0;
    }

    private static bool ContainsBirthdayReference(string message) =>
        message.Contains("생일", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("birthday", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsLookupIntent(string message) =>
        Regex.IsMatch(
            message,
            @"언제|몇\s*월|알려|조회|검색|찾아|what|when|look\s*up|query|search|find|tell\s*me",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ContainsCurrentMonthIntent(string message) =>
        Regex.IsMatch(
            message,
            @"이번\s*달|이달|this\s+month",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string FormatDate(StructuredFactValue value, string language)
    {
        var year = value.Year is { } valueYear
            ? language == "en" ? $"{valueYear}-" : $"{valueYear}년 "
            : string.Empty;
        var monthAndDay = language == "en"
            ? $"{value.Month}/{value.Day}"
            : $"{value.Month}월 {value.Day}일";
        return year + monthAndDay;
    }

    private static string UnsupportedResponse(string language) =>
        language == "en"
            ? "I can currently save birthdays, look up a person's saved birthday, or list birthdays this month."
            : "현재 생일 저장, 특정 직원 생일 조회, 이번 달 생일자 조회만 지원합니다.";

    private static void ValidateCommand(ChatTurnCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Message);
        if (command.Message.Length > MaximumMessageLength)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "메시지는 2,000자를 초과할 수 없습니다.");
        }

        if (command.Language is not ("ko" or "en"))
        {
            throw new ArgumentException("언어는 ko 또는 en이어야 합니다.", nameof(command));
        }
    }

    private readonly record struct ExtractedIntent(
        string Name,
        string? PersonName,
        int? Year,
        int? Month,
        int? Day);

    private sealed record IntentResult(string Response);
}
