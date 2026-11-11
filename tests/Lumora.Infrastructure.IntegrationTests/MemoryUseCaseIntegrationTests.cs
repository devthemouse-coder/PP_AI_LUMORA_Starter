using System.Security.Cryptography;
using System.Text;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.Abstractions.Security;
using Lumora.Application.UseCases.Memories;
using Lumora.Application.UseCases.StructuredFacts;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lumora.Infrastructure.IntegrationTests;

public sealed class MemoryUseCaseIntegrationTests
{
    [Fact]
    public async Task Memory_use_cases_encrypt_scope_page_update_and_delete()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.InitializeKeysAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await SeedTenantAndUsersAsync(database, tenantId, userId, otherUserId);
        var scope = new TenantUserScope(tenantId, userId);

        await using var services = database.CreateScope();
        var useCases = services.ServiceProvider.GetRequiredService<IMemoryUseCases>();
        var first = await useCases.CreateAsync(
            scope,
            new CreateMemoryCommand(MemoryType.Preference, "synthetic memory one"),
            CancellationToken.None);
        var second = await useCases.CreateAsync(
            scope,
            new CreateMemoryCommand(MemoryType.Fact, "synthetic memory two"),
            CancellationToken.None);

        Assert.Equal("synthetic memory one", (await useCases.GetAsync(scope, first.Id, CancellationToken.None))?.Content);
        Assert.Null(await useCases.GetAsync(
            new TenantUserScope(tenantId, otherUserId),
            first.Id,
            CancellationToken.None));

        var page = await useCases.SearchAsync(
            scope,
            new MemorySearch(MemoryType: MemoryType.Preference),
            new PageRequest(1, 1),
            CancellationToken.None);
        Assert.Equal(1, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(first.Id, page.Items[0].Id);
        var firstPage = await useCases.SearchAsync(
            scope,
            new MemorySearch(),
            new PageRequest(1, 1),
            CancellationToken.None);
        var secondPage = await useCases.SearchAsync(
            scope,
            new MemorySearch(),
            new PageRequest(2, 1),
            CancellationToken.None);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Single(firstPage.Items);
        Assert.Single(secondPage.Items);
        Assert.NotEqual(firstPage.Items[0].Id, secondPage.Items[0].Id);

        var updated = await useCases.UpdateAsync(
            scope,
            first.Id,
            new UpdateMemoryCommand(
                MemoryType.Preference,
                "synthetic memory revised",
                9_000,
                MemoryVerificationStatus.Verified,
                MemorySensitivity.Personal),
            CancellationToken.None);
        Assert.Equal("synthetic memory revised", updated?.Content);

        await using (var context = database.CreateContext())
        {
            var revision = await context.MemoryRevisions.SingleAsync(item => item.MemoryId == first.Id);
            Assert.Equal(1, revision.RevisionNumber);
            Assert.DoesNotContain(
                "synthetic memory one",
                Encoding.UTF8.GetString(revision.PreviousValue.Ciphertext));
            var payload = await context.MemoryPayloads.SingleAsync(item => item.MemoryId == first.Id);
            Assert.DoesNotContain(
                "synthetic memory revised",
                Encoding.UTF8.GetString(payload.Value.Ciphertext));
        }

        Assert.True(await useCases.DeleteAsync(scope, first.Id, CancellationToken.None));
        Assert.False(await useCases.DeleteAsync(scope, first.Id, CancellationToken.None));
        Assert.Null(await useCases.GetAsync(scope, first.Id, CancellationToken.None));
        Assert.NotNull(await useCases.GetAsync(scope, second.Id, CancellationToken.None));
        await AssertDatabaseIntegrityAsync(database);
    }

    [Fact]
    public async Task Structured_fact_use_cases_search_dates_detect_conflicts_and_record_revisions()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.InitializeKeysAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await SeedTenantAndUsersAsync(database, tenantId, userId, otherUserId);
        var scope = new TenantUserScope(tenantId, userId);
        var otherScope = new TenantUserScope(tenantId, otherUserId);

        await using var services = database.CreateScope();
        var useCases = services.ServiceProvider.GetRequiredService<IStructuredFactUseCases>();
        var yearlessBirthday = await useCases.CreateAsync(
            scope,
            new CreateStructuredFactCommand(
                "Person.Birthday",
                new StructuredFactValue("Synthetic Person", Month: 11, Day: 12)),
            CancellationToken.None);
        var leapBirthday = await useCases.CreateAsync(
            scope,
            new CreateStructuredFactCommand(
                "Person.Birthday",
                new StructuredFactValue("Leap Person", Year: 2000, Month: 2, Day: 29)),
            CancellationToken.None);
        var yearlessLeapBirthday = await useCases.CreateAsync(
            scope,
            new CreateStructuredFactCommand(
                "Person.Birthday",
                new StructuredFactValue("Yearless Leap Person", Month: 2, Day: 29)),
            CancellationToken.None);
        await useCases.CreateAsync(
            otherScope,
            new CreateStructuredFactCommand(
                "Person.Birthday",
                new StructuredFactValue("Other User", Month: 11, Day: 12)),
            CancellationToken.None);

        var november = await useCases.SearchAsync(
            scope,
            new StructuredFactSearch(BirthdayMonth: 11),
            new PageRequest(1, 10),
            CancellationToken.None);
        Assert.Equal(1, november.TotalCount);
        Assert.Equal(yearlessBirthday.MemoryId, Assert.Single(november.Items).MemoryId);

