using Lumora.Application.Abstractions.Persistence;

namespace Lumora.Api.Services;

public interface ITenantUserScopeAccessor
{
    TenantUserScope? GetScope();
}
