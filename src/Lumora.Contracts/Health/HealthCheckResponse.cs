namespace Lumora.Contracts.Health;

/// <summary>
/// API 프로세스의 기본 상태를 외부에 전달합니다.
/// </summary>
/// <param name="Status">API 프로세스 상태입니다.</param>
/// <param name="ApplicationName">설정된 애플리케이션 이름입니다.</param>
/// <param name="ServerTime">응답을 생성한 서버 시각(UTC)입니다.</param>
public sealed record HealthCheckResponse(
    string Status,
    string ApplicationName,
    DateTimeOffset ServerTime);
