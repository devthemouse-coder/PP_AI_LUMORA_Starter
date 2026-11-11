# 28. Memory UseCase 설계 및 구현

## 1. 범위와 상태

이 단계에서 LLM 없이 Memory 생성·조회·검색·수정·삭제를 Application UseCase로 구현했습니다. 인증·인가, 자동 파싱, 전문 검색, 프런트엔드 화면은 범위에 포함되지 않습니다.

## 2. 호출 흐름

```text
HTTP 요청 → Contracts DTO → API Endpoint → Application UseCase
          → scope가 지정된 Repository / IUnitOfWork
          → Infrastructure EF Core / SQLite
```

Application은 Domain 및 저장소·암호화 계약만 참조합니다. Infrastructure 구현체는 API 조립 지점에서 DI로 등록합니다. Repository 조회와 저장은 비동기이며 `CancellationToken`을 전달합니다.

## 3. 기능과 데이터 처리

| UseCase | 동작 |
|---|---|
| `CreateAsync` | 입력 길이·점수 검증, 본문 AEAD 암호화, 직접 입력 출처 생성, 단일 트랜잭션 저장 |
| `GetAsync` | Tenant/User 범위로 기억과 Payload 조회 후 복호화 |
| `SearchAsync` | 유형·생성/수정 UTC 시간·활성 여부 필터, 정렬 및 최대 200건 페이지 조회 |
| `UpdateAsync` | 기존 소유권 확인, 이전 본문을 MemoryRevision용 AAD로 재암호화, 새 Payload·출처·버전 저장 |
| `DeleteAsync` | 소유 범위가 일치할 때 기억과 관련 Payload·출처·검색 토큰·Revision을 물리 삭제 |

기억 본문은 `MemoryPayload`에 AES-256-GCM 인증 암호문으로 저장합니다. 수정 Revision에는 이전 본문을 별도의 Entity/Field AAD로 암호화해 기록합니다. 읽기 전용 목록은 페이지별 Payload를 일괄 조회하여 N+1 조회를 피합니다.

## 4. 검색과 범위 보안

- 필터는 유형, UTC 생성/수정 시간, 활성 상태를 지원합니다. 기본 정렬은 수정 시각 내림차순이며 페이지 기본값은 1페이지·50건, 최대 크기는 200입니다.
- 본문은 암호화되므로 자유 텍스트 검색은 제공하지 않습니다. 본문을 평문 검색 열로 복제하지 않습니다.
- Repository 계약에는 `TenantUserScope`가 필수이며 소유자 없는 전체 목록 API를 노출하지 않습니다. 다른 범위의 ID는 존재하지 않는 ID와 동일하게 조회되지 않습니다.
- 현재 API scope는 인증 사용자로부터 만들어지지 않습니다. 개발 환경에서만 명시적으로 설정한 단일 고정 scope를 사용하며 설정이 없거나 유효하지 않으면 503으로 거부합니다. 이 설정은 인증/인가를 대체하지 않으므로 운영에 노출하면 안 됩니다.

## 5. 오류 및 검증

잘못된 입력은 Problem Details 400, 범위 내에 없는 ID는 404, 개발 scope 미설정은 503을 반환합니다. 예외 처리 로그에는 예외 형식과 상태 코드만 기록하고 본문·암호문을 기록하지 않습니다.

실제 SQLite Provider 기반 통합 테스트는 생성·복호화·페이지/유형 검색·다른 사용자 차단·Revision·삭제 및 암호문 비노출을 확인합니다. API 테스트는 scope 미설정 시 fail-closed와 설정된 개발 scope CRUD를 확인합니다.

```powershell
dotnet test tests\Lumora.Infrastructure.IntegrationTests\Lumora.Infrastructure.IntegrationTests.csproj --filter FullyQualifiedName~MemoryUseCaseIntegrationTests
dotnet test tests\Lumora.Api.IntegrationTests\Lumora.Api.IntegrationTests.csproj --filter FullyQualifiedName~MemoryApiTests
```

## 6. 미구현 사항

인증 사용자 컨텍스트, 실제 인가, tombstone, 감사 로그, 전문 검색, 변경 이력 복원 UI, 업무 화면은 미구현입니다. DB 삭제가 완료된 Memory라도 이미 생성된 암호화 백업에는 보존 기간 동안 남을 수 있습니다. 백업 삭제 정책은 [27. 데이터 보존 정책](./27_DATA_RETENTION_POLICY.md)을 따릅니다.
