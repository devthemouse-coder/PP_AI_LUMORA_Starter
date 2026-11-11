# 15. 데이터 모델

## 1. 모델링 원칙과 상태

다음 표는 SQLite 및 Microsoft SQL Server를 함께 고려한 논리 스키마와 SQLite 구현의 기준입니다. EF Core 엔터티, SQLite 테이블, PK/FK 및 인덱스는 구현되어 있으며 SQL Server Provider는 아직 없습니다. 실제 구현된 테이블/컬럼 매핑과 Provider 한계는 [19. SQLite 구현](./19_SQLITE_IMPLEMENTATION.md)에서 설명합니다.

- 모든 사용자 데이터는 `TenantId`, `UserId` 소유 범위를 포함합니다. 개인 설치도 한 Tenant와 그 안의 한 사용자로 시작할 수 있습니다.
- 애플리케이션 권한 판단은 인증된 서버 문맥에서 결정합니다. 요청 본문이 보낸 `TenantId`를 권한 증명으로 사용하지 않습니다.
- 고유 ID는 GUID를 사용합니다. 관계는 복합 FK/Unique constraint로 Tenant·User 경계를 DB에서도 확인할 수 있도록 합니다.
- enum은 이름 문자열 대신 버전 관리되는 정수 코드로 저장합니다. 미지 코드에는 안전한 호환 처리와 마이그레이션 정책이 필요합니다.
- 민감 본문·이름은 암호화 Payload에 두고, 인덱스에 필요한 검색 정보만 별도 토큰으로 둡니다.
- 시간은 UTC로 정규화합니다. 범위 값은 명확히 반개구간 `[ValidFrom, ValidTo)`로 해석합니다.

## 2. 논리 자료형 매핑 후보

| 논리 자료형 | SQLite | SQL Server | 규칙 |
|---|---|---|---|
| UUID/GUID | TEXT(표준 하이픈 형식) | `uniqueidentifier` | 대소문자·공백이 없는 정규 표현 |
| 작은 열거/상태 코드 | `INTEGER` | `smallint` | 코드 표는 애플리케이션에서 관리 |
| 정수/순번/버전 | `INTEGER` (64-bit) | `bigint` | 범위와 overflow 검사 |
| 불리언 | `INTEGER` (0/1) | `bit` | 양 Provider 매핑 확인 |
| UTC 시각 | 정규화 UTC TEXT(고정 정밀도) | `datetime2(7)` | `DateTime`을 저장 전 UTC로 정규화하고 읽은 값도 UTC `Kind`로 취급; offset은 별도 보관이 필요한 사용자 설정에만 둠 |
| 고정 길이 암호 키 식별자 | `TEXT` | `nvarchar(64)` | 비밀 키 자체가 아닌 KeyId |
| 암호문·Nonce·Tag·HMAC | `BLOB` | `varbinary(n)` | 문자열 변환/로그 금지 |
| 신뢰 점수 | `INTEGER` | `smallint` | 0~10,000 정수 스케일 |
| 보통 식별용 문자열 | `TEXT` | `nvarchar(n)` | 길이 제한·정규화·collation 차이 테스트 |

SQLite의 동적 타입과 대소문자 비교, SQL Server의 collation은 동일하지 않습니다. EF Core SQLite의 `DateTimeOffset` 정렬/비교 제한을 피하기 위해 업무 UTC timestamp는 UTC 정규화 `DateTime`으로 사용하고, SQL Server에서는 `datetime2(7)`로 저장하는 방안을 권고합니다. Provider converter/materialization에서 UTC `Kind`를 명시하고 양 Provider의 경계값·정렬 테스트를 추가합니다. SQL Server `rowversion`, SQLite 전용 JSON/날짜 함수, Provider 전용 SQL을 공통 모델의 필수 의미로 사용하지 않습니다.

## 3. 엔터티 및 테이블 후보

열거형 이름·상태 값과 인덱스의 순서는 논리 설계이며, 실제 명명 규칙은 기존 코딩 규칙의 영문 식별자와 snake_case DB 명명 기준을 일관되게 적용합니다.

### 3.1 `Tenant`

| 필드 | 논리 타입 | 제약/설명 |
|---|---|---|
| `TenantId` | GUID | PK |
| `Name` | 문자열 | 필수; 조직 명칭으로 간주해 로그 노출 주의 |
| `Status` | 상태 코드 | 필수; 사용/비활성 |
| `CreatedAtUtc` | UTC 시각 | 필수 |
| `UpdatedAtUtc` | UTC 시각 | 필수 |

관계: Tenant 1:N User, Conversation, MemoryItem. 후보 인덱스: `(Status, CreatedAtUtc)`는 운영 목록 요구가 확인될 때만 추가. 개인 설치의 단일 Tenant는 별도 DB 초기화 명령으로 프로비저닝합니다.

