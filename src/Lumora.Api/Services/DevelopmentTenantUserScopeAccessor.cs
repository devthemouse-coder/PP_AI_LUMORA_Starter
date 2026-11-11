using Lumora.Application.Abstractions.Persistence;

namespace Lumora.Api.Services;

internal sealed class DevelopmentTenantUserScopeAccessor(
    IConfiguration configuration,
    IHostEnvironment environment) : ITenantUserScopeAccessor
{
    public TenantUserScope? GetScope()
    {
        if (!environment.IsDevelopment() ||
            !bool.TryParse(configuration["MemoryApi:EnableDevelopmentScope"], out var enabled) ||
            !enabled ||
            !Guid.TryParse(configuration["MemoryApi:TenantId"], out var tenantId) ||
            !Guid.TryParse(configuration["MemoryApi:UserId"], out var userId) ||
            tenantId == Guid.Empty ||
            userId == Guid.Empty)
        {
            return null;
        }

        return new TenantUserScope(tenantId, userId);
    }
}
