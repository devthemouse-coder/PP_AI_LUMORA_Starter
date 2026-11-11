# 19. SQLite 저장소 구현

## 1. 범위와 구현 상태

현재는 개인용 SQLite만 구현했습니다. EF Core 10.0.14를 사용하며 SQL Server Provider, SQL Server Migration, 인증/인가, Ollama 연동은 구현하지 않았습니다. Memory 및 StructuredFact 업무 API는 구현되어 있지만 인증이 없고 개발 환경의 명시적 고정 scope에서만 사용할 수 있습니다. API는 DB 시작 Migration을 자동 실행하지 않습니다.

## 2. 구현 위치

```text
src/
  Lumora.Domain/Entities/           # Provider 독립 엔터티와 암호문 값
  Lumora.Application/Abstractions/   # scope가 있는 Repository, Unit of Work, 암호화 계약
  Lumora.Infrastructure/
    Persistence/
      Configurations/                # Fluent API 매핑
      Context/LumoraDbContext.cs
      Repositories/
      Sqlite/                         # Provider 등록, Unit of Work, design-time factory
      Migrations/Sqlite/              # SQLite 전용 초기 Migration
      Backup/                         # Online Backup, 검증·복원, 보존 정책
    Security/
      AesGcmEncryptionService.cs
      HmacSearchTokenService.cs
      KeyManagement/DpapiKeyMaterialStore.cs
```

## 3. 데이터 경로 및 구성

Provider는 `Database:Provider=Sqlite`로 명시합니다. SQLite 파일 경로 선택 우선순위는 다음과 같습니다.

1. `ConnectionStrings:Sqlite` — 설정된 파일 경로
2. `Database:Sqlite:DataDirectory`
3. `LUMORA_DATA_DIRECTORY`
4. `%LOCALAPPDATA%\P.P. AI - LUMORA\data`

디렉터리에는 `lumora.db`가 생성됩니다. 연결 문자열은 비밀이 아니지만 사용자 경로를 로그에 출력하지 않습니다. 연결별 `Foreign Keys=True`가 강제됩니다. SQLite DB와 WAL/SHM/임시 파일 및 `data/`는 Git에서 제외됩니다.

백업 경로는 `Backup:Directory`, `LUMORA_BACKUP_DIRECTORY`, 기본 `%LOCALAPPDATA%\P.P. AI - LUMORA\backups` 순서입니다. `--backup`은 SQLite Online Backup API를 사용해 `.lumora-backup`을 생성하고 무결성과 키 샘플을 검증합니다. 키 복구 파일은 DB/백업과 별도 경로에 저장하며 상세 절차는 [24–27 백업·복구 문서](24_BACKUP_STRATEGY.md)를 따릅니다.

## 4. 구현된 테이블

| 테이블 | 용도 | PK 및 주요 FK | 주요 인덱스/제약 |
|---|---|---|---|
| `Tenants` | 조직/개인 Tenant | `Id` | `Version` 동시성 토큰 |
| `Users` | Tenant의 사용자 | `Id`; `TenantId → Tenants.Id` | `(TenantId, Id)` 고유키, `Version` |
| `Conversations` | 사용자 대화 세션과 암호화 요약 | `Id`; `(TenantId, UserId) → Users` | `(TenantId, UserId, Id)` 고유키, 최근 대화 목록 인덱스 |
| `Messages` | 암호화 대화 메시지 | `Id`; 소유 User와 Conversation 복합 FK | Conversation 순번 복합 고유 인덱스, 출처 참조 고유키 |
| `Memories` | 장기 기억 메타데이터 | `Id`; `(TenantId, UserId) → Users` | 사용자·활성·검증·유형·수정시각 목록 인덱스, `Version` |
| `MemoryPayloads` | 기억 본문 AEAD Envelope | `(TenantId, UserId, MemoryId)` 복합 PK/FK | Memory와 1:1 제약 |
| `StructuredFacts` | 유형이 지정된 암호화 사실 | `(TenantId, UserId, MemoryId)` 복합 PK/FK | 소유 범위 및 `FactType` 인덱스 |
| `MemorySources` | 직접 입력/대화 메시지 출처 | `Id`; Memory 및 선택적 Message 복합 FK | 출처 삭제 시 Message 링크를 먼저 해제 |
| `MemoryRevisions` | 이전 암호화 Payload, 변경 유형/시각 | `Id`; Memory, 선택적 행위자·Source FK | `(TenantId, UserId, MemoryId, RevisionNumber)` 고유 |
| `MemoryLookupTokens` | HMAC 검색 토큰 | `Id`; Memory 복합 FK | `(TenantId, UserId, TokenType, KeyId, Token, MemoryId)` |
| `__EFMigrationsHistory` | EF 스키마 버전 | EF 관리 | SQLite Migration 적용 이력 |

일반 메모리/대화 본문과 프로필 표시명은 SQLite에 평문 문자열로 저장하지 않습니다. 구조화 사실의 `FactType`은 색인/분류 메타데이터이며 사실 값 자체는 AEAD Payload입니다. 생일 월·일은 SQL 정수 열로 저장하지 않고 HMAC 토큰으로 조회를 지원합니다.

## 5. 소유권 및 삭제

- 모든 사용자 소유 Repository 호출은 `TenantUserScope` 또는 tenant/user ID를 인자로 요구합니다. Query Filter가 아니라 LINQ 조건 및 복합 FK가 범위를 강제합니다.
- Conversation, Message, Memory, StructuredFact, MemoryLookupToken은 범위 필터를 항상 포함하며 목록은 페이지 크기 최대 200으로 제한합니다.
- Tenant/User 삭제 및 Message 참조 해제는 원자적 Unit of Work 안에서 실행해야 합니다. 삭제는 암호화 본문·revision·source·검색 토큰을 DB cascade 또는 사전 참조 해제로 제거합니다.
- 외부 사용자가 보낸 TenantId/UserId를 인증 문맥으로 신뢰해서는 안 됩니다. 인증 기능이 없으므로 업무 API는 Development 환경, `MemoryApi:EnableDevelopmentScope=true`, 명시적 TenantId/UserId가 모두 유효한 경우에만 고정 범위를 사용합니다. 이 설정은 운영 인증을 대신하지 않으며, 설정이 없으면 503으로 거부합니다.

Memory/StructuredFact UseCase와 API 범위는 [28. Memory UseCase](28_MEMORY_USECASE_DESIGN.md), [29. StructuredFact](29_STRUCTURED_FACT_DESIGN.md), [30. API 계약](30_API_CONTRACT.md), [31. 검색 설계](31_SEARCH_DESIGN.md)에 기록했습니다. 기능 구현으로 SQLite 스키마 Migration은 변경되지 않았습니다.

## 6. API 상태 검사

- `GET /api/health`: DB와 독립적인 API 생존 검사입니다.
- `GET /api/ready`: SQLite 접속 및 미적용 Migration 여부를 검사합니다. 연결 실패 또는 미적용 Migration이면 503입니다.
- DB가 없거나 Migration이 적용되지 않았다는 이유로 Liveness 결과를 실패시키지 않습니다.

## 7. 아직 없는 항목

SQL Server, 사용자 인증/인가, 일반 사용자 프로비저닝 명령, 키 회전/복수 키 복호화, 대규모 tenant purge 작업은 구현하지 않았습니다. 개발용 고정 scope 외에 운영 API 호출을 위한 신뢰할 수 있는 사용자 컨텍스트도 없습니다. SQLite 온라인 백업/복원과 별도 키 복구 도구는 구현되었으나, 운영 데이터 생성 전 실제 별도 PC 복구와 보안 검토를 완료해야 합니다.