### 3.2 `User`

| 필드 | 논리 타입 | 제약/설명 |
|---|---|---|
| `UserId` | GUID | PK |
| `TenantId` | GUID | Tenant FK; 필수 |
| `DisplayName` | 암호화 Payload | 민감 프로필 값. 필요할 때 복호화 |
| `Status` | 상태 코드 | 필수 |
| `CreatedAtUtc`, `UpdatedAtUtc` | UTC 시각 | 필수 |

후보 고유 제약: `(TenantId, UserId)` (하위 복합 FK 지원). 이름 검색용 일반 인덱스는 만들지 않습니다. 인증 주체/이메일은 요구·인증 설계가 끝나기 전 모델에 고정하지 않습니다.

### 3.3 `Conversation`

| 필드 | 논리 타입 | 제약/설명 |
|---|---|---|
| `ConversationId` | GUID | PK |
| `TenantId`, `UserId` | GUID | 소유 범위 및 복합 FK |
| `Summary` | 암호화 Payload | 선택. 대화 전문과 별도 보관하며 필요한 문맥만 제공 |
| `SummaryUpdatedAtUtc` | UTC 시각 | 선택 |
| `CreatedAtUtc`, `UpdatedAtUtc`, `LastMessageAtUtc` | UTC 시각 | 메시지 정렬·보존용 |
| `Status` | 상태 코드 | 활성/종료 등; 실제 정책 확정 필요 |

고유 후보 `(TenantId, UserId, ConversationId)`; 하위 복합 FK 대상이 되도록 합니다. 목록 인덱스 후보 `(TenantId, UserId, Status, UpdatedAtUtc, ConversationId)`.

### 3.4 `ConversationMessage`

| 필드 | 논리 타입 | 제약/설명 |
|---|---|---|
| `MessageId` | GUID | PK |
| `TenantId`, `UserId`, `ConversationId` | GUID | 소유 범위 및 Conversation 복합 FK |
| `SequenceNumber` | 64-bit 정수 | 필수; Conversation별 증가 |
| `Role` | 상태 코드 | system/user/assistant/tool 등 허용값 검증 |
| `BodyFormatVersion`, `BodyKeyId`, `BodyNonce`, `BodyCiphertext`, `BodyAuthenticationTag` | 암호화 Payload 구성 | 본문 전체 AEAD; 검색 및 로그에 평문 사용 금지 |
| `CreatedAtUtc` | UTC 시각 | 정렬 및 보존 정책 |

고유 제약 `(TenantId, UserId, ConversationId, SequenceNumber)`와 `(TenantId, UserId, ConversationId, MessageId)` (메시지 복합 FK 대상). 시간순 조회는 순번 제약으로 페이징합니다. 본문에 일반 Full Text 인덱스를 만들지 않습니다. 대화 요약도 본문과 같은 AEAD envelope 규칙을 적용합니다.

### 3.5 `MemoryItem`

| 필드 | 논리 타입 | 제약/설명 |
|---|---|---|
| `MemoryId` | GUID | PK |
| `TenantId`, `UserId` | GUID | 소유 범위; User 복합 FK |
| `MemoryType` | 상태 코드 | Profile, Preference, Fact, Work, Procedure, Decision, ConversationExtract 등 |
| `ConfidenceScore` | 0..10,000 정수 | 의미·근거를 문서화; 검증 상태와 별도 |
| `VerificationStatus` | 상태 코드 | 제안/확인/반려 등 |
| `Sensitivity` | 상태 코드 | 공개/개인/민감 등 정책 분류 |
| `CreatedAtUtc`, `UpdatedAtUtc` | UTC 시각 | 필수 |
| `ValidFromUtc`, `ValidToUtc` | UTC 시각 | 선택; 업무상 유효 기간 |
| `IsActive` | 불리언 | 비활성은 이용 중단이며 개인정보 완전 삭제와 다름 |
| `DeletedAtUtc` | Nullable UTC 시각 | 제한 보존 중 논리 삭제 표식; null이면 삭제 요청 전 |
| `Version` | 64-bit 정수 | 애플리케이션 관리 동시성 토큰 |

고유 후보 `(TenantId, UserId, MemoryId)`. 조회 인덱스 후보 `(TenantId, UserId, IsActive, VerificationStatus, MemoryType, UpdatedAtUtc, MemoryId)`; 실제 컬럼 순서는 쿼리 계획 측정 후 확정.

### 3.6 `MemoryPayload`

| 필드 | 논리 타입 | 제약/설명 |
|---|---|---|
| `TenantId`, `UserId`, `MemoryId` | GUID | MemoryItem 복합 PK/FK; 1:1 |
| `FormatVersion` | 작은 정수 | 암호문 포맷/직렬화 버전 |
| `KeyId` | 문자열 | 암호화 키 버전 식별자 |
| `Nonce` | 고정 Binary(12) | AES-GCM 권장 길이 |
| `Ciphertext` | Binary | 자유 기억 또는 정규화된 구조화 값 |
| `AuthenticationTag` | 고정 Binary(16) | AES-GCM tag |

