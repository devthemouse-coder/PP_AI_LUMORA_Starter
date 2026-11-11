# 14. 데이터베이스 아키텍처

## 1. 범위와 결정 상태

이 문서는 데이터베이스 논리·운영 아키텍처와 실제 SQLite 구현 경계를 함께 기록합니다. EF Core 10과 SQLite는 구현되었으며 SQL Server는 설계만 있고 Provider·Migration은 구현하지 않았습니다.

| 항목 | 설계 방향 | 상태 |
|---|---|---|
| ORM | Entity Framework Core 10 | 채택 |
| 개인용 Provider | SQLite | 초기 구현 대상 |
| 기업용 Provider | Microsoft SQL Server | 별도 검증 후 추가 |
| 비즈니스 DB 경계 | Application의 범위 지정 Repository/Unit of Work 계약 | SQLite 계약 구현 |
| DB 구현과 선택 | Infrastructure 등록 확장 + API Composition Root | 채택 |
| Dapper | 도입하지 않음 | 실측 병목이 확인되면 특정 조회에 한해 재검토 |
| Vector DB | 도입하지 않음 | 이 단계 범위 밖 |

## 2. 구성과 의존성

```text
Lumora.Api (Composition Root)
  └─ Database:Provider 선택 및 Infrastructure 등록
      └─ Lumora.Infrastructure
          ├─ 공통 EF Core 모델과 Repository 구현
          ├─ Providers/Sqlite
          └─ Providers/SqlServer
                  │
                  └─ Application Repository/Unit of Work 계약
                      └─ Domain 모델
```

Application에는 `IQueryable`, `DbContext`, `DbSet`, EF 예외, 연결 문자열, Provider별 자료형을 노출하지 않습니다. API는 설정을 검증하고 Infrastructure의 등록 API를 호출합니다. Provider 구현은 Infrastructure 안에 둡니다.

## 3. Provider 선택 및 연결 설정

- Provider 이름은 `Database:Provider`로 명시합니다. 현재 API는 `Sqlite`만 허용하며 SQL Server 또는 잘못된 값은 시작 시 명확한 설정 오류로 처리하고 SQLite로 조용히 대체하지 않습니다.
- 개인 개발·단독 설치의 기본 배포 설정은 SQLite를 명시합니다. 기업용 배포는 SQL Server를 명시적으로 선택합니다.
- SQLite 경로는 `ConnectionStrings:Sqlite`, `Database:Sqlite:DataDirectory`, `LUMORA_DATA_DIRECTORY` 순으로 확인하고 기본 위치는 사용자별 LocalAppData입니다. 파일 경로는 로그에 노출하지 않으며 DB와 백업 파일은 소스 저장소에 넣지 않습니다.
- SQL Server 연결 문자열은 구성 계층(`ConnectionStrings:SqlServer`)에서 읽습니다. 자격 증명은 환경 변수·비밀 저장소·조직의 서비스 계정에서 공급하고 JSON 설정과 로그에 평문으로 기록하지 않습니다.
- API Composition Root는 선택한 Provider와 연결 설정을 Infrastructure에 전달해 한 번만 등록합니다. 런타임에 Provider를 자동 전환하거나 데이터베이스를 자동 생성하지 않습니다.
- 연결 문자열 또는 사용자 데이터는 예외 메시지와 구조화 로그에서 제거합니다.

## 4. DbContext 및 Repository 수명

- `LumoraDbContext`는 요청 범위(scope)로 등록하고 요청 간 공유하지 않습니다. DbContext는 thread-safe가 아니므로 한 컨텍스트의 동시 작업을 `Task.WhenAll`로 병렬 실행하지 않습니다.
- 설계 목표는 하나의 공통 모델과 Infrastructure 내부의 Provider 설정입니다. SQL 문법·타입 차이를 공통으로 표현할 수 없으면 Provider 내부의 좁은 구현으로 격리하고, 지원할 수 없는 기능은 명시적 오류를 반환합니다.
- Repository 계약은 Application에 두고 사용자 소유 데이터의 읽기·쓰기·삭제 호출은 `TenantUserScope` 또는 tenant/user ID를 요구합니다. 범용 `IQueryable`, 임의 SQL 입력 인터페이스는 만들지 않습니다.
- 읽기 작업은 전용 DTO/읽기 모델로 투영하고, 변경 추적이 필요 없는 쿼리는 `AsNoTracking()`을 적용합니다. Domain Entity를 API에 그대로 반환하지 않습니다.
- 구체 Repository, 매핑, EF 구성, Provider 전용 SQL은 Infrastructure에 둡니다. Domain은 EF Core 및 DB Provider를 참조하지 않습니다.

## 5. 저장, 트랜잭션 및 비동기

- DB I/O는 비동기 EF Core API와 호출자 `CancellationToken`을 사용합니다.
- 하나의 `SaveChangesAsync`에 함께 포함한 기억 본문, 인덱스 토큰, 출처, 개정 이력 변경은 한 원자적 저장 단위로 처리합니다.
- `IUnitOfWork.ExecuteInTransactionAsync`는 여러 Repository 작업을 SQLite 트랜잭션으로 묶고 작업 완료 시 변경 사항을 저장합니다. 여러 저장 구간을 포함하는 삭제 등 유스케이스는 이 계약 안에서 실행해야 합니다.
- SQLite의 단일 작성자 제약·잠금 대기와 SQL Server의 잠금·실행 전략을 동일하게 가정하지 않습니다. 재시도는 멱등성, 재실행 경계, 시간 제한을 확인한 Provider 경계에서만 구성합니다. 취소된 작업과 비멱등 작업을 무조건 재시도하지 않습니다.
- 낙관적 동시성은 Provider 공통 정수 `Version` 값으로 검사합니다. 갱신 시 버전을 조건에 포함하고 불일치는 충돌로 돌려 확인·재조회하게 합니다. SQL Server 전용 `rowversion`에 의존하지 않습니다.

