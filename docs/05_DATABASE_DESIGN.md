# 05. 데이터베이스 설계 원칙

## 1. 목표

초기 개인용 SQLite 구현을 빠르게 제공하면서 향후 MSSQL 및 다른 DB로 이전할 때 Domain/Application을 대규모 수정하지 않도록 합니다.

## 2. 초기 기술

- ORM: Entity Framework Core (초기 ORM 결정; 구현 패키지는 다음 단계에서 추가)
- 초기 Provider: SQLite
- 후속 Provider: Microsoft SQL Server
- DB 스키마 변경: EF Core Migration
- DB별 Provider와 Migration은 분리
- 매개변수화된 Provider 전용 SQL은 Infrastructure 내부에서 EF Core 표현의 한계와 측정된 병목이 확인된 경우에만 검토

Dapper를 기본 데이터 접근 기술로 함께 도입하지 않습니다. EF Core 쿼리·실행 계획 최적화 후에도 특정 조회에 성능 문제가 남는다는 측정 근거가 있을 때만 범위를 한정해 별도 검토합니다. Provider 전용 SQL은 각 Provider 계약 테스트로 검증합니다.

패키지 버전은 .NET 10과 호환되는 안정 버전을 구현 시 확인하고 중앙 관리합니다.

## 3. 핵심 엔터티 초안

| 엔터티 | 목적 |
|---|---|
| Tenant / User | 개인 및 기업 사용자 소유 범위 |
| Conversation / ConversationMessage | 장기 기억과 별도로 보존하는 대화 세션·메시지 |
| MemoryItem / MemoryPayload | 기억 메타데이터와 암호화된 본문 |
| MemoryStructuredFact | 검색 유형 및 구조화 Payload 스키마 버전 |
| MemorySource | 기억의 출처와 대화 메시지 연결 |
| MemoryRevision | 암호화된 변경 이력 |
| MemoryLookupToken | 허용된 제한 검색을 위한 HMAC 인덱스 |

실제 컬럼·관계·인덱스 후보는 [15. 데이터 모델](./15_DATA_MODEL.md)을 따르고, 양 Provider Migration 테스트와 보안 검토 후 확정합니다. 이 문서는 확정된 DB 스키마가 아닙니다. 스키마 버전은 기본적으로 Provider별 EF Migration 이력에 기록합니다.

구체적인 Provider 구성, 데이터 모델, 메모리 수명 주기, 암호화 및 성능 설계는 [14. 데이터베이스 아키텍처](./14_DATABASE_ARCHITECTURE.md), [15. 데이터 모델](./15_DATA_MODEL.md), [16. 메모리 설계](./16_MEMORY_DESIGN.md), [17. 데이터 보안 및 암호화](./17_DATA_SECURITY_AND_ENCRYPTION.md), [18. 데이터베이스 성능](./18_DATABASE_PERFORMANCE.md)를 기준으로 합니다.

## 4. DB 이식 규칙

- Domain에서 EF Core 타입을 사용하지 않습니다.
- Repository 계약은 업무 의미 중심으로 정의합니다.
- DB별 SQL은 Infrastructure 내부에 둡니다.
- 날짜/시간은 UTC 저장 원칙을 적용하고 사용자 시간대는 별도 보관합니다.
- 금액·수량·날짜 등은 DB 간 호환 가능한 타입으로 매핑합니다.
- SQLite의 동적 타입 특성에 기대지 않습니다.
- MSSQL 전환 시 별도 Migration을 생성하고 실제 SQL Server에서 통합 테스트합니다.
- 단순히 Connection String만 변경하면 전환된다고 문서에 표현하지 않습니다.

## 5. 검색과 암호화

개인정보 원문은 암호문으로 저장합니다. 암호문 컬럼에 일반 LIKE 검색을 수행하지 않습니다.

생일 월별 조회는 설계 대안이 있습니다.

- 원본 생일 값은 암호화합니다.
- 월·일 검색을 위한 별도 인덱스 토큰을 만들 수 있습니다.
- 토큰은 별도 키를 사용하는 HMAC-SHA-256 기반으로 검토합니다.
- 월 조회는 해당 월의 1~31일 후보 토큰을 생성해 검색할 수 있습니다.
- 토큰도 반복값과 동등성 정보를 노출하므로 민감 메타데이터로 취급합니다.
- 키가 없는 상태에서 토큰만으로 원본을 복원할 수 없도록 합니다.

이 방식은 설계 후보이며, 위협 모델·검색량·개인정보 정책 검토 후 구현을 확정합니다. 작은 개인 DB에서는 권한 확인 후 복호화된 제한 집합을 애플리케이션에서 필터링하는 단순 대안도 측정해 비교합니다.

## 6. 인덱스 원칙

- 사용자/소유 범위와 조회 조건을 함께 고려합니다.
- 실제 쿼리 패턴을 기준으로 인덱스를 설계합니다.
- 과도한 인덱스는 쓰기 성능과 저장 공간을 악화시키므로 측정 후 추가합니다.
- 모든 조회는 사용자·테넌트 범위 필터를 누락하지 않습니다.

## 7. 데이터 이전

Export/Import는 버전이 있는 중립 포맷(JSON Lines 또는 명시적 JSON 스키마 등)을 사용합니다.

이전 절차:
1. 원본 DB 백업
2. 스키마/데이터 버전 확인
3. 대상 DB Migration
4. 데이터 내보내기
5. 암호화 키 이전 또는 재암호화 정책 확인
6. 대상 DB 가져오기
7. 레코드 수·필수 필드·참조 무결성 검증
8. 샘플 복호화 및 업무 조회 테스트
9. 결과 보고 및 원본 보존

암호화 키 없이 암호문만 복사하면 데이터 복호화가 불가능할 수 있으므로, 키 백업·복구 절차는 데이터 백업과 별도로 검증합니다.