        var february = await useCases.SearchAsync(
            scope,
            new StructuredFactSearch(BirthdayMonth: 2),
            new PageRequest(1, 10),
            CancellationToken.None);
        Assert.Equal(2, february.TotalCount);
        Assert.Contains(february.Items, item => item.MemoryId == leapBirthday.MemoryId);
        Assert.Contains(february.Items, item => item.MemoryId == yearlessLeapBirthday.MemoryId);
        Assert.Null(await useCases.GetAsync(otherScope, yearlessBirthday.MemoryId, CancellationToken.None));

        var subjectSearch = await useCases.SearchAsync(
            scope,
            new StructuredFactSearch(Subject: "synthetic person"),
            new PageRequest(1, 10),
            CancellationToken.None);
        Assert.Equal(yearlessBirthday.MemoryId, Assert.Single(subjectSearch.Items).MemoryId);

        var exactValueSearch = await useCases.SearchAsync(
            scope,
            new StructuredFactSearch(ExactValue: "11-12"),
            new PageRequest(1, 10),
            CancellationToken.None);
        Assert.Equal(yearlessBirthday.MemoryId, Assert.Single(exactValueSearch.Items).MemoryId);

        var factTypeSearch = await useCases.SearchAsync(
            scope,
            new StructuredFactSearch(FactType: "Person.Birthday"),
            new PageRequest(1, 10),
            CancellationToken.None);
        Assert.Equal(3, factTypeSearch.TotalCount);

        var dateRange = await useCases.SearchAsync(
            scope,
            new StructuredFactSearch(DateFrom: new DateOnly(2000, 2, 29), DateTo: new DateOnly(2000, 2, 29)),
            new PageRequest(1, 10),
            CancellationToken.None);
        Assert.Equal(leapBirthday.MemoryId, Assert.Single(dateRange.Items).MemoryId);
        var firstFactPage = await useCases.SearchAsync(
            scope,
            new StructuredFactSearch(),
            new PageRequest(1, 1),
            CancellationToken.None);
        var secondFactPage = await useCases.SearchAsync(
            scope,
            new StructuredFactSearch(),
            new PageRequest(2, 1),
            CancellationToken.None);
        Assert.Equal(3, firstFactPage.TotalCount);
        Assert.Single(firstFactPage.Items);
        Assert.Single(secondFactPage.Items);
        Assert.NotEqual(firstFactPage.Items[0].MemoryId, secondFactPage.Items[0].MemoryId);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => useCases.SearchAsync(
            scope,
            new StructuredFactSearch(BirthdayMonth: 13),
            new PageRequest(1, 10),
            CancellationToken.None));

        await Assert.ThrowsAsync<DuplicateStructuredFactException>(() => useCases.CreateAsync(
            scope,
            new CreateStructuredFactCommand(
                "Person.Birthday",
                new StructuredFactValue("Synthetic Person", Month: 11, Day: 12)),
            CancellationToken.None));
        await Assert.ThrowsAsync<ConflictingStructuredFactException>(() => useCases.CreateAsync(
            scope,
            new CreateStructuredFactCommand(
                "Person.Birthday",
                new StructuredFactValue("Synthetic Person", Month: 11, Day: 13)),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => useCases.CreateAsync(
            scope,
            new CreateStructuredFactCommand(
                "Person.Birthday",
                new StructuredFactValue("Invalid Leap Date", Year: 2023, Month: 2, Day: 29)),
            CancellationToken.None));

        var updated = await useCases.UpdateAsync(
            scope,
            yearlessBirthday.MemoryId,
            new UpdateStructuredFactCommand(
                "Person.Birthday",
                new StructuredFactValue("Synthetic Person", Month: 12, Day: 1)),
            CancellationToken.None);
        Assert.Equal(12, updated?.Value.Month);

        await using (var context = database.CreateContext())
        {
            var revision = await context.MemoryRevisions.SingleAsync(item => item.MemoryId == yearlessBirthday.MemoryId);
            Assert.Equal(1, revision.RevisionNumber);
            var storedFact = await context.StructuredFacts.SingleAsync(item => item.MemoryId == yearlessBirthday.MemoryId);
            Assert.DoesNotContain("Synthetic Person", Encoding.UTF8.GetString(storedFact.Value.Ciphertext));
            Assert.Equal(5, await context.MemoryLookupTokens.CountAsync(item => item.MemoryId == yearlessBirthday.MemoryId));
        }

        Assert.True(await useCases.DeleteAsync(scope, yearlessBirthday.MemoryId, CancellationToken.None));
        Assert.Null(await useCases.GetAsync(scope, yearlessBirthday.MemoryId, CancellationToken.None));
        await using var verify = database.CreateContext();
        Assert.False(await verify.MemoryRevisions.AnyAsync(item => item.MemoryId == yearlessBirthday.MemoryId));
        Assert.False(await verify.StructuredFacts.AnyAsync(item => item.MemoryId == yearlessBirthday.MemoryId));
        await AssertDatabaseIntegrityAsync(database);
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

    private static async Task AssertDatabaseIntegrityAsync(SqliteTestDatabase database)
    {
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();
        await using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = await integrity.ExecuteScalarAsync();
            Assert.Equal("ok", result);
        }

        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            await using var reader = await foreignKeys.ExecuteReaderAsync();
            Assert.False(await reader.ReadAsync());
        }
    }
}
