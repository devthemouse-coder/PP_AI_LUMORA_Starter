namespace Lumora.Application.Abstractions.Persistence;

/// <summary>저장소 접근에 필요한 명시적 테넌트·사용자 범위입니다.</summary>
public readonly record struct TenantUserScope
{
    public TenantUserScope(Guid tenantId, Guid userId)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("테넌트와 사용자 식별자는 비어 있을 수 없습니다.");
        }

        TenantId = tenantId;
        UserId = userId;
    }

    public Guid TenantId { get; }

    public Guid UserId { get; }
}
