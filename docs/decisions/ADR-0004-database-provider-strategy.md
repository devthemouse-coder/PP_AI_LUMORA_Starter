# ADR-0004: SQLite 및 SQL Server Provider 전략

- 상태: 승인 (SQLite 구현 완료; SQL Server 구현 조건부)
- 날짜: 2026-11-08

## 맥락

개인 사용에는 단일 사용자·로컬 파일 기반 SQLite가 적합하고, 향후 기업 설치는 다중 사용자·Tenant 격리·SQL Server 운영이 필요합니다. 연결 문자열만 교체해서 DB 간 스키마·Migration·동시성·SQL 차이가 해결된다고 가정할 수 없습니다.

## 결정

- SQLite는 초기 개인 설치의 기본 Provider로 선택합니다.
- Microsoft SQL Server는 별도 배포 설정과 Provider 구현·Migration·실제 SQL Server integration test가 갖춰진 후 추가합니다.
- Provider 선택은 API Composition Root가 명시적 `Database:Provider` 설정(`Sqlite`, `SqlServer`)으로 수행하고 Infrastructure 등록 확장에 전달합니다. 모르는 값 및 기업 설정 누락을 조용히 SQLite로 fallback하지 않습니다.
- Application 계층의 업무 중심 Repository/Unit of Work 계약을 두 Provider가 각각 구현합니다. EF Core `DbContext`와 provider 타입은 Infrastructure 내부에 둡니다.
- Migration 이력 및 적용 대상은 Provider별로 격리합니다. Provider Migration을 같은 이력 집합으로 섞지 않으며 한 DB에서 만든 migration을 다른 DB에 적용하지 않습니다. 분리가 보장되지 않으면 별도 Migration assembly/project를 둡니다.
- Migration/DB init은 운영 서비스 시작 시 암묵적으로 수행하지 않습니다. SQLite 파일 일관 백업, 기업 SQL Server 승인된 배포와 복구 절차를 적용합니다.

## 근거

- SQLite는 개인 PC 단독 설치의 운영 복잡도를 줄입니다.
- SQL Server는 기업 사용자/권한, 운영 백업, 동시성 요구에 맞는 별도 배포 경로가 됩니다.
- 공통 유스케이스와 데이터 의미를 재사용할 수 있지만 실제 DDL/query/collation/concurrency 및 encryption은 Provider 계약으로 노출·검증해야 합니다.

## 결과 및 주의

- schema/model의 공통 범위와 Provider 특화 기능을 문서화합니다.
- UUID/boolean/time/binary, 문자열 collation, JSON, SQL 함수·인덱스, 트랜잭션과 locking, migration을 별도로 검증합니다.
- SQLite 파일/WAL 백업, 개인 키 보관, SQL Server credential 공급 정책은 DB와 분리해 운영합니다.
- SQLCipher, TDE, Always Encrypted, connection pool/재시도 세부사항은 별도 threat model·운영 검증 전 확정하지 않습니다.
- SQL Server Provider 승인을 Migration, 권한 모델, staging/recovery 테스트 완료와 연결합니다.

## 관련 문서

- [14. 데이터베이스 아키텍처](../14_DATABASE_ARCHITECTURE.md)
- [15. 데이터 모델](../15_DATA_MODEL.md)
- [17. 데이터 보안 및 암호화](../17_DATA_SECURITY_AND_ENCRYPTION.md)
- [18. 데이터베이스 성능](../18_DATABASE_PERFORMANCE.md)
