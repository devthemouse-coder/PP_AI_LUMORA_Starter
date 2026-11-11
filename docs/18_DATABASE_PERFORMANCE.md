# 18. 데이터베이스 성능 및 운영

## 1. 원칙

성능 목표를 데이터·실행 계획 없이 추정하지 않습니다. 정확한 구조화 질의는 관계형 SQL 인덱스를 우선하고, 이 단계에서는 Vector DB, cache, Dapper, 무조건적인 DbContext pooling을 도입하지 않습니다. 사용자가 지정한 관계형 DB 두 Provider의 실제 성능을 따로 측정합니다.

## 2. 인덱스 후보

인덱스는 [15. 데이터 모델](./15_DATA_MODEL.md)에 정의된 Tenant/User 범위를 선두로 설계하고, 실제 Query UseCase와 tenant cardinality로 검증합니다.

| 조회 | 복합 인덱스 후보 | 확인 사항 |
|---|---|---|
| 최근 대화 페이지 | `(TenantId, UserId, Status, UpdatedAtUtc, ConversationId)` | 상태별 분포, 커서 정렬 |
| 대화 메시지 순서 | `(TenantId, UserId, ConversationId, SequenceNumber)` UNIQUE | Conversation 당 중복 순번 방지 |
| 활성 기억 목록 | `(TenantId, UserId, IsActive, VerificationStatus, MemoryType, UpdatedAtUtc, MemoryId)` | active/status 필터 선택성, 쿼리 계획 |
| Memory source 조회 | `(TenantId, UserId, MemoryId, CapturedAtUtc)` | source가 실제 목록으로 조회될 때만 |
| 기억 revision 조회 | `(TenantId, UserId, MemoryId, RevisionNumber)` UNIQUE | 최신/선택 버전 조회 |
| 검색 토큰 lookup | `(TenantId, UserId, TokenType, KeyId, Token, MemoryId)` | binary index 크기, 회전 중 key versions |

모든 후보를 그대로 생성하지 않습니다. 고유·FK 제약이 자동 제공하는 인덱스와 중복되지 않는지 확인하고 쓰기 비용·파일 크기·실제 query plans를 비교합니다. 암호문, 메시지 본문, 사용자 표시 이름은 일반 인덱스 대상이 아닙니다.

## 3. 정확한 사실 조회 및 페이지네이션

- 생일 월별 조회는 HMAC 월 Token으로 대상 기억을 제한하고 사용자의 현재 권한·활성/검증 상태를 적용합니다. Query가 찾은 후보만 페이지 단위로 복호화합니다.
- 지정 대상 생일 조회는 별도 subject exact-match token을 적용하고, 토큰 일치 뒤 decrypted payload와 권한을 검증합니다.
- 대량 목록은 안정된 복합 정렬 키와 seek/keyset pagination을 우선합니다. offset이 필요한 관리자 화면은 최대 offset/페이지 크기를 제한하고 측정합니다.
- 사용자별 필터·`IsActive`·상태 조건·정렬 키를 쿼리마다 유지하며 무제한 `.ToListAsync()`로 전 테이블을 가져오지 않습니다.
- 각 DTO에 필요한 열만 투영합니다. 화면/LLM에 대화 전체나 모든 기억을 보내지 않습니다. LLM 문맥 제한은 DB 페이징을 대체하지 않습니다.

## 4. EF Core 쿼리 및 컨텍스트

- 읽기 전용 경로는 `AsNoTracking()`과 projection을 사용합니다. 페이지 크기와 입력 범위를 검증합니다.
- Application은 Provider queryable/SQL을 받지 않습니다. SQL Server/SQLite 특화 검색은 Infrastructure 구현 안의 명시적인 기능 계약과 Provider별 테스트로 좁힙니다.
- 한 scoped `DbContext`를 병렬로 재사용하지 않습니다. 긴 LLM 호출 동안 DB 트랜잭션이나 컨텍스트를 유지하지 않습니다.
- DbContext pooling, compiled query, bulk update/delete, raw SQL은 실측 병목 및 검토된 의미가 있을 때만 도입합니다.
- N+1 관계 조회를 피하도록 페이지 단위 projection/join을 설계합니다. 필요한 aggregate load 범위를 통합 테스트로 확인합니다.

## 5. 트랜잭션 및 동시성 차이

### SQLite

- SQLite는 동시 작성자를 여러 개 처리하지 못하며 쓰기는 serialize됩니다. 오래 걸리는 트랜잭션, 여러 스레드의 동일 connection 사용, 외부 네트워크 파일 시스템 DB 경로를 피합니다.
- WAL은 독립적으로 검토할 수 있지만 파일 보안/백업/Checkpoint 정책을 문서화하고 테스트하기 전 활성화하지 않습니다. WAL/SHM을 단독 DB 백업으로 복사하지 않습니다.
- busy timeout, journal mode, connection 설정은 SQLite Provider 범위에서만 구성합니다. Migration·백업·복구의 locking을 함께 테스트합니다.

### SQL Server

- 적절한 Isolation Level, deadlock 진단, DB connection pool 및 조직의 availability/backup 정책을 DBA와 합의합니다.
- deadlock/일시 장애 재시도는 멱등성과 전체 트랜잭션 경계가 검증된 작업에 한정합니다. Provider 실행 전략과 명시 트랜잭션의 재시도 사용법을 확인합니다.
- SQL Server `rowversion`과 동일한 기능이 SQLite에 있다고 가정하지 않습니다. 공통 동시성은 application-managed Version을 사용합니다.

양 Provider에서 Memory Payload, lookup token, revision이 원자적으로 일관되도록 같은 유스케이스 계약 테스트를 유지합니다. 오류·취소 발생 후 부분 쓰기 여부를 확인합니다.

