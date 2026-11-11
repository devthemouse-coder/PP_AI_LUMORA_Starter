using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Lumora.Api.Endpoints;
using Lumora.Api.Middleware;
using Lumora.Api.Services;
using Lumora.Application.UseCases.Memories;
using Lumora.Application.UseCases.StructuredFacts;
using Lumora.Application.UseCases.Chat;
using Lumora.Application.UseCases.Conversations;
using Lumora.Application.Abstractions.Llm;
using Lumora.Contracts.Health;
using Lumora.Infrastructure.Persistence.Backup;
using Lumora.Infrastructure.Persistence.Context;
using Lumora.Infrastructure.Persistence.Sqlite;
using Lumora.Infrastructure.Providers.Ollama;
using Lumora.Infrastructure.Security.KeyManagement;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<MemoryApiExceptionHandler>();
builder.Services.AddLumoraPersistence(builder.Configuration);
builder.Services.AddScoped<IMemoryUseCases, MemoryUseCases>();
builder.Services.AddScoped<IStructuredFactUseCases, StructuredFactUseCases>();
builder.Services.AddScoped<IConversationUseCases, ConversationUseCases>();
builder.Services.AddScoped<IChatUseCases, ChatUseCases>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITenantUserScopeAccessor, DevelopmentTenantUserScopeAccessor>();
builder.Services.AddOllamaLlmProvider(builder.Configuration);

if (args is ["--initialize-keys"])
{
    await using var keyInitializationApp = builder.Build();
    await using var scope = keyInitializationApp.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<LumoraDbContext>();
    var databasePath = dbContext.Database.GetDbConnection().DataSource;
    if (File.Exists(databasePath))
    {
        if (!await dbContext.Database.CanConnectAsync())
        {
            throw new InvalidOperationException(
                "기존 데이터베이스를 열 수 없어 키 초기화를 중단했습니다. 기존 DPAPI 키를 복구하십시오.");
        }

        if ((await dbContext.Database.GetPendingMigrationsAsync()).Any())
        {
            throw new InvalidOperationException(
                "기존 데이터베이스에 미적용 Migration이 있어 키 초기화를 중단했습니다.");
        }

        var containsProtectedData =
            await dbContext.Users.AnyAsync() ||
            await dbContext.Messages.AnyAsync() ||
            await dbContext.Conversations.AnyAsync(conversation => conversation.Summary != null) ||
            await dbContext.MemoryPayloads.AnyAsync() ||
            await dbContext.StructuredFacts.AnyAsync() ||
            await dbContext.MemoryRevisions.AnyAsync() ||
            await dbContext.MemoryLookupTokens.AnyAsync();
        if (containsProtectedData)
        {
            throw new InvalidOperationException(
                "기존 데이터베이스에 암호화 데이터 또는 검색 토큰이 있어 새 키를 만들 수 없습니다. 기존 키를 복구하십시오.");
        }
    }

    await scope.ServiceProvider.GetRequiredService<IKeyMaterialInitializer>()
        .InitializeAsync(CancellationToken.None);
    Console.WriteLine("DPAPI 키를 초기화했습니다. 키 파일은 데이터베이스와 별도 경로에 보관됩니다.");
    return;
}

