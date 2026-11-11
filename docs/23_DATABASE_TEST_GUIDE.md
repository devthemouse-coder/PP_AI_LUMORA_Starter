# 23. 데이터베이스 테스트 가이드

## 1. 원칙

Infrastructure 통합 테스트는 실제 Microsoft SQLite Provider와 임시 DB 파일을 사용하며 EF InMemory Provider를 사용하지 않습니다. 테스트 자료는 임의 GUID 및 합성 문자열/바이트만 사용합니다. Windows DPAPI 테스트는 현재 Windows 사용자 컨텍스트에서 실행합니다.

## 2. 실행 명령

저장소 루트에서 전체 검증:

```powershell
dotnet restore
dotnet build
dotnet test
```

저장소 통합 테스트만 실행:

```powershell
dotnet test tests\Lumora.Infrastructure.IntegrationTests\Lumora.Infrastructure.IntegrationTests.csproj
dotnet test tests\Lumora.Api.IntegrationTests\Lumora.Api.IntegrationTests.csproj
```

각 DB 테스트는 임시 폴더의 SQLite 파일에 Migration을 적용하고 종료 시 연결을 닫은 후 폴더를 제거합니다. 테스트 fixture는 실제 Foreign Key enforcement와 SQLite 제약을 사용합니다.

## 3. 검증 범위

- 초기 Migration, 테이블·인덱스, 적용 Migration 이력, `PRAGMA foreign_keys`, `PRAGMA foreign_key_check`
- Tenant/User scope별 조회 및 페이지네이션, HMAC 검색 토큰 조건 조회
- Conversation/Message 및 Memory/StructuredFact 복합 FK의 cross-owner 거부
- Memory, User, Tenant, Conversation 삭제에서 물리 데이터 및 메시지 출처 링크 제거
- `Version` 기반 낙관적 동시성 충돌 및 UTC DateTime materialization
- DPAPI 키 생성/보호/로드, 키 부재·손상 시 fail-closed, AES-GCM round-trip·Nonce 차이·AAD/Tag/키 오류
- HMAC 토큰의 deterministic 성질, 사용자 범위 분리, 목적 구분 및 입력 유효성
- API Health Check가 DB와 독립적으로 성공하고 readiness가 Migration 적용 전 503, 적용 후 200인지 확인
- SQLite WAL 활성 상태에서 Online Backup 및 동시 쓰기, 1MiB 경계를 넘는 암호화 백업의 스트리밍 복원
- 백업 헤더/태그 변조, 잘림, 기존 대상 파일 보존, 명시적 승인 전후 DB 교체 및 암호화 사전 백업 복원
- PBKDF2/AES-GCM 키 복구 파일의 잘못된 비밀번호·손상·DPAPI 가져오기 및 AES/HMAC 키 분리
- 설정된 백업 디렉터리에서만 성공 검증 후 보존 개수/기간 정리를 수행하고, 생성 실패 시 기존 백업이 남는지 확인

암호화 테스트용 temporary key directory는 테스트마다 새 GUID로 생성하고 종료 시 삭제합니다. 실제 LocalAppData 키, 개인 DB 파일, 환경별 운영 설정을 테스트 입력으로 사용하지 않습니다.
