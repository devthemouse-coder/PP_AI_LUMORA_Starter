using Lumora.Application.Abstractions.Llm;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.UseCases.Chat;
using Lumora.Application.UseCases.Conversations;
using Lumora.Application.UseCases.StructuredFacts;

namespace Lumora.Application.UnitTests;

public sealed class ChatUseCasesTests
{
    private static readonly TenantUserScope Scope = new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000002"));

    [Fact]
    public async Task Explicit_birthday_is_created_and_exchange_is_saved()
    {
        var facts = new FakeStructuredFacts();
        var conversations = new FakeConversations();
        var service = CreateUseCases(
            """{"intent":"SAVE_BIRTHDAY","arguments":{"personName":"홍길동","month":5,"day":10}}""",
            facts,
            conversations);

        var result = await service.SendAsync(
            Scope,
            new ChatTurnCommand("홍길동의 생일은 5월 10일이야", "ko"),
            CancellationToken.None);

        var created = Assert.Single(facts.Created);
        Assert.Equal("Person.Birthday", created.Command.FactType);
        Assert.Equal("홍길동", created.Command.Value.Subject);
        Assert.Equal(5, created.Command.Value.Month);
        Assert.Equal(10, created.Command.Value.Day);
        Assert.Equal(Scope, created.Scope);
        Assert.Equal("홍길동님의 생일을 5월 10일로 저장했습니다.", result.Response);
        Assert.True(result.Persisted);
        Assert.Equal(conversations.NewConversationId, result.ConversationId);
        Assert.Equal(1, conversations.SaveCount);
    }

    [Fact]
    public async Task English_birthday_statement_is_supported_with_grounded_date()
    {
        var facts = new FakeStructuredFacts();
        var service = CreateUseCases(
            """{"intent":"SAVE_BIRTHDAY","arguments":{"personName":"Alex","month":5,"day":10}}""",
            facts,
            new FakeConversations());

        var result = await service.SendAsync(
            Scope,
            new ChatTurnCommand("Alex's birthday is May 10", "en"),
            CancellationToken.None);

        Assert.Equal("Alex", Assert.Single(facts.Created).Command.Value.Subject);
        Assert.Equal("Saved 5/10 as Alex's birthday.", result.Response);
    }

    [Fact]
    public async Task Invalid_date_is_clarified_without_creating_a_fact()
    {
        var facts = new FakeStructuredFacts();
        var conversations = new FakeConversations();
        var service = CreateUseCases(
            """{"intent":"SAVE_BIRTHDAY","arguments":{"personName":"홍길동","month":2,"day":30}}""",
            facts,
            conversations);

        var result = await service.SendAsync(
            Scope,
            new ChatTurnCommand("홍길동의 생일은 2월 30일이야", "ko"),
            CancellationToken.None);

        Assert.Empty(facts.Created);
        Assert.Contains("유효하지 않은 날짜", result.Response, StringComparison.Ordinal);
        Assert.Equal(1, conversations.SaveCount);
    }

    [Fact]
    public async Task Missing_person_name_is_clarified_without_creating_a_fact()
    {
        var facts = new FakeStructuredFacts();
        var service = CreateUseCases(
            """{"intent":"SAVE_BIRTHDAY","arguments":{"personName":null,"month":5,"day":10}}""",
            facts,
            new FakeConversations());

        var result = await service.SendAsync(
            Scope,
            new ChatTurnCommand("생일은 5월 10일이야", "ko"),
            CancellationToken.None);

        Assert.Contains("누구의 생일", result.Response, StringComparison.Ordinal);
        Assert.Empty(facts.Created);
    }

