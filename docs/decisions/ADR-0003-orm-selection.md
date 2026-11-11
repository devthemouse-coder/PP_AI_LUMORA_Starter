# ADR-0003: ORM 선택

- 상태: 승인
- 날짜: 2026-11-08

## 맥락

개인용 SQLite에서 시작해 기업용 Microsoft SQL Server로 확장해야 합니다. Domain/Application 계층을 DB 종속성에서 분리하고, Migration·형 변환·Repository 구현을 Infrastructure에 가두어야 합니다. CRUD 편의만으로 별도 ORM을 추가하면 패키지·테스트·트랜잭션 경계가 이중화될 수 있습니다.

## 결정

- 공통 ORM은 Entity Framework Core 10을 사용합니다.
- 실제 DB Provider는 SQLite용 EF Core Provider를 개인용으로, SQL Server용 EF Core Provider를 기업용 확장으로 각각 사용합니다.
- Dapper는 이번 설계에서 도입하지 않습니다. 추후 대표 질의에서 EF Core query plan/할당/지연을 측정하고 EF Core 최적화가 부족한 경우에 한해 해당 Infrastructure 기능에 도입을 검토합니다.
- EF Core 및 SQLite/Design 패키지는 10.0.14 안정 패치를 중앙 버전으로 사용합니다. SQLite Migration은 Infrastructure 안에서 관리하고 SQL Server Provider는 별도 구현 전까지 추가하지 않습니다.

## 근거

- 공식 Migration, change tracking, query projection, async API 및 Provider 생태계를 활용하면서도 Repository 구현을 Infrastructure로 제한할 수 있습니다.
- 단일 ORM 경계는 트랜잭션·테스트·패키지 관리 복잡도를 줄입니다.
- SQLite 및 SQL Server EF Provider가 다른 점을 감추지는 않으며, Provider별 Migration·integration test를 유지합니다.

## 결과 및 주의

- Application Repository 계약은 업무 단위이며 `DbContext`, `IQueryable`, EF 예외를 노출하지 않습니다.
- Domain은 ORM 속성·패키지를 참조하지 않습니다.
- 매핑·집계 로딩·동시성·오류 분류를 별도 설계하고 실제 Provider에서 검증합니다.
- 요구 수준이 복잡한 벌크 연산·query plan 등으로 바뀌면 measured hot path 별로 재검토합니다. Dapper를 관성적으로 함께 도입하지 않습니다.

## 관련 문서

- [14. 데이터베이스 아키텍처](../14_DATABASE_ARCHITECTURE.md)
- [15. 데이터 모델](../15_DATA_MODEL.md)
- [18. 데이터베이스 성능](../18_DATABASE_PERFORMANCE.md)