if (args is ["--backup", _] or ["--export-key-recovery", _] or ["--import-key-recovery", _] or
    ["--restore-backup", _, _] or ["--replace-backup", _, "--approve-replace"])
{
    await using var maintenanceApp = builder.Build();
    await using var maintenanceScope = maintenanceApp.Services.CreateAsyncScope();
    var services = maintenanceScope.ServiceProvider;

    try
    {
        switch (args[0])
        {
            case "--backup":
            {
                var backup = await services.GetRequiredService<SqliteBackupService>()
                    .CreateAsync(args[1], CancellationToken.None);
                await services.GetRequiredService<BackupStatusStore>()
                    .RecordBackupSucceededAsync(DateTimeOffset.UtcNow, CancellationToken.None);
                Console.WriteLine(
                    $"암호화된 SQLite 백업을 만들고 검증했습니다. 크기: {backup.FileSizeBytes} bytes, 스키마: {backup.SchemaVersion}");
                break;
            }
            case "--export-key-recovery":
            {
                EnsureRecoveryFileIsStoredSeparately(
                    args[1],
                    services.GetRequiredService<LumoraDbContext>(),
                    builder.Configuration);
                var password = ReadPassword("복구 비밀번호(12자 이상): ");
                var confirmation = ReadPassword("복구 비밀번호 확인: ");
                try
                {
                    if (!password.AsSpan().SequenceEqual(confirmation))
                    {
                        throw new InvalidOperationException("복구 비밀번호 확인이 일치하지 않습니다.");
                    }

                    await services.GetRequiredService<PasswordProtectedKeyRecoveryService>()
                        .ExportAsync(args[1], password, CancellationToken.None);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(password.AsSpan()));
                    CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(confirmation.AsSpan()));
                }

                Console.WriteLine("별도 비밀번호로 보호된 키 복구 파일을 생성했습니다. DB 백업과 다른 오프라인 위치에 보관하십시오.");
                break;
            }
            case "--import-key-recovery":
            {
                EnsureRecoveryFileIsStoredSeparately(
                    args[1],
                    services.GetRequiredService<LumoraDbContext>(),
                    builder.Configuration);
                var password = ReadPassword("복구 비밀번호: ");
                try
                {
                    await services.GetRequiredService<PasswordProtectedKeyRecoveryService>()
                        .ImportAsync(
                            args[1],
                            password,
                            services.GetRequiredService<IKeyMaterialRecoveryTarget>(),
                            CancellationToken.None);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(password.AsSpan()));
                }

                Console.WriteLine("키 복구 파일을 현재 Windows 사용자용 DPAPI 저장소로 가져왔습니다.");
                break;
            }
            case "--restore-backup":
            {
                var restored = await services.GetRequiredService<SqliteBackupService>()
                    .RestoreAsync(args[1], args[2], CancellationToken.None);
                await services.GetRequiredService<BackupStatusStore>()
                    .RecordRestoreVerifiedAsync(DateTimeOffset.UtcNow, CancellationToken.None);
                Console.WriteLine(
                    $"백업을 검증하고 별도 DB 파일로 복원했습니다. 크기: {restored.FileSizeBytes} bytes, 스키마: {restored.SchemaVersion}");
                break;
            }
            case "--replace-backup":
            {
                var dbContext = services.GetRequiredService<LumoraDbContext>();
                var activeDatabasePath = dbContext.Database.GetDbConnection().DataSource;
                var existingDatabase = File.Exists(activeDatabasePath);
                var safetyBackup = Path.Combine(
                    BackupDirectoryResolver.Resolve(builder.Configuration),
                    $"pre-restore-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}.lumora-backup");
                var restored = await services.GetRequiredService<SqliteBackupService>()
                    .ReplaceDatabaseAsync(
                        args[1],
                        activeDatabasePath,
                        safetyBackup,
                        userApprovedReplacement: true,
                        CancellationToken.None);
                var status = services.GetRequiredService<BackupStatusStore>();
                if (existingDatabase)
                {
                    await status.RecordBackupSucceededAsync(DateTimeOffset.UtcNow, CancellationToken.None);
                }

                await status.RecordRestoreVerifiedAsync(DateTimeOffset.UtcNow, CancellationToken.None);
                Console.WriteLine(existingDatabase
                    ? $"승인된 DB 교체를 완료했습니다. 기존 DB의 암호화된 사전 백업: {safetyBackup}, 복원 스키마: {restored.SchemaVersion}"
                    : $"승인된 복원 DB를 새 경로에 설치했습니다. 스키마: {restored.SchemaVersion}");
                break;
            }
        }
    }
    catch (Exception exception)
    {
        if (args[0] is "--backup" or "--restore-backup" or "--replace-backup")
        {
            try
            {
                await services.GetRequiredService<BackupStatusStore>()
                    .RecordFailureAsync(args[0], exception.GetType().Name, CancellationToken.None);
            }
            catch (Exception statusException)
            {
                Console.Error.WriteLine(
                    $"유지보수 작업 실패 ({exception.GetType().Name}), 상태 기록 실패 ({statusException.GetType().Name}). 상세 정보는 민감정보 보호를 위해 출력하지 않았습니다.");
                Environment.ExitCode = 1;
                return;
            }
        }

        Console.Error.WriteLine(
            $"유지보수 작업 실패 ({exception.GetType().Name}). 경로와 상세 오류는 민감정보 보호를 위해 출력하지 않았습니다.");
        Environment.ExitCode = 1;
        return;
    }

    return;
}