`Ciphertext`는 평문 문자열이 아닌 UTF-8 인코딩된 버전 있는 Payload를 암호화한 바이트입니다. AAD에 TenantId, UserId, MemoryId, 필드 용도, FormatVersion을 포함합니다. 암호화 세부사항은 [17. 데이터 보안 및 암호화](./17_DATA_SECURITY_AND_ENCRYPTION.md)를 참고하십시오.

### 3.7 `MemoryStructuredFact`

| 필드 | 논리 타입 | 제약/설명 |
|---|---|---|
| `TenantId`, `UserId`, `MemoryId` | GUID | PK/FK; Memory와 1:1 |
| `FactType` | 제한 문자열/코드 | 예: `Person.Birthday`; 버전 관리 |
| `ValueSchemaVersion` | 작은 정수 | 복호화 값의 스키마 버전 |

대상 이름과 값(예: 생일의 월·일)은 `MemoryPayload`의 암호화된 타입별 구조에 저장합니다. JSON은 Provider가 검색할 DB JSON 열이 아니라 암호화 전 앱 내부 직렬화 표현입니다. 제한된 질의에 필요한 키는 HMAC 토큰으로 검색합니다.

### 3.8 `MemorySource`

| 필드 | 논리 타입 | 제약/설명 |
|---|---|---|
| `SourceId` | GUID | PK |
| `TenantId`, `UserId`, `MemoryId` | GUID | MemoryItem 복합 FK |
| `SourceType` | 상태 코드 | 직접 입력, 대화 메시지 추출, 가져오기 등 |
| `SourceConversationId`, `SourceMessageId` | Nullable GUID | 원본 대화 연결; 같은 Tenant/User 범위의 복합 FK |
| `CapturedAtUtc` | UTC 시각 | 출처를 기록한 시점 |
| `ConfidenceScore` | 0..10,000 정수 | 해당 출처 근거 점수; 기억의 현재 신뢰도와 구분 가능 |

하나의 기억에는 여러 근거가 연결될 수 있습니다. 고유 후보 `(TenantId, UserId, MemoryId, SourceId)`를 두어 개정에서 범위가 맞는 출처만 참조하게 합니다. `SourceConversationId`는 Conversation 소유 범위에, `(SourceConversationId, SourceMessageId)`는 같은 Tenant/User의 메시지 고유 키에 복합 FK로 연결합니다. 원문 프롬프트/메시지를 복제해 저장하지 않습니다. 원본 메시지가 보존 정책상 삭제되면 연결을 null 처리하되, 해당 Memory를 제거하는 사용자 삭제 요청은 연결된 출처·토큰·개정까지 함께 삭제합니다.

### 3.9 `MemoryRevision`

| 필드 | 논리 타입 | 제약/설명 |
|---|---|---|
| `RevisionId` | GUID | PK |
| `TenantId`, `UserId`, `MemoryId` | GUID | MemoryItem 복합 FK |
| `RevisionNumber` | 64-bit 정수 | Memory별 고유 순번 |
| `ChangeType` | 상태 코드 | 수정, 비활성화, 논리 삭제, 재활성화 등 |
| `PreviousFormatVersion`, `PreviousKeyId`, `PreviousNonce`, `PreviousPayloadCiphertext`, `PreviousAuthenticationTag` | 암호화 Payload 구성 | 변경 전 값 |
| `ReasonFormatVersion`, `ReasonKeyId`, `ReasonNonce`, `ReasonCiphertext`, `ReasonAuthenticationTag` | Nullable 암호화 Payload | 선택; 이유에 개인정보가 포함될 수 있음 |
| `ChangedAtUtc` | UTC 시각 | 필수 |
| `ChangedByUserId` | Nullable GUID | 소유 범위 내 행위자 |
| `SourceId` | Nullable GUID | 변경을 유발한 근거 |

고유 `(TenantId, UserId, MemoryId, RevisionNumber)`. `ChangedByUserId`는 `(TenantId, ChangedByUserId)`에서 User의 `(TenantId, UserId)`로 이어지는 복합 FK, `SourceId`는 `(TenantId, UserId, MemoryId, SourceId)`에서 동일 Memory의 출처로 이어지는 복합 FK 후보입니다. 변경 전 스냅샷도 암호화하며 일반 감사 로그에 복사하지 않습니다. 저장·이력은 동일 트랜잭션이어야 합니다. 논리 삭제 Revision은 승인된 보존 기간 안에서만 유지할 수 있고 완전 삭제 시 본문·Revision을 함께 제거합니다.