## 6. 권장 Infrastructure 배치

```text
Lumora.Infrastructure/
  Persistence/
    Configurations/
    Context/
    Repositories/
    Sqlite/
    Migrations/
      Sqlite/                 # 또는 별도 SQLite Migration 어셈블리
  Security/
    KeyManagement/
```

SQL Server 지원 승인 후 추가할 경로:

```text
Lumora.Infrastructure/
  Persistence/
    Providers/
      SqlServer/
    Migrations/
      SqlServer/              # 또는 별도 SQL Server Migration 어셈블리
```

EF Core는 Migration 탐색을 어셈블리 단위로 설정합니다. 양 Provider의 Migration을 한 어셈블리/한 이력에 무분별하게 섞지 않습니다. EF 도구가 Provider별 집합을 안정적으로 구분하지 못하거나 독립 배포가 필요하면, 후속 설계에서 `Lumora.Infrastructure.Migrations.Sqlite`와 `Lumora.Infrastructure.Migrations.SqlServer` 같은 별도 Migration 프로젝트/어셈블리를 사용합니다. 실제 코드 구조는 EF 도구 버전과 첫 Provider 구현으로 검증한 뒤 확정합니다.

## 7. Migration과 기존 데이터

- SQLite Migration은 SQLite용 Provider와 설계된 SQLite Migration만 적용합니다. SQL Server Migration은 해당 SQL Server Provider 및 별도 Migration 집합으로 생성·검증합니다. 한 DB에서 생성한 Migration을 다른 DB에 그대로 적용하지 않습니다.
- 개인용 SQLite는 실행 중 DB 파일을 임의 복사하지 않습니다. Migration 전에 SQLite 온라인 백업 API 또는 검증된 백업 명령으로 일관된 백업을 만들고, Migration 후 `integrity_check`, `foreign_key_check`, 앱 수준 검증 및 복원 점검을 수행합니다.
- 기업용 SQL Server는 Migration을 운영 앱 시작 시 자동 적용하지 않습니다. DBA/배포 단계에서 생성된 검토 가능한 SQL 또는 Migration Bundle을 백업·승인·점검 창구와 함께 적용합니다.
- EF 기본 Migration 이력 테이블(기본 `__EFMigrationsHistory`)을 Provider별 스키마 버전의 기준으로 사용합니다. 별도 `SchemaVersion` 테이블은 그 값만 중복 저장하지 않습니다.
- SQLite 초기 Migration은 `Persistence/Migrations/Sqlite`에 두고 `dotnet ef database update`로 명시 적용합니다. API 시작 시 자동 적용하지 않습니다. 운영 데이터의 Migration 전 백업/복구 도구는 별도 준비가 필요합니다.
- 기존 데이터의 의미·인코딩·제약을 조사하고 단계적 변경(add → backfill/검증 → 제약 전환 → 불필요 컬럼 제거)으로 전개합니다. 암호화 변환은 배치 진행 상태, 재실행 가능성, 키 보유, 데이터 비교 검증을 포함합니다.
- Migration 실패 시 백업을 보존하고 원인을 먼저 진단합니다. 파괴적인 자동 다운그레이드를 수행하지 않습니다. 호환 가능한 복구 Migration 또는 승인된 백업 복원 후 재검증을 사용합니다. 롤백 불가 DDL은 적용 전에 표시하고 테스트합니다.

구체 절차와 Provider 차이는 [18. 데이터베이스 성능 및 운영](./18_DATABASE_PERFORMANCE.md)과 실제 배포 문서에서 함께 유지합니다.

## 8. 초기화 및 상태 확인

- DB 생성, Migration 적용, 시드 사용자/테넌트 생성은 API 시작 시 암묵적으로 실행하지 않습니다. 개발 환경의 명시적 명령 및 기업 배포 도구로 분리합니다.
- SQLite 사용 시 `PRAGMA foreign_keys=ON`을 실제 연결마다 활성화하고 시작/통합 테스트에서 FK 검사로 확인합니다. FK 제약 생성만으로 런타임 FK 집행이 보장된다고 가정하지 않습니다.
- Liveness는 DB와 무관하게 API 프로세스를 검사합니다. 추후 Readiness는 선택된 Provider 연결·스키마 준비 상태를 별도 진단할 수 있지만 DB 단절을 API 생존 실패와 혼동하지 않습니다.
- 상태 응답은 연결 문자열, 로컬 절대 경로, SQL 오류 전문, 스키마 내부 정보를 외부에 노출하지 않습니다.

## 9. Provider별 차이와 이식 규칙

자료형, 정렬/대소문자 규칙, JSON, 트랜잭션·잠금, Migration, SQL 함수, 인덱스 및 암호화 차이는 추상화로 숨기지 않고 [15. 데이터 모델](./15_DATA_MODEL.md) 및 [18. 데이터베이스 성능](./18_DATABASE_PERFORMANCE.md)에 기록합니다.

이식 가능한 EF 표현과 공통 저장 형식을 우선합니다. 특정 DB 함수나 SQL이 필요한 기능은 Infrastructure Provider별 구현과 동일 계약 테스트를 제공하며, 실제 SQLite와 SQL Server 둘 다에서 실행해 검증합니다. 연결 문자열만 바꾸면 Provider 이동이 끝났다고 보지 않습니다.