var app = builder.Build();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet(
        "/api/health",
        (IConfiguration configuration) =>
            Results.Ok(
                new HealthCheckResponse(
                    "ok",
                    configuration["Application:Name"] ?? "P.P. AI - LUMORA",
                    DateTimeOffset.UtcNow)))
    .WithName("GetHealth")
    .WithSummary("API 프로세스 상태 확인")
    .WithDescription("DB 및 LLM 준비 상태와 분리된 API 생존 확인 엔드포인트입니다.");

app.MapGet(
        "/api/ready",
        async (LumoraDbContext dbContext, CancellationToken cancellationToken) =>
        {
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return Results.Json(
                    new { status = "not_ready", database = "unavailable" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Any())
            {
                return Results.Json(
                    new { status = "not_ready", database = "migrations_pending" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new { status = "ready", database = "ready" });
        })
    .WithName("GetReadiness")
    .WithSummary("SQLite 데이터 저장소 준비 상태 확인")
    .WithDescription("DB 연결과 적용된 Migration을 확인하며 API 생존 확인과 분리됩니다.");

app.MapMemoryEndpoints();
app.MapStructuredFactEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapLlmEndpoints();
    app.MapChatEndpoints();
}

app.Run();

static char[] ReadPassword(string prompt)
{
    Console.Write(prompt);
    if (Console.IsInputRedirected)
    {
        return (Console.ReadLine() ?? throw new InvalidOperationException("비밀번호 입력을 읽을 수 없습니다."))
            .ToCharArray();
    }

    var characters = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key is ConsoleKey.Enter)
        {
            Console.WriteLine();
            return characters.ToArray();
        }

        if (key.Key is ConsoleKey.Backspace)
        {
            if (characters.Count > 0)
            {
                characters.RemoveAt(characters.Count - 1);
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            characters.Add(key.KeyChar);
        }
    }
}

static void EnsureRecoveryFileIsStoredSeparately(
    string recoveryFilePath,
    LumoraDbContext dbContext,
    IConfiguration configuration)
{
    var recoveryDirectory = Path.GetDirectoryName(Path.GetFullPath(recoveryFilePath))
        ?? throw new InvalidOperationException("키 복구 파일 디렉터리를 확인할 수 없습니다.");
    var databaseDirectory = Path.GetDirectoryName(
        Path.GetFullPath(dbContext.Database.GetDbConnection().DataSource))
        ?? throw new InvalidOperationException("SQLite 데이터 디렉터리를 확인할 수 없습니다.");
    var backupDirectory = BackupDirectoryResolver.Resolve(configuration);
    if (PathsEqual(recoveryDirectory, databaseDirectory) || PathsEqual(recoveryDirectory, backupDirectory))
    {
        throw new InvalidOperationException("키 복구 파일은 DB 및 DB 백업과 다른 디렉터리/오프라인 매체에 저장하십시오.");
    }
}

static bool PathsEqual(string first, string second) =>
    string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
        StringComparison.OrdinalIgnoreCase);

public partial class Program
{
}
