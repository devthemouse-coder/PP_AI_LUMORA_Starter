using Lumora.Application.Abstractions.Persistence;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lumora.Infrastructure.IntegrationTests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task Migration_creates_expected_tables_indexes_and_enables_foreign_keys()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var dbContext = database.CreateContext();

        var tables = await ReadNamesAsync(dbContext, "table");
        var indexes = await ReadNamesAsync(dbContext, "index");
        var foreignKeyState = await ReadScalarAsync(dbContext, "PRAGMA foreign_keys;");
        var foreignKeyViolations = await ReadRowsAsync(dbContext, "PRAGMA foreign_key_check;");

        Assert.Equal(1L, foreignKeyState);
        Assert.Empty(foreignKeyViolations);
        Assert.Contains("Tenants", tables);
        Assert.Contains("Users", tables);
        Assert.Contains("Conversations", tables);
        Assert.Contains("Messages", tables);
        Assert.Contains("Memories", tables);
        Assert.Contains("MemoryPayloads", tables);
        Assert.Contains("StructuredFacts", tables);
        Assert.Contains("MemorySources", tables);
        Assert.Contains("MemoryRevisions", tables);
        Assert.Contains("MemoryLookupTokens", tables);
        Assert.Contains("IX_Messages_TenantId_UserId_ConversationId_SequenceNumber", indexes);
        Assert.Contains("IX_MemoryRevisions_TenantId_UserId_MemoryId_RevisionNumber", indexes);
        Assert.Single(await dbContext.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Repositories_enforce_user_scope_and_page_results()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var firstMemoryId = Guid.NewGuid();
        var secondMemoryId = Guid.NewGuid();
        var otherMemoryId = Guid.NewGuid();
        var searchToken = Enumerable.Repeat((byte)7, 32).ToArray();
        var now = DateTime.UtcNow;

        await using (var dbContext = database.CreateContext())
        {
            dbContext.Tenants.Add(new Tenant(tenantId, "합성 테스트 조직", now));
            dbContext.Users.AddRange(
                new User(userId, tenantId, CreatePayload(), now),
                new User(otherUserId, tenantId, CreatePayload(), now));
            dbContext.Memories.AddRange(
                new MemoryItem(firstMemoryId, tenantId, userId, MemoryType.Fact, now),
                new MemoryItem(secondMemoryId, tenantId, userId, MemoryType.Preference, now),
                new MemoryItem(otherMemoryId, tenantId, otherUserId, MemoryType.Work, now));
            dbContext.MemoryPayloads.Add(
                new MemoryPayload(tenantId, userId, firstMemoryId, CreatePayload()));
            dbContext.MemoryLookupTokens.AddRange(
                new MemoryLookupToken(
                    Guid.NewGuid(),
                    tenantId,
                    userId,
                    firstMemoryId,
                    LookupTokenType.BirthdayMonth,
                    "test-key",
                    searchToken),
                new MemoryLookupToken(
                    Guid.NewGuid(),
                    tenantId,
                    otherUserId,
                    otherMemoryId,
                    LookupTokenType.BirthdayMonth,
                    "test-key",
                    searchToken));
            await dbContext.SaveChangesAsync();
        }

        await using (var scope = database.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IMemoryRepository>();
            var userScope = new TenantUserScope(tenantId, userId);
            var ownPage = await repository.ListAsync(
                userScope,
                new PageRequest(1, 1),
                CancellationToken.None);
            var otherUserResult = await repository.GetByIdAsync(
                new TenantUserScope(tenantId, otherUserId),
                firstMemoryId,
                CancellationToken.None);
            await Assert.ThrowsAsync<ArgumentException>(() => repository.AddAsync(
                new TenantUserScope(tenantId, otherUserId),
                new MemoryItem(Guid.NewGuid(), tenantId, userId, MemoryType.Fact, now),
                new MemoryPayload(tenantId, userId, Guid.NewGuid(), CreatePayload()),
                new MemorySource(
                    Guid.NewGuid(),
                    tenantId,
                    userId,
                    Guid.NewGuid(),
                    MemorySourceType.DirectInput,
                    10_000,
                    now),
                [],
                CancellationToken.None));

            var memory = await repository.GetByIdAsync(
                userScope,
                firstMemoryId,
                CancellationToken.None);
            Assert.NotNull(memory);
            memory.UpdateMetadata(
                8_000,
                MemoryVerificationStatus.Verified,
                MemorySensitivity.Personal,
                now.AddMinutes(1),
                null,
                null);
            await repository.UpdateAsync(
                userScope,
                memory,
                new MemoryPayload(tenantId, userId, firstMemoryId, CreatePayload(5)),
                new MemoryRevision(
                    Guid.NewGuid(),
                    tenantId,
                    userId,
                    firstMemoryId,
                    1,
                    MemoryChangeType.Updated,
                    CreatePayload(),
                    now.AddMinutes(1),
                    userId),
                new MemorySource(
                    Guid.NewGuid(),
                    tenantId,
                    userId,
                    firstMemoryId,
                    MemorySourceType.UserCorrection,
                    10_000,
                    now.AddMinutes(1)),
                [
                    new MemoryLookupToken(
                        Guid.NewGuid(),
                        tenantId,
                        userId,
                        firstMemoryId,
                        LookupTokenType.BirthdayMonth,
                        "test-key",
                        searchToken)
                ],
                CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            var updatedPayload = await repository.GetPayloadByIdAsync(
                userScope,
                firstMemoryId,
                CancellationToken.None);
            var revisions = await repository.ListRevisionsAsync(
                userScope,
                firstMemoryId,
                new PageRequest(1, 10),
                CancellationToken.None);

            Assert.Equal(2, ownPage.TotalCount);
            Assert.Single(ownPage.Items);
            Assert.Null(otherUserResult);
            Assert.NotNull(updatedPayload);
            Assert.Equal(CreatePayload(5).Ciphertext, updatedPayload.Value.Ciphertext);
            Assert.Single(revisions.Items);

            var tokenRepository = scope.ServiceProvider.GetRequiredService<IMemoryLookupTokenRepository>();
            var matchedIds = await tokenRepository.FindMemoryIdsAsync(
                new TenantUserScope(tenantId, userId),
                LookupTokenType.BirthdayMonth,
                "test-key",
                searchToken,
                new PageRequest(1, 10),
                CancellationToken.None);
            Assert.Equal(firstMemoryId, Assert.Single(matchedIds.Items));
        }
    }

    [Fact]
    public async Task Composite_foreign_keys_reject_cross_owner_relationships()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var memoryId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var dbContext = database.CreateContext();
        dbContext.Tenants.Add(new Tenant(tenantId, "합성 테스트 조직", now));
        dbContext.Users.AddRange(
            new User(userId, tenantId, CreatePayload(), now),
            new User(otherUserId, tenantId, CreatePayload(), now));
        dbContext.Conversations.Add(new Conversation(conversationId, tenantId, otherUserId, now));
        dbContext.Memories.Add(new MemoryItem(memoryId, tenantId, userId, MemoryType.Fact, now));
        await dbContext.SaveChangesAsync();

        dbContext.Messages.Add(new Message(
            Guid.NewGuid(),
            tenantId,
            userId,
            conversationId,
            1,
            MessageRole.User,
            CreatePayload(),
            now));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        dbContext.ChangeTracker.Clear();

        dbContext.MemoryPayloads.Add(new MemoryPayload(tenantId, otherUserId, memoryId, CreatePayload()));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_a_memory_removes_payload_fact_source_revision_and_tokens()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var memoryId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var dbContext = database.CreateContext())
        {
            dbContext.Tenants.Add(new Tenant(tenantId, "합성 테스트 조직", now));
            dbContext.Users.Add(new User(userId, tenantId, CreatePayload(), now));
            dbContext.Memories.Add(new MemoryItem(memoryId, tenantId, userId, MemoryType.Fact, now));
            dbContext.MemoryPayloads.Add(new MemoryPayload(tenantId, userId, memoryId, CreatePayload()));
            dbContext.StructuredFacts.Add(
                new StructuredFact(tenantId, userId, memoryId, "Person.Birthday", CreatePayload()));
            dbContext.MemorySources.Add(new MemorySource(
                sourceId,
                tenantId,
                userId,
                memoryId,
                MemorySourceType.DirectInput,
                10_000,
                now));
            dbContext.MemoryRevisions.Add(new MemoryRevision(
                Guid.NewGuid(),
                tenantId,
                userId,
                memoryId,
                1,
                MemoryChangeType.Updated,
                CreatePayload(),
                now,
                userId,
                sourceId));
            dbContext.MemoryLookupTokens.Add(new MemoryLookupToken(
                Guid.NewGuid(),
                tenantId,
                userId,
                memoryId,
                LookupTokenType.BirthdayMonth,
                "test-key",
                new byte[32]));
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = database.CreateContext())
        {
            await using var scope = database.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMemoryRepository>().DeleteAsync(
                new TenantUserScope(tenantId, userId),
                memoryId,
                CancellationToken.None);
            Assert.Equal(0, await dbContext.SaveChangesAsync());
        }

        await using (var dbContext = database.CreateContext())
        {
            Assert.False(await dbContext.Memories.AnyAsync());
            Assert.False(await dbContext.MemoryPayloads.AnyAsync());
            Assert.False(await dbContext.StructuredFacts.AnyAsync());
            Assert.False(await dbContext.MemorySources.AnyAsync());
            Assert.False(await dbContext.MemoryRevisions.AnyAsync());
            Assert.False(await dbContext.MemoryLookupTokens.AnyAsync());
        }
    }

    [Fact]
    public async Task Deleting_a_conversation_removes_messages_and_detaches_memory_sources()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var memoryId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var dbContext = database.CreateContext())
        {
            dbContext.Tenants.Add(new Tenant(tenantId, "합성 테스트 조직", now));
            dbContext.Users.Add(new User(userId, tenantId, CreatePayload(), now));
            dbContext.Conversations.Add(new Conversation(conversationId, tenantId, userId, now));
            dbContext.Messages.Add(new Message(
                messageId,
                tenantId,
                userId,
                conversationId,
                1,
                MessageRole.User,
                CreatePayload(),
                now));
            dbContext.Memories.Add(new MemoryItem(memoryId, tenantId, userId, MemoryType.Fact, now));
            dbContext.MemorySources.Add(new MemorySource(
                sourceId,
                tenantId,
                userId,
                memoryId,
                MemorySourceType.ConversationMessage,
                10_000,
                now,
                conversationId,
                messageId));
            await dbContext.SaveChangesAsync();
        }

        await using (var scope = database.CreateScope())
        {
            var conversationRepository = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.ExecuteInTransactionAsync(
                token => conversationRepository.DeleteAsync(
                    new TenantUserScope(tenantId, userId),
                    conversationId,
                    token),
                CancellationToken.None);
        }

        await using (var dbContext = database.CreateContext())
        {
            var source = await dbContext.MemorySources.SingleAsync(candidate => candidate.Id == sourceId);
            Assert.Null(source.ConversationId);
            Assert.Null(source.MessageId);
            Assert.False(await dbContext.Conversations.AnyAsync());
            Assert.False(await dbContext.Messages.AnyAsync());
            Assert.True(await dbContext.Memories.AnyAsync(memory => memory.Id == memoryId));
        }
    }

    [Fact]
    public async Task Deleting_a_user_or_tenant_removes_owned_rows_and_source_links()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var firstTenantId = Guid.NewGuid();
        var firstUserId = Guid.NewGuid();
        var firstConversationId = Guid.NewGuid();
        var firstMessageId = Guid.NewGuid();
        var firstMemoryId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var dbContext = database.CreateContext())
        {
            dbContext.Tenants.AddRange(
                new Tenant(firstTenantId, "합성 테스트 조직 A", now),
                new Tenant(secondTenantId, "합성 테스트 조직 B", now));
            dbContext.Users.AddRange(
                new User(firstUserId, firstTenantId, CreatePayload(), now),
                new User(secondUserId, secondTenantId, CreatePayload(), now));
            dbContext.Conversations.Add(new Conversation(firstConversationId, firstTenantId, firstUserId, now));
            dbContext.Messages.Add(new Message(
                firstMessageId,
                firstTenantId,
                firstUserId,
                firstConversationId,
                1,
                MessageRole.User,
                CreatePayload(),
                now));
            dbContext.Memories.Add(new MemoryItem(firstMemoryId, firstTenantId, firstUserId, MemoryType.Fact, now));
            dbContext.MemoryPayloads.Add(
                new MemoryPayload(firstTenantId, firstUserId, firstMemoryId, CreatePayload()));
            dbContext.MemoryLookupTokens.Add(new MemoryLookupToken(
                Guid.NewGuid(),
                firstTenantId,
                firstUserId,
                firstMemoryId,
                LookupTokenType.FactType,
                "test-key",
                new byte[32]));
            dbContext.MemorySources.Add(new MemorySource(
                Guid.NewGuid(),
                firstTenantId,
                firstUserId,
                firstMemoryId,
                MemorySourceType.ConversationMessage,
                10_000,
                now,
                firstConversationId,
                firstMessageId));
            await dbContext.SaveChangesAsync();
        }

        await using (var scope = database.CreateScope())
        {
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.ExecuteInTransactionAsync(
                token => userRepository.DeleteAsync(firstTenantId, firstUserId, token),
                CancellationToken.None);
        }

        await using (var scope = database.CreateScope())
        {
            var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.ExecuteInTransactionAsync(
                token => tenantRepository.DeleteAsync(secondTenantId, token),
                CancellationToken.None);
        }

        await using (var dbContext = database.CreateContext())
        {
            Assert.True(await dbContext.Tenants.AnyAsync(tenant => tenant.Id == firstTenantId));
            Assert.False(await dbContext.Tenants.AnyAsync(tenant => tenant.Id == secondTenantId));
            Assert.False(await dbContext.Users.AnyAsync(user =>
                user.Id == firstUserId || user.Id == secondUserId));
            Assert.False(await dbContext.Conversations.AnyAsync(conversation =>
                conversation.Id == firstConversationId));
            Assert.False(await dbContext.Messages.AnyAsync(message =>
                message.Id == firstMessageId));
            Assert.False(await dbContext.Memories.AnyAsync(memory =>
                memory.Id == firstMemoryId));
            Assert.False(await dbContext.MemoryPayloads.AnyAsync(payload =>
                payload.MemoryId == firstMemoryId));
            Assert.False(await dbContext.MemoryLookupTokens.AnyAsync(token =>
                token.MemoryId == firstMemoryId));
            Assert.False(await dbContext.MemorySources.AnyAsync(source =>
                source.TenantId == firstTenantId || source.TenantId == secondTenantId));
        }
    }

    [Fact]
    public async Task Concurrent_memory_updates_are_detected_and_times_are_materialized_as_utc()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var memoryId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var seedContext = database.CreateContext())
        {
            seedContext.Tenants.Add(new Tenant(tenantId, "합성 테스트 조직", now));
            seedContext.Users.Add(new User(userId, tenantId, CreatePayload(), now));
            seedContext.Memories.Add(new MemoryItem(memoryId, tenantId, userId, MemoryType.Fact, now));
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstCopy = await firstContext.Memories.SingleAsync();
        var secondCopy = await secondContext.Memories.SingleAsync();
        var scope = new TenantUserScope(tenantId, userId);
        firstCopy.UpdateMetadata(8_000, MemoryVerificationStatus.Verified, MemorySensitivity.Personal, now.AddMinutes(1), null, null);
        secondCopy.UpdateMetadata(7_000, MemoryVerificationStatus.Verified, MemorySensitivity.Personal, now.AddMinutes(2), null, null);

        await firstContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());

        firstContext.ChangeTracker.Clear();
        var reloaded = await firstContext.Memories.SingleOrDefaultAsync(
            memory => memory.TenantId == scope.TenantId &&
                      memory.UserId == scope.UserId &&
                      memory.Id == memoryId);
        Assert.NotNull(reloaded);
        Assert.Equal(DateTimeKind.Utc, reloaded.CreatedAtUtc.Kind);
        Assert.Equal(2, reloaded.Version);
        Assert.Equal(8_000, reloaded.ConfidenceScore);
    }

    private static EncryptedPayload CreatePayload(byte value = 1) =>
        new(1, "test-key", new byte[12], [value, 2, 3], new byte[16]);

    private static async Task<HashSet<string>> ReadNamesAsync(LumoraDbContext dbContext, string type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        await dbContext.Database.OpenConnectionAsync();
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$type";
        parameter.Value = type;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<long> ReadScalarAsync(LumoraDbContext dbContext, string sql)
    {
        await dbContext.Database.OpenConnectionAsync();
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<List<string>> ReadRowsAsync(LumoraDbContext dbContext, string sql)
    {
        var rows = new List<string>();
        await dbContext.Database.OpenConnectionAsync();
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
