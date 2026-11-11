using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.Abstractions.Security;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Lumora.Infrastructure.Security;
using Lumora.Infrastructure.Security.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Lumora.Api.IntegrationTests;

public sealed class MemoryApiTests
{
    [Fact]
    public async Task Memory_endpoints_fail_closed_without_explicit_development_scope()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/memories",
            new { type = "Preference", content = "synthetic input" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Memory_endpoints_use_only_the_configured_development_scope()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lumora-memory-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "api-test.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        try
        {
            await using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(webHost =>
                {
                    webHost.UseEnvironment("Development");
                    webHost.ConfigureAppConfiguration((_, configuration) =>
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Database:Provider"] = "Sqlite",
                            ["ConnectionStrings:Sqlite"] = connectionString,
                            ["Security:KeyDirectory"] = Path.Combine(directory, "keys"),
                            ["MemoryApi:EnableDevelopmentScope"] = "true",
                            ["MemoryApi:TenantId"] = tenantId.ToString(),
                            ["MemoryApi:UserId"] = userId.ToString()
                        }));
                });
            using var client = factory.CreateClient();
            await using (var serviceScope = factory.Services.CreateAsyncScope())
            {
                var services = serviceScope.ServiceProvider;
                await services.GetRequiredService<IKeyMaterialInitializer>().InitializeAsync(CancellationToken.None);
                var dbContext = services.GetRequiredService<LumoraDbContext>();
                await dbContext.Database.MigrateAsync();
                var encryption = services.GetRequiredService<IEncryptionService>();
                var now = DateTime.UtcNow;
                dbContext.Tenants.AddRange(
                    new Tenant(tenantId, "Synthetic Tenant", now),
                    new Tenant(otherTenantId, "Other Synthetic Tenant", now));
                dbContext.Users.AddRange(
                    new User(userId, tenantId, await EncryptUserNameAsync(encryption, tenantId, userId), now),
                    new User(otherUserId, otherTenantId, await EncryptUserNameAsync(encryption, otherTenantId, otherUserId), now));
                await dbContext.SaveChangesAsync();
            }

            using var createResponse = await client.PostAsJsonAsync(
                "/api/memories",
                new
                {
                    type = "Preference",
                    content = "synthetic api memory",
                    tenantId = otherTenantId,
                    userId = otherUserId
                });
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var memoryId = created.RootElement.GetProperty("id").GetGuid();