## 6. Provider 이식성 및 SQL 차이

| 주제 | SQLite 주의사항 | SQL Server 주의사항 | 공통화 정책 |
|---|---|---|---|
| ID/Boolean | GUID 문자열/INTEGER 저장 매핑 | `uniqueidentifier`/`bit` | 비교 가능한 CLR 타입 및 Migration 변환 시험 |
| 시간 | UTC 정규 text 저장/정렬을 확인 | `datetime2(7)` | UTC 정규화, `Kind` 복원, 경계값·범위 쿼리 Provider 테스트 |
| 문자열 | 기본 collation/대소문자 정책 다름 | DB/컬럼 collation 설정 | 검색 문자열 명시 정규화; DB 기본 case-insensitive 가정 금지 |
| 숫자 | dynamic typing, decimal 연산 한계 | `decimal` 정확도 및 정밀도 설정 | confidence는 제한된 정수 스케일; 입력 범위 검증 |
| JSON | JSON 함수가 환경/버전에 의존 | SQL Server JSON 함수/버전 차이 | 민감 Payload JSON은 암호화 후 앱에서 schema 검사; JSON column 질의 금지 |
| DDL/Migration | SQLite의 일부 ALTER 제약·table rebuild | T-SQL DDL·락/실행 옵션 | Provider별 migration/script 및 복구 리허설 |
| 제약/잠금 | writer serialize | lock/isolation/deadlock | 업무 의미만 공통화하고 전용 설정 분리 |
| 암호화/바이너리 | DB 파일 기본 암호화 안 함; BLOB | 별도 TDE/backup 옵션 | 필드 AEAD 앱 처리와 저장 매체 암호화를 분리 |
| SQL 함수 | SQLite 함수/문법 전용 | T-SQL 및 함수별 동작 | EF 공통 LINQ 또는 각 Provider 구현+동일 계약 테스트 |

UTC `DateTime` converter의 저장 형식·`Kind` 복원·precision·범위 정렬을 양 Provider에서 직접 확인합니다. SQLite에 다른 `.Value` 표현이나 provider type을 숨겨 쿼리 성공을 가장하지 않습니다. 다른 타임존 표기가 필요하면 UTC instant와 별도의 명시적 timezone identifier로 표현합니다. DB 간 이동은 데이터 export/import 및 명시 변환으로 별도 수행합니다.

## 7. 암호화 비용 및 민감 검색

- 필요한 page만 복호화하고 HTTP/LLM 응답에 쓰는 필드만 처리합니다.
- 필드마다 AES-GCM을 적용하면 nonce/tag/storage overhead와 CPU 비용이 생기므로 이를 보안 필요성과 함께 측정하되 저장 최적화를 위해 암호화를 끄지 않습니다.
- HMAC token 인덱스는 작은 질의의 스캔을 줄일 수 있지만 토큰 생성, 키 버전, 회전·dual lookup 비용과 metadata 누출을 만듭니다.
- 일정 건수 이상의 제한된 전 메모리 복호화를 대안으로 검토한다면 먼저 권한 범위·메모리 peak·응답 시간과 누출 위험을 측정합니다. 이에 따라 현재 토큰 방식을 임의 확정하지 않습니다.
- plain LIKE/fulltext가 암호화 본문을 검색할 수 있다고 가정하지 않습니다.

## 8. 측정 및 캐시 기준

실데이터 대신 합성 데이터로 시작하고, 실제 요구가 있을 때 개수·테넌트 분포·페이지 크기를 기록합니다. 각 provider/version, 설정, CPU/RAM, SQLite 파일 배치, SQL Server 인스턴스/네트워크 지연, payload 암호화 여부를 함께 적습니다.

- DB 질의·직렬화·암호화 시간을 LLM 추론 및 API 전체 응답과 분리합니다.
- 대표 사용 사례별 평균/중앙값/p95, 처리량, 오류/lock timeout, row/page 수, 읽기/쓰기, 인덱스 크기 및 query plan을 기록합니다.
- 측정된 빈도·지연 이득과 무효화·사용자/Tenant 격리 정책이 함께 확인되기 전에는 캐시를 도입하지 않습니다. 민감한 user-specific memory response를 공유 cache에 보관하지 않습니다.
- 실제 query plan과 write amplification을 확인하고 인덱스를 추가/제거한 뒤 양 Provider에서 회귀 검사합니다.

## 9. 운영·Migration·백업 비용

Migration과 복호화/re-encryption은 대규모 테이블에서 서비스 정지·락을 일으킬 수 있으므로 staging test data로 처리량과 실패 복구를 리허설합니다. SQLite 개인 설치는 쓰기를 안정적으로 막고 consistent backup을 확인합니다. SQL Server는 승인된 배포 계획과 잠금 영향을 계획합니다. 적용 시간/오류/버전 상태는 민감 SQL·값 없이 기록합니다.

복원 테스트는 전체 row 수, Tenant/User 경계, FK 무결성, migration history, 암호문 tag 및 key access, 페이지 조회, 삭제 정책을 확인합니다. 단순 backup 파일 존재를 복구 가능성으로 간주하지 않습니다.

## 10. 아직 확정하지 않은 것

- 업무별 SLO·최대 DB 크기·동시 요청·Tenant 규모 및 page size
- HMAC token lookup과 bounded decrypt scan의 선택
- SQLite WAL 및 connection 옵션
- SQL Server Isolation, retry/timeout 및 운영 인덱스
- DbContext pooling, cache, provider-specific projection 필요 여부
- Migration 도구/자동화·다운타임·backfill 계획

실측·위협 모델·기업 운영 요구가 없는 상태에서 이 값을 특정 제품의 보장치로 작성하지 않습니다.