### 3.10 `MemoryLookupToken`

| 필드 | 논리 타입 | 제약/설명 |
|---|---|---|
| `TokenId` | GUID | PK |
| `TenantId`, `UserId`, `MemoryId` | GUID | MemoryItem 복합 FK |
| `TokenType` | 상태 코드 | 예: FactType, BirthdayMonth, SubjectExact |
| `KeyId` | 문자열 | HMAC 키 버전 |
| `Token` | Binary(32) | HMAC-SHA-256 출력; 평문 원문 아님 |

후보 인덱스 `(TenantId, UserId, TokenType, KeyId, Token, MemoryId)`. 고유 제약은 동일 토큰 다중 기억 허용 여부를 확정한 뒤 선택합니다. 토큰은 반복·동등성 정보를 노출할 수 있는 민감 메타데이터입니다.

## 4. 관계 및 삭제 경계

```text
Tenant 1 ── * User
Tenant/User 1 ── * Conversation 1 ── * ConversationMessage
Tenant/User 1 ── * MemoryItem
MemoryItem 1 ── 1 MemoryPayload
MemoryItem 1 ── 0..1 MemoryStructuredFact
MemoryItem 1 ── * MemorySource
MemoryItem 1 ── * MemoryRevision
MemoryItem 1 ── * MemoryLookupToken
MemorySource 0..* ── 0..1 ConversationMessage (근거 링크)
```

모든 하위 참조에 Tenant/User 범위의 복합 FK를 적용할 수 있도록 부모에 복합 Unique 키를 둡니다. 개인 데이터 전체 삭제는 Tenant/User 범위에서 DB 행뿐 아니라 암호화 키, 백업 보존 정책, 로그 및 복제 데이터를 함께 처리해야 합니다. 대규모 기업 테넌트 삭제는 대규모 단일 cascade 대신 검증 가능한 단계적 purge로 설계합니다.

## 5. 생일 사실 예시

`MemoryItem.MemoryType=Fact`, 확인/신뢰 상태, 소유 범위, 유효 기간, 버전을 일반 메타데이터로 관리합니다. `MemoryStructuredFact.FactType=Person.Birthday`로 구조를 표시합니다. 대상 이름과 `{ month: 7, day: 15 }`는 암호화 Payload 안에 typed schema로 보관합니다.

검색 저장에는 다음 두 대안을 구분합니다.

1. **직접 typed projection(대안):** 검색 전용 `BirthdayMonth`/`BirthdayDay`를 SQL `tinyint`(SQLite `INTEGER`)로 저장해 월·일 인덱스를 사용할 수 있지만, 생일 정보를 평문 메타데이터로 노출합니다.
2. **권고 후보:** 타입별 값은 암호화 Payload에만 저장하고 월별 질의용 `BirthdayMonth:07`, 대상 이름 일치용 `SubjectExact:<정규화된 이름>`을 HMAC token으로 저장합니다. 토큰은 복원 가능한 평문 생일을 담지 않지만 반복·동등성·질의 결과 노출은 남습니다.

검색 성능과 위협 모델 간 최종 결정은 미결정입니다. 구현 전 성능 요구와 허용 가능한 메타데이터 노출을 검토해 ADR로 승인합니다. 토큰 생성·키 회전·허용 질의는 [16. Memory 설계](./16_MEMORY_DESIGN.md)에서 정의합니다.

Application은 `GetBirthdaysForMonth(tenantId, userId, month, page)` 같은 명시적 UseCase/Repository 계약을 호출합니다. 이 계약은 임의 SQL이나 LLM 생성 식을 받지 않습니다. 서버가 날짜·사용자 범위·권한을 결정하고, 결과를 페이지 단위로 복호화한 후에만 답변 작성에 전달합니다.

## 6. DB 차이와 미결정 사항

- SQL Server `datetime2`와 SQLite UTC TEXT 간 UTC 정렬·범위 질의를 Provider 계약 테스트로 검증합니다.
- 긴 암호문 Binary 열의 최대 크기, SQLite 연결 제한, SQL Server 인덱스 제한은 payload 제한을 정하며 확정합니다.
- Tenant/User와 메모리 주체의 인증·가시성 정책, 기업 내 공유 기억의 권한 모델은 인증 설계에서 결정해야 합니다.
- 대화 요약의 길이·갱신 시점과 대화 보존 기간은 프런트/API 대화 설계에서 결정합니다.
- Structured JSON의 스키마 registry, 허용 predicate 목록, 사실 유형 확장은 Application 유스케이스로 검증하며 DB JSON 기능에 종속시키지 않습니다.
- 실제 인덱스, 컬럼 길이, cascade/restrict 세부 규칙, enum 숫자 값은 샘플 데이터가 아닌 보존·조회 요구사항 및 두 Provider Migration 테스트를 근거로 확정합니다.
