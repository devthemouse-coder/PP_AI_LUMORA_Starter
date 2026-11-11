# 12. 프로젝트 의존성

## 1. 허용되는 참조

| 참조 프로젝트 | 참조 대상 | 이유 |
|---|---|---|
| `Lumora.Application` | `Lumora.Domain` | 유스케이스가 도메인 규칙과 모델을 사용합니다. |
| `Lumora.Infrastructure` | `Lumora.Application`, `Lumora.Domain` | 인프라 구현이 내부 계약과 도메인 모델을 구현·사용합니다. |
| `Lumora.Api` | `Lumora.Application`, `Lumora.Infrastructure`, `Lumora.Contracts` | HTTP 진입점이 유스케이스를 호출하고 Composition Root에서 구현체를 등록하며 DTO를 매핑합니다. |
| 각 테스트 프로젝트 | 테스트 대상 프로젝트 | 대상 계층의 동작을 검증합니다. |

## 2. 금지되는 참조

- `Lumora.Domain`은 다른 Lumora 프로젝트나 외부 인프라 기술에 의존하지 않습니다.
- `Lumora.Application`은 Infrastructure, API, Contracts에 의존하지 않습니다.
- `Lumora.Infrastructure`는 API와 Contracts에 의존하지 않습니다.
- `Lumora.Contracts`는 다른 Lumora 프로젝트에 의존하지 않습니다.
- 프런트엔드는 .NET 프로젝트를 참조하지 않습니다.
- Domain Entity를 API 응답으로 직접 노출하지 않습니다.

## 3. 의존성 역전

저장소와 암호화 등 Application이 필요로 하는 기능은 Application 내부의 인터페이스로 선언하고, Infrastructure가 SQLite·DPAPI 구현을 제공합니다. API의 Composition Root가 구체 구현을 등록합니다. Domain은 EF Core, SQLite, DPAPI, ASP.NET Core에 의존하지 않습니다.

API 요청·응답 DTO는 `Lumora.Contracts`에 두고, Application의 내부 모델과 분리합니다. Domain에서 Contracts를 참조하지 않으며, 필요한 매핑은 API 경계에서 수행합니다.

프런트엔드의 API 서비스 계층은 REST 요청을 한 곳에 모읍니다. URL과 프런트엔드 설정은 환경 변수로 전달하며 DB나 서버 내부 계층에 직접 접근하지 않습니다.

## 4. 참조 검증

솔루션의 프로젝트 참조는 각 `.csproj`의 `ProjectReference`로 선언합니다. 참조를 추가하거나 계층을 변경할 때 이 문서와 [아키텍처 문서](./04_ARCHITECTURE.md)를 함께 검토하고, 불필요한 역방향 참조가 생기지 않도록 합니다.
