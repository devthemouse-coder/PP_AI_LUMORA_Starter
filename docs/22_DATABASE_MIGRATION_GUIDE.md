# 22. 데이터베이스 Migration 가이드

## 1. Provider 및 이력

현재 Migration 집합은 `Lumora.Infrastructure` 어셈블리의 `Persistence/Migrations/Sqlite`에 있으며 SQLite 전용입니다. EF의 `__EFMigrationsHistory`가 적용 버전을 기록합니다. SQL Server Migration은 아직 없습니다. SQLite Migration을 SQL Server에 적용하지 않습니다.

## 2. 최초 DB 생성 및 업데이트

저장소 루트 PowerShell에서 실행합니다.

```powershell
dotnet tool install --global dotnet-ef --version 10.0.14
$env:LUMORA_DATA_DIRECTORY = Join-Path $env:LOCALAPPDATA 'P.P. AI - LUMORA\data'
dotnet ef migrations list --project src\Lumora.Infrastructure\Lumora.Infrastructure.csproj --startup-project src\Lumora.Api\Lumora.Api.csproj --context Lumora.Infrastructure.Persistence.Context.LumoraDbContext
dotnet ef database update --project src\Lumora.Infrastructure\Lumora.Infrastructure.csproj --startup-project src\Lumora.Api\Lumora.Api.csproj --context Lumora.Infrastructure.Persistence.Context.LumoraDbContext
```

현재 초기 Migration은 테이블/키/인덱스를 생성합니다. `Program.cs`는 시작 시 DB를 생성하거나 Migration을 적용하지 않습니다. `/api/ready`가 503이면 우선 DB 파일 경로와 Migration 적용 상태를 확인합니다.

## 3. 스키마 변경

1. Domain/Fluent configuration을 검토하고 Migration 변경 범위를 확인합니다.
2. `dotnet ef migrations add <설명>`으로 SQLite Migration을 만듭니다.
3. 생성된 Up/Down 및 snapshot을 검토하고 임시 SQLite 파일에서 신규 생성/기존 Migration 후 적용/외래 키 검사를 실행합니다.
4. 실제 운영 데이터가 있는 경우 일관된 백업과 복원 절차를 먼저 리허설합니다.
5. 개발 DB에 `database update`를 실행하고 `dotnet ef migrations list`, API readiness 및 통합 테스트를 확인합니다.

Migration 파일을 수동으로 편집하거나 이미 공유/배포한 Migration을 다시 쓰지 않습니다. 기존 데이터의 변환은 재실행·중단 복구·행 수 검증이 가능한 단계적 절차로 설계해야 합니다.

## 4. 백업·복원 및 실패 복구

- SQLite 파일이 사용 중이거나 WAL 모드인 동안 `.db`만 임의 복사하지 않습니다. `--backup`은 SQLite Online Backup API를 사용하며 생성 후 `integrity_check`, `foreign_key_check`, 적용 Migration 및 복호화 샘플을 검증합니다.
- `--restore-backup`은 새로운 대상 경로만 허용합니다. `--replace-backup <backup> --approve-replace`는 명시적으로 승인된 경우에만 사전 암호화 백업, WAL Checkpoint, 별도 복원 검증 후 원자 교체를 시도합니다.
- DPAPI 키는 별도 `.lumora-keys` 복구 파일로 사용자 비밀번호 기반 보호를 받을 수 있습니다. DB 백업과 키 복구 파일을 서로 다른 경로/오프라인 매체에 보관합니다.
- Migration 실패 시 DB와 Migration 이력을 조사하고 원본 백업을 보존합니다. 실패한 Migration을 무조건 자동 downgrade하거나 DB 파일을 삭제하지 않습니다. 안전한 복구 Migration 또는 승인된 백업 복원 후 다시 검증합니다.
- 운영 시작 시 무조건 Migration하지 않습니다. SQL Server를 구현할 때 DBA 승인, 별도 Migration assembly 또는 provider별 migration set, SQL 스크립트 점검, 복구 테스트를 별도 설계합니다.
- 실행 가능한 백업/복원/키 이동 명령은 [25. 백업·복원 안내](25_BACKUP_RESTORE_GUIDE.md), 보존 정책은 [27. 데이터 보존 정책](27_DATA_RETENTION_POLICY.md)를 참고합니다. 다른 물리 PC에서의 이전은 테스트 환경에서 리허설해야 합니다.
