# 20. EF Core 개발 가이드

## 1. 패키지와 계층

`Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`, `System.Security.Cryptography.ProtectedData`는 `Directory.Packages.props`에서 10.0.14로 관리합니다. API와 Infrastructure의 Design 패키지는 개발 도구 전용이며 PrivateAssets로 전파되지 않습니다. 테스트는 EF Core InMemory Provider가 아닌 SQLite Provider를 사용합니다.

- Domain: 순수 C# 엔터티와 불변 조건. EF 특성 및 DB 패키지 없음.
- Application: 저장소/Unit of Work 계약. `DbContext`, `IQueryable`, EF 예외/자료형 미노출.
- Infrastructure: `LumoraDbContext`, Fluent API, SQLite Repository, Migration.
- API: Provider 등록 Composition Root, HTTP 생존/준비 검사.

## 2. DbContext 및 Mapping

`LumoraDbContext`는 요청 범위로 등록합니다. 매핑은 `Persistence/Configurations`에 두고 도메인 엔터티에 EF 특성을 추가하지 않습니다. 시간은 `DateTime` UTC만 저장하며 SaveChanges 전 UTC 여부를 검사하고 SQLite에서 다시 읽을 때 `DateTimeKind.Utc`를 복원합니다.

SQLite Provider의 `Foreign Keys=True`는 연결별로 적용합니다. 복합 FK/고유키는 `TenantId`, `UserId`, 부모 ID를 묶어 다른 사용자의 부모 행을 참조하는 데이터를 DB에서 거부합니다. 전역 Query Filter는 보안 경계로 사용하지 않습니다.

## 3. 저장소 사용

`TenantUserScope`는 TenantId/UserId 모두 비어 있지 않은 값이어야 합니다. 사용자 소유 저장소는 범위 없는 전체 목록 또는 ID 단독 조회를 제공하지 않습니다. 페이지 번호는 1부터 시작하며 PageSize는 1–200입니다.

```csharp
var scope = new TenantUserScope(tenantId, userId);
var result = await memoryRepository.ListAsync(
    scope,
    new PageRequest(pageNumber: 1, pageSize: 50),
    cancellationToken);
```

현재 API는 인증되지 않았으므로 이 계약을 임의의 HTTP 입력에 직접 연결하지 마십시오. 이후 인증 구현에서 서버가 결정한 Tenant/User 범위만 전달해야 합니다.

조회는 목록/토큰 검색에서 `AsNoTracking()`을 사용하고 안정된 정렬 및 페이지 제한을 적용합니다. 단건 변경 대상은 범위 조건을 포함해 조회한 뒤 도메인 메서드로 수정하고 `IUnitOfWork.SaveChangesAsync`를 호출합니다. `UpdateAsync` 또한 전달된 Entity의 소유 범위를 검증합니다.

## 4. 트랜잭션 및 동시성

- 같은 요청의 여러 Repository 변경은 `IUnitOfWork.ExecuteInTransactionAsync`로 묶습니다. 이 메서드는 작업 후 SaveChanges와 commit을 수행하고 실패 시 rollback 후 예외를 다시 던집니다.
- `Tenant`, `User`, `Conversation`, `MemoryItem`은 정수 `Version`을 낙관적 동시성 토큰으로 사용합니다. Infrastructure가 변경 Entity의 Version을 증가시킵니다. 충돌은 `DbUpdateConcurrencyException`으로 나타나며 최신 값을 읽고 사용자 의도를 재검토해야 합니다.
- `DbContext`는 thread-safe하지 않습니다. 하나의 인스턴스로 병렬 DB 작업을 실행하지 말고 LLM 호출 동안 DB 트랜잭션을 유지하지 않습니다.
- Repository 삭제의 `ExecuteDeleteAsync`/`ExecuteUpdateAsync`는 DB cascade 및 추적기를 우회합니다. 삭제 전후의 관계 변경이 여러 SQL 구문이면 반드시 Unit of Work 트랜잭션 안에서 실행합니다.

## 5. Migration 명령

```powershell
dotnet tool install --global dotnet-ef --version 10.0.14
$env:LUMORA_DATA_DIRECTORY = Join-Path $env:LOCALAPPDATA 'P.P. AI - LUMORA\data'
dotnet ef migrations list --project src\Lumora.Infrastructure\Lumora.Infrastructure.csproj --startup-project src\Lumora.Api\Lumora.Api.csproj --context Lumora.Infrastructure.Persistence.Context.LumoraDbContext
dotnet ef migrations add DescriptiveChange --project src\Lumora.Infrastructure\Lumora.Infrastructure.csproj --startup-project src\Lumora.Api\Lumora.Api.csproj --context Lumora.Infrastructure.Persistence.Context.LumoraDbContext --output-dir Persistence\Migrations\Sqlite
dotnet ef database update --project src\Lumora.Infrastructure\Lumora.Infrastructure.csproj --startup-project src\Lumora.Api\Lumora.Api.csproj --context Lumora.Infrastructure.Persistence.Context.LumoraDbContext
```

Migration은 SQLite 전용이며 운영 서버 시작 시 자동 적용하지 않습니다. 기존 Migration을 편집하지 말고 새 Migration으로 변경합니다. SQL Server를 도입할 때는 별도 Provider/Migration/통합 테스트가 필요합니다.
