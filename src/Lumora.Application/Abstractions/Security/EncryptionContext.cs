namespace Lumora.Application.Abstractions.Security;

/// <summary>암호문을 특정 소유자·레코드·필드에 결합하는 추가 인증 문맥입니다.</summary>
public sealed record EncryptionContext(
    Guid TenantId,
    Guid UserId,
    Guid RecordId,
    string EntityType,
    string FieldName);
