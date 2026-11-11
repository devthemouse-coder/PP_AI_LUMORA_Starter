using System.Security.Cryptography;
using System.Text;
using Lumora.Application.Abstractions.Llm;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.Abstractions.Security;
using Lumora.Application.UseCases.Chat;
using Lumora.Application.UseCases.Conversations;
using Lumora.Application.UseCases.StructuredFacts;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lumora.Infrastructure.IntegrationTests;

public sealed class ChatUseCaseIntegrationTests
{
    [Fact]
    public async Task Chat_saves_and_queries_birthday_through_existing_encrypted_use_cases()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.InitializeKeysAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await SeedTenantAndUsersAsync(database, tenantId, userId, otherUserId);
        var scope = new TenantUserScope(tenantId, userId);
        var otherScope = new TenantUserScope(tenantId, otherUserId);

        await using var serviceScope = database.CreateScope();
        var services = serviceScope.ServiceProvider;
        var facts = services.GetRequiredService<IStructuredFactUseCases>();
        await facts.CreateAsync(
            otherScope,
            new CreateStructuredFactCommand(
                "Person.Birthday",
                new StructuredFactValue("홍길동", Month: 6, Day: 15)),
            CancellationToken.None);

        var llm = new QueueChatService(
            """{"intent":"SAVE_BIRTHDAY","arguments":{"personName":"홍길동","month":5,"day":10}}""",
            """{"intent":"QUERY_BIRTHDAY","arguments":{"personName":"홍길동"}}""");
        var useCases = new ChatUseCases(
            llm,
            facts,
            services.GetRequiredService<IConversationUseCases>(),
            new ChatModelOptions("synthetic:latest"),
            TimeProvider.System);

        var saved = await useCases.SendAsync(
            scope,
            new ChatTurnCommand("합성 대상 홍길동의 생일은 5월 10일이야", "ko"),
            CancellationToken.None);
        var queried = await useCases.SendAsync(
            scope,
            new ChatTurnCommand("홍길동 생일 알려줘", "ko", saved.ConversationId),
            CancellationToken.None);

        Assert.Equal(saved.ConversationId, queried.ConversationId);
        Assert.Equal("홍길동님의 생일은 5월 10일입니다.", queried.Response);
        Assert.Equal(
            [LlmMessageRole.System, LlmMessageRole.User, LlmMessageRole.Assistant, LlmMessageRole.User],
            llm.Requests[1].Messages.Select(message => message.Role).ToArray());
        Assert.Equal("합성 대상 홍길동의 생일은 5월 10일이야", llm.Requests[1].Messages[1].Content);

        var history = await services.GetRequiredService<IConversationUseCases>().ListMessagesAsync(
            scope,
            saved.ConversationId!.Value,
            new PageRequest(1, 10),
            CancellationToken.None);
        Assert.NotNull(history);
        Assert.Equal(4, history.TotalCount);
        Assert.Equal("홍길동님의 생일은 5월 10일입니다.", history.Items[3].Content);

        await using var dbContext = database.CreateContext();
        Assert.Equal(2, await dbContext.StructuredFacts.CountAsync());
    }

    private static async Task SeedTenantAndUsersAsync(
        SqliteTestDatabase database,
        Guid tenantId,
        params Guid[] userIds)
    {
        await using var services = database.CreateScope();
        var encryption = services.ServiceProvider.GetRequiredService<IEncryptionService>();
        var now = DateTime.UtcNow;
        var users = new List<User>(userIds.Length);
        foreach (var userId in userIds)
        {
            var name = Encoding.UTF8.GetBytes($"Synthetic User {userId:N}");
            try
            {
                var payload = await encryption.EncryptAsync(
                    name,
                    new EncryptionContext(tenantId, userId, userId, nameof(User), nameof(User.DisplayName)),
                    CancellationToken.None);
                users.Add(new User(userId, tenantId, payload, now));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(name);
            }
        }

        await using var context = database.CreateContext();
        context.Tenants.Add(new Tenant(tenantId, "Synthetic Tenant", now));
        context.Users.AddRange(users);
        await context.SaveChangesAsync();
    }

    private sealed class QueueChatService(params string[] responses) : ILlmChatService
    {
        private int _responseIndex;

        public List<LlmChatRequest> Requests { get; } = [];

        public Task<LlmChatResponse> CompleteAsync(
            LlmChatRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var content = responses[_responseIndex++];
            return Task.FromResult(new LlmChatResponse(request.ModelName, content, "stop", null));
        }
    }
}