            using var getResponse = await client.GetAsync($"/api/memories/{memoryId}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            using var retrieved = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
            Assert.Equal("synthetic api memory", retrieved.RootElement.GetProperty("content").GetString());

            using var listResponse = await client.GetAsync("/api/memories");
            Assert.True(
                listResponse.StatusCode == HttpStatusCode.OK,
                await listResponse.Content.ReadAsStringAsync());
            using var listed = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            Assert.Equal(1, listed.RootElement.GetProperty("totalCount").GetInt64());

            using var memoryUpdateResponse = await client.PutAsJsonAsync(
                $"/api/memories/{memoryId}",
                new
                {
                    type = "Preference",
                    content = "synthetic api memory updated",
                    confidenceScore = 9_000,
                    verificationStatus = "Verified",
                    sensitivity = "Personal"
                });
            Assert.Equal(HttpStatusCode.OK, memoryUpdateResponse.StatusCode);

            await using (var serviceScope = factory.Services.CreateAsyncScope())
            {
                var dbContext = serviceScope.ServiceProvider.GetRequiredService<LumoraDbContext>();
                var stored = await dbContext.Memories.SingleAsync(item => item.Id == memoryId);
                Assert.Equal(tenantId, stored.TenantId);
                Assert.Equal(userId, stored.UserId);
            }

            using var deleteResponse = await client.DeleteAsync($"/api/memories/{memoryId}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
            using var missingResponse = await client.GetAsync($"/api/memories/{memoryId}");
            Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

            using var factCreateResponse = await client.PostAsJsonAsync(
                "/api/structured-facts",
                new
                {
                    factType = "Person.Birthday",
                    value = new { subject = "Synthetic API subject", month = 5, day = 12 }
                });
            Assert.Equal(HttpStatusCode.Created, factCreateResponse.StatusCode);
            using var createdFact = JsonDocument.Parse(await factCreateResponse.Content.ReadAsStringAsync());
            var factId = createdFact.RootElement.GetProperty("memoryId").GetGuid();

            using var factGetResponse = await client.GetAsync($"/api/structured-facts/{factId}");
            Assert.Equal(HttpStatusCode.OK, factGetResponse.StatusCode);
            using var factUpdateResponse = await client.PutAsJsonAsync(
                $"/api/structured-facts/{factId}",
                new
                {
                    factType = "Person.Birthday",
                    value = new { subject = "Synthetic API subject", month = 5, day = 13 }
                });
            Assert.Equal(HttpStatusCode.OK, factUpdateResponse.StatusCode);

            using var birthdaySearchResponse = await client.GetAsync("/api/structured-facts/birthdays?month=5");
            Assert.Equal(HttpStatusCode.OK, birthdaySearchResponse.StatusCode);
            using var birthdaySearch = JsonDocument.Parse(await birthdaySearchResponse.Content.ReadAsStringAsync());
            Assert.Equal(1, birthdaySearch.RootElement.GetProperty("totalCount").GetInt64());
            Assert.Equal(
                JsonValueKind.Null,
                birthdaySearch.RootElement.GetProperty("items")[0].GetProperty("value").GetProperty("year").ValueKind);

            using var factDeleteResponse = await client.DeleteAsync($"/api/structured-facts/{factId}");
            Assert.Equal(HttpStatusCode.NoContent, factDeleteResponse.StatusCode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Internal_encryption_error_is_generic_and_sensitive_exception_text_is_not_logged()
    {
        const string sensitiveMarker = "synthetic-sensitive-error-marker";
        var directory = Path.Combine(Path.GetTempPath(), $"lumora-api-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "api-test.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        using var loggerProvider = new CapturingLoggerProvider();

        try
        {
            await using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(webHost =>
                {
                    webHost.UseEnvironment("Development");
                    webHost.ConfigureAppConfiguration((_, configuration) =>
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Database:Provider"] = "Sqlite",
                            ["ConnectionStrings:Sqlite"] = connectionString,
                            ["Security:KeyDirectory"] = Path.Combine(directory, "keys"),
                            ["MemoryApi:EnableDevelopmentScope"] = "true",
                            ["MemoryApi:TenantId"] = tenantId.ToString(),
                            ["MemoryApi:UserId"] = userId.ToString()
                        }));
                    webHost.ConfigureLogging(logging =>
                        logging.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Warning));
                    webHost.ConfigureServices(services =>
                    {
                        services.RemoveAll<IEncryptionService>();
                        services.AddScoped<IEncryptionService>(_ =>
                            new SensitiveMessageEncryptionService(
                                Path.Combine(directory, "keys"),
                                sensitiveMarker));
                    });
                });
            using var client = factory.CreateClient();
            await using (var serviceScope = factory.Services.CreateAsyncScope())
            {
                var services = serviceScope.ServiceProvider;
                await services.GetRequiredService<IKeyMaterialInitializer>().InitializeAsync(CancellationToken.None);
                var dbContext = services.GetRequiredService<LumoraDbContext>();
                await dbContext.Database.MigrateAsync();
                var encryption = services.GetRequiredService<IEncryptionService>();
                var now = DateTime.UtcNow;
                dbContext.Tenants.Add(new Tenant(tenantId, "Synthetic Tenant", now));
                dbContext.Users.Add(new User(
                    userId,
                    tenantId,
                    await EncryptUserNameAsync(encryption, tenantId, userId),
                    now));
                await dbContext.SaveChangesAsync();
            }

            using var createResponse = await client.PostAsJsonAsync(
                "/api/memories",
                new { type = "Preference", content = "synthetic api memory" });
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var memoryId = created.RootElement.GetProperty("id").GetGuid();

            using var response = await client.GetAsync($"/api/memories/{memoryId}");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(sensitiveMarker, responseBody);
            Assert.DoesNotContain(sensitiveMarker, loggerProvider.Messages);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<EncryptedPayload> EncryptUserNameAsync(
        IEncryptionService encryption,
        Guid tenantId,
        Guid userId)
    {
        var plaintext = Encoding.UTF8.GetBytes($"Synthetic User {userId:N}");
        try
        {
            return await encryption.EncryptAsync(
                plaintext,
                new EncryptionContext(tenantId, userId, userId, nameof(User), nameof(User.DisplayName)),
                CancellationToken.None);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private sealed class SensitiveMessageEncryptionService(
        string keyDirectory,
        string sensitiveMarker) : IEncryptionService
    {
        private readonly AesGcmEncryptionService _inner =
            new(new DpapiKeyMaterialStore(keyDirectory));

        public Task<EncryptedPayload> EncryptAsync(
            ReadOnlyMemory<byte> plaintext,
            EncryptionContext context,
            CancellationToken cancellationToken) =>
            _inner.EncryptAsync(plaintext, context, cancellationToken);

        public Task<byte[]> DecryptAsync(
            EncryptedPayload payload,
            EncryptionContext context,
            CancellationToken cancellationToken) =>
            Task.FromException<byte[]>(new CryptographicException(sensitiveMarker));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public string Messages => string.Join(Environment.NewLine, _messages);

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull =>
                null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue($"{formatter(state, exception)} {exception}");
            }
        }
    }
}