    [Fact]
    public async Task Model_cannot_create_a_date_not_grounded_in_the_message()
    {
        var facts = new FakeStructuredFacts();
        var conversations = new FakeConversations();
        var service = CreateUseCases(
            """{"intent":"SAVE_BIRTHDAY","arguments":{"personName":"홍길동","month":5,"day":11}}""",
            facts,
            conversations);

        var result = await service.SendAsync(
            Scope,
            new ChatTurnCommand("홍길동의 생일은 5월 10일이야", "ko"),
            CancellationToken.None);

        Assert.Empty(facts.Created);
        Assert.Contains("이름과 생일", result.Response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Model_cannot_assign_another_persons_date()
    {
        var facts = new FakeStructuredFacts();
        var service = CreateUseCases(
            """{"intent":"SAVE_BIRTHDAY","arguments":{"personName":"김철수","month":11,"day":3}}""",
            facts,
            new FakeConversations());

        await service.SendAsync(
            Scope,
            new ChatTurnCommand("김철수의 생일은 5월 12일이고, 이영희의 생일은 11월 3일이야.", "ko"),
            CancellationToken.None);

        Assert.Empty(facts.Created);
    }

    [Fact]
    public async Task Birthday_lookup_uses_structured_fact_search_and_scope()
    {
        var facts = new FakeStructuredFacts
        {
            SearchResult = new StructuredFactPage(
                [CreateFact("홍길동", 5, 10)],
                1,
                1,
                1)
        };
        var service = CreateUseCases(
            """{"intent":"QUERY_BIRTHDAY","arguments":{"personName":"홍길동"}}""",
            facts,
            new FakeConversations());

        var result = await service.SendAsync(
            Scope,
            new ChatTurnCommand("홍길동 생일 알려줘", "ko"),
            CancellationToken.None);

        Assert.Equal("홍길동님의 생일은 5월 10일입니다.", result.Response);
        Assert.Equal(Scope, facts.LastSearchScope);
        Assert.Equal("홍길동", facts.LastSearch?.Subject);
        Assert.Equal("Person.Birthday", facts.LastSearch?.FactType);
    }

    [Fact]
    public async Task Missing_birthday_lookup_returns_no_saved_fact_claim()
    {
        var facts = new FakeStructuredFacts();
        var service = CreateUseCases(
            """{"intent":"QUERY_BIRTHDAY","arguments":{"personName":"없는직원"}}""",
            facts,
            new FakeConversations());

        var result = await service.SendAsync(
            Scope,
            new ChatTurnCommand("없는직원 생일 언제야?", "ko"),
            CancellationToken.None);

        Assert.Contains("저장된 정보가 없습니다", result.Response, StringComparison.Ordinal);
        Assert.Empty(facts.Created);
    }

    [Fact]
    public async Task Current_month_query_uses_clock_month_and_returns_saved_names()
    {
        var facts = new FakeStructuredFacts
        {
            SearchResult = new StructuredFactPage(
                [CreateFact("홍길동", 3, 10)],
                1,
                200,
                1)
        };
        var service = CreateUseCases(
            """{"intent":"QUERY_BIRTHDAYS_THIS_MONTH","arguments":{}}""",
            facts,
            new FakeConversations());

        var result = await service.SendAsync(
            Scope,
            new ChatTurnCommand("이번 달 생일자 조회해줘", "ko"),
            CancellationToken.None);

        Assert.Equal(3, facts.LastSearch?.BirthdayMonth);
        Assert.Contains("홍길동 (3월 10일)", result.Response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_intent_is_refused_without_fact_access()
    {
        var facts = new FakeStructuredFacts();
        var conversations = new FakeConversations();
        var service = CreateUseCases(
            """{"intent":"UNKNOWN","arguments":{}}""",
            facts,
            conversations);

        var result = await service.SendAsync(
            Scope,
            new ChatTurnCommand("다른 작업 해줘", "ko"),
            CancellationToken.None);

        Assert.Contains("현재 생일 저장", result.Response, StringComparison.Ordinal);
        Assert.Empty(facts.Created);
        Assert.Null(facts.LastSearch);
        Assert.Equal(1, conversations.SaveCount);
    }

    [Fact]
    public async Task Duplicate_birthday_is_reported_without_claiming_a_new_fact()
    {
        var facts = new FakeStructuredFacts
        {
            CreateException = new DuplicateStructuredFactException()
        };
        var service = CreateUseCases(
            """{"intent":"SAVE_BIRTHDAY","arguments":{"personName":"홍길동","month":5,"day":10}}""",
            facts,
            new FakeConversations());

        var result = await service.SendAsync(
            Scope,
            new ChatTurnCommand("홍길동의 생일은 5월 10일이야", "ko"),
            CancellationToken.None);

        Assert.Contains("이미 같은 날짜", result.Response, StringComparison.Ordinal);
        Assert.Single(facts.Created);
    }

    [Theory]
    [InlineData("""{"intent":"DELETE_MEMORY","arguments":{"id":"unknown"}}""")]
    [InlineData("{broken")]
    [InlineData("")]
    public async Task Invalid_or_unsupported_model_output_never_changes_data(string content)
    {
        var facts = new FakeStructuredFacts();
        var conversations = new FakeConversations();
        var service = CreateUseCases(content, facts, conversations);

        var exception = await Assert.ThrowsAsync<LlmProviderException>(() =>
            service.SendAsync(
                Scope,
                new ChatTurnCommand("홍길동의 생일은 5월 10일이야", "ko"),
                CancellationToken.None));

        Assert.Equal(LlmProviderErrorKind.InvalidResponse, exception.Kind);
        Assert.Empty(facts.Created);
        Assert.Equal(0, conversations.SaveCount);
    }

    [Fact]
    public async Task Truncated_model_output_is_rejected_without_data_changes()
    {
        var facts = new FakeStructuredFacts();
        var conversations = new FakeConversations();
        var service = CreateUseCases(
            """{"intent":"SAVE_BIRTHDAY","arguments":{"personName":"홍길동","month":5,"day":10}}""",
            facts,
            conversations,
            finishReason: "length");

        await Assert.ThrowsAsync<LlmProviderException>(() =>
            service.SendAsync(
                Scope,
                new ChatTurnCommand("홍길동의 생일은 5월 10일이야", "ko"),
                CancellationToken.None));

        Assert.Empty(facts.Created);
        Assert.Equal(0, conversations.SaveCount);
    }

    [Fact]
    public async Task Provider_failure_does_not_change_facts_or_conversations()
    {
        var facts = new FakeStructuredFacts();
        var conversations = new FakeConversations();
        var service = CreateUseCases(null, facts, conversations, providerFailure: true);

        await Assert.ThrowsAsync<LlmProviderException>(() =>
            service.SendAsync(
                Scope,
                new ChatTurnCommand("홍길동의 생일은 5월 10일이야", "ko"),
                CancellationToken.None));

        Assert.Empty(facts.Created);
        Assert.Equal(0, conversations.SaveCount);
    }

    private static ChatUseCases CreateUseCases(
        string? content,
        FakeStructuredFacts facts,
        FakeConversations conversations,
        string finishReason = "stop",
        bool providerFailure = false) =>
        new(
            new FakeChatService(content, finishReason, providerFailure),
            facts,
            conversations,
            new ChatModelOptions("synthetic:latest"),
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero)));

    private static StructuredFactDetails CreateFact(string subject, int month, int day) =>
        new(
            Guid.NewGuid(),
            "Person.Birthday",
            new StructuredFactValue(subject, Month: month, Day: day),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            10_000,
            1);

    private sealed class FakeChatService(
        string? content,
        string finishReason,
        bool providerFailure) : ILlmChatService
    {
        public Task<LlmChatResponse> CompleteAsync(
            LlmChatRequest request,
            CancellationToken cancellationToken) =>
            providerFailure
                ? Task.FromException<LlmChatResponse>(
                    new LlmProviderException(LlmProviderErrorKind.RuntimeUnavailable))
                : Task.FromResult(new LlmChatResponse(
                    request.ModelName,
                    content ?? string.Empty,
                    finishReason,
                    null));
    }

    private sealed class FakeStructuredFacts : IStructuredFactUseCases
    {
        public List<(TenantUserScope Scope, CreateStructuredFactCommand Command)> Created { get; } = [];
        public StructuredFactPage SearchResult { get; set; } = new([], 1, 1, 0);
        public Exception? CreateException { get; set; }
        public TenantUserScope? LastSearchScope { get; private set; }
        public StructuredFactSearch? LastSearch { get; private set; }

        public Task<StructuredFactDetails> CreateAsync(
            TenantUserScope scope,
            CreateStructuredFactCommand command,
            CancellationToken cancellationToken)
        {
            Created.Add((scope, command));
            if (CreateException is not null)
            {
                return Task.FromException<StructuredFactDetails>(CreateException);
            }

            return Task.FromResult(new StructuredFactDetails(
                Guid.NewGuid(),
                command.FactType,
                command.Value,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                10_000,
                1));
        }

        public Task<StructuredFactDetails?> GetAsync(
            TenantUserScope scope,
            Guid memoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult<StructuredFactDetails?>(null);

        public Task<StructuredFactPage> SearchAsync(
            TenantUserScope scope,
            StructuredFactSearch search,
            PageRequest page,
            CancellationToken cancellationToken)
        {
            LastSearchScope = scope;
            LastSearch = search;
            return Task.FromResult(SearchResult);
        }

        public Task<StructuredFactDetails?> UpdateAsync(
            TenantUserScope scope,
            Guid memoryId,
            UpdateStructuredFactCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult<StructuredFactDetails?>(null);

        public Task<bool> DeleteAsync(
            TenantUserScope scope,
            Guid memoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class FakeConversations : IConversationUseCases
    {
        public Guid NewConversationId { get; } = Guid.NewGuid();
        public int SaveCount { get; private set; }

        public Task<PageResult<ConversationSummary>> ListAsync(
            TenantUserScope scope,
            PageRequest page,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PageResult<ConversationSummary>([], page.PageNumber, page.PageSize, 0));

        public Task<ConversationMessagePage?> ListMessagesAsync(
            TenantUserScope scope,
            Guid conversationId,
            PageRequest page,
            CancellationToken cancellationToken) =>
            Task.FromResult<ConversationMessagePage?>(null);

        public Task<IReadOnlyList<ConversationMessageDetails>> GetRecentMessagesAsync(
            TenantUserScope scope,
            Guid conversationId,
            int messageCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConversationMessageDetails>>([]);

        public Task<Guid> SaveExchangeAsync(
            TenantUserScope scope,
            Guid? conversationId,
            string userMessage,
            string assistantMessage,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.FromResult(conversationId ?? NewConversationId);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
