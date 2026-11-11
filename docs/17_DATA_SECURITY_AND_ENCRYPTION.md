# 17. 데이터 보안 및 암호화

## 1. 보안 목표

데이터베이스는 서비스와 별도 권한을 가진 사람이 파일·백업·스토리지에 접근할 수 있음을 가정합니다. 개인 정보의 저장 기밀성, Tenant/User 격리, 인증된 암호문 무결성, 최소 노출 및 복구 가능성을 함께 다룹니다. 저장 암호화는 실행 중인 프로세스/메모리 접근을 막지 못합니다.

## 2. 데이터 분류 및 보호

| 분류 | 예 | DB 저장·보호 |
|---|---|---|
| 운영 식별 메타데이터 | GUID, enum 상태, 순서, 버전, UTC 시각 | 평문 저장. 최소 보존, 범위/권한 검증, 필요 없는 API·로그 노출 금지 |
| 계정/프로필 개인정보 | 사용자 표시명, 선택적 인증 식별자 | 기본적으로 앱 계층 암호화. 검색 필요성이 입증되기 전 평문 인덱스 금지 |
| 대화 원문·요약 | 사용자·assistant 메시지, 제목/요약 | 본문/요약 암호화; 로그·analytics·오류 응답에 복사하지 않음 |
| Memory 내용 | 프로필, 사실, 선호, 업무, 절차, 결정 | `MemoryPayload` 암호화. 구조화 사실도 민감하면 동일 원칙 |
| Memory 출처 및 변경 이유 | 원본 메시지 연결, 자유 텍스트 이유 | 최소 메타데이터만 두고 설명 원문은 암호화 또는 저장하지 않음 |
| 검색 인덱스 토큰 | 유형·월·대상 HMAC | 원문 복원이 어렵더라도 반복/동등성 노출이 있어 민감 메타데이터로 접근 통제·백업 보호 |
| 암호 키 | AES/HMAC key bytes | DB와 분리된 OS 보호 저장소/승인된 서버 key store. 평문 설정·로그·Git 금지 |

모든 ID·시각·상태 코드 암호화는 하지 않습니다. 단, 평문 메타데이터 자체가 사용자 관계와 활동을 드러낼 수 있으므로 필요한 범위로 제한합니다.

## 3. 필드 암호화 형식

SQLite 구현은 .NET `AesGcm` API의 AES-256-GCM을 사용합니다. Envelope 포맷은 `EncryptedPayload`에 저장하며, 구체 처리와 미지원 항목은 [21. 암호화 구현](./21_ENCRYPTION_IMPLEMENTATION.md)을 참고하십시오.

저장 레코드는 최소한 다음을 구분합니다.

- `FormatVersion`
- `KeyId`
- 96-bit random `Nonce`
- `Ciphertext`
- 128-bit `AuthenticationTag`

각 암호화 시 키별 CSPRNG nonce가 새로 생성되고 재사용되지 않도록 합니다. AEAD AAD에 entity kind, TenantId, UserId, record ID, field/payload purpose, FormatVersion을 포함해 ciphertext를 다른 사용자·행·필드로 복사해도 검증 실패하도록 합니다. AAD는 비밀은 아니지만 무결성 보호 대상입니다.

Payload 직렬화는 명시적으로 버전이 있는 UTF-8 canonical representation을 사용합니다. 복호화 시 JSON/schema를 다시 검증합니다. 잘못된 tag·KeyId·format은 오류로 알리고 빈 문자열/평문 fallback을 사용하지 않습니다. 암호 예외·복호된 payload를 로그로 남기지 않습니다.

## 4. 키 계층, 격리 및 회전

- 대칭 키와 HMAC 키는 서로 분리하고 도메인 분리합니다. SQL DB, SQLite DB, 같은 DB의 설정 테이블 또는 같은 백업 평문 파일에 root key를 저장하지 않습니다.
- 개인 설치에서는 Windows DPAPI CurrentUser로 암호화/HMAC 키를 별도 파일에 보호합니다. 사용자 profile 이동, OS 계정 복구, 재설치 시에는 같은 Windows 사용자 컨텍스트와 별도 백업 절차가 필요합니다.
- 기업용은 운영체제/서비스 계정과 조직의 승인된 key-management 서비스·키 저장소를 사용합니다. 구체 Provider는 배포 환경과 조직 정책이 확인되기 전 미결정입니다.
- Envelope/파생 키를 채택할 경우 root key에서 목적/tenant별 encryption 및 HMAC key를 독립 파생하고, root key 식별자와 생성 시점을 DB 외부 key catalog에 관리합니다. 이 계층과 key 파생 규칙은 암호화 ADR로 추가 확정해야 합니다.
- 실제 구현에는 단일 활성 키 쌍만 있습니다. 키 회전 및 기존 데이터 재암호화는 구현되지 않았으며, 키 ID를 저장하더라도 과거 키를 자동 복구할 수 없습니다.
- 단일 테넌트 키 삭제로 전체 DB를 암호학적으로 지울 수 있는지, 사용자/테넌트별 키 격리 비용과 함께 평가합니다. 키를 삭제하기 전 모든 백업 및 보존본에 대한 효과를 확인해야 합니다.

## 5. 민감 검색과 인덱스 토큰

암호문 자체는 보통 값 범위·LIKE·정렬 검색을 지원하지 않습니다. 검색을 위해 전체 DB를 복호화하거나 일반 해시를 저장하지 않습니다.

- 승인된 exact-match·bounded lookup에만 HMAC-SHA-256 토큰을 고려합니다. 키는 암호화 키와 별개로 유지하고 Tenant/User/TokenType 문맥을 포함해 교차 사용자 일치성을 줄입니다.
- 생일 month token은 월 후보를 직접 조회하는 데 사용할 수 있지만 같은 월 값의 반복과 선택된 질의 결과가 노출됩니다. 대상 이름 token도 반복·동등성 정보를 드러냅니다.
- 생일 월은 낮은 엔트로피 값이므로 비밀 key 없이 단순 SHA-256을 계산한 평문 인덱스는 brute-force를 허용할 수 있습니다. HMAC도 키 침해 또는 선택 질의에 대한 노출을 없애지 않습니다.
- DB 밖의 secret key를 공격자가 얻지 않는 한 토큰만으로 원문을 계산하기 어렵게 하되, 권한 있는 앱이 실제 질의를 수행한 결과 수와 패턴은 부채널로 고려합니다.
- 토큰은 `TokenType`, `KeyId`, 버전으로 구분하고 tenant/user 범위를 포함하는 복합 인덱스만 추가합니다. 일반 로그·metric에 토큰 바이트나 원문을 출력하지 않습니다.
- 토큰 생성/정규화/회전/갱신 일관성 테스트 및 생일 조회 성능/metadata leakage threat model이 완료되기 전 최종 채택은 미결정입니다.

## 6. SQLite 파일·MSSQL 암호화 차이

- 애플리케이션 필드 암호화와 DB 파일/볼륨 암호화는 서로 다른 보호 경계입니다.
- SQLite는 기본적으로 DB 파일과 WAL/SHM/임시 파일을 자동 암호화하지 않습니다. 사용자 프로필 ACL, Windows 장치 암호화/BitLocker, 암호화된 백업 저장소 등 OS/디스크 계층 보호를 기본 보완책으로 평가합니다.
- SQLCipher 등 SQLite 파일 암호화 확장은 native binary 배포, 라이선스, 상용 배포 조건, EF Provider 호환성, 파일 형식과 백업 절차를 확인하기 전 채택하지 않습니다.
- SQL Server의 전송 암호화, 저장소 보호, TDE 및 backup encryption은 SQL Server 운영자/배포 보안 구성입니다. 이를 application field encryption과 같은 의미로 취급하지 않습니다.
- SQL Server 전용 Always Encrypted나 DB native encryption은 사용 시 Provider 및 schema 의존성을 갖습니다. 이식성/검색 제한/키 위임 위협 모델 검토 전에 기본 선택하지 않습니다.

## 7. 백업·복구

- SQLite 백업은 실행 중 DB를 임의 복사하지 않고 SQLite 일관된 backup API/검증된 명령으로 생성합니다. WAL mode를 사용하는 경우 DB/WAL의 독립 파일 복사를 일관 백업으로 간주하지 않습니다.
- SQL Server 백업은 DBA 승인 도구로 생성하고, 파일과 키/복호 권한의 분리를 유지합니다.
- 백업 매니페스트에는 제품/스키마 version, Provider, 생성 UTC 시각, 암호화 format/key identifier 등 복구에 필요한 비밀이 아닌 metadata만 포함합니다.
- key backup은 DB backup과 별개 경로·보호·권한·복원 절차를 적용합니다. 동일 평문 복사본에 두지 않습니다.
- 정기 복원 테스트에서 integrity, schema/migration history, row count/참조 무결성, 샘플 AEAD tag와 검색 기능을 확인합니다. 실패/키 부재는 부분 성공처럼 보고하지 않습니다.
- 사용자의 삭제 권한은 오래된 backup에 즉시 반영되지 않을 수 있습니다. 백업 보존 기간, 법적 hold, 삭제된 사용자 데이터의 복원 후 재삭제 절차를 명시해야 합니다.

## 8. Tenant 격리·로그·전송

- 모든 조회/수정/삭제는 인증된 TenantId/UserId와 대상 record 소유권을 검증합니다. EF global query filter는 편의일 뿐 권한 경계로 간주하지 않으며, raw query에도 범위 필터가 필요합니다.
- 외부에 노출되는 메시지/기억 DTO는 필요한 필드만 허용 목록으로 구성합니다. API 오류 및 telemetry에 DB 경로, SQL parameter, ciphertext, 사용자 입력, 키 정보를 포함하지 않습니다.
- DB endpoint/파일은 로컬/내부 네트워크 경계에 둡니다. 이 설계 단계는 외부 AI 또는 사용자 데이터의 외부 송신을 추가하지 않습니다.
- 데이터 외부 공개 전 네트워크·백업·복구 보안 검토를 별도로 진행합니다.

## 9. 미결정 사항과 구현 보안 Gate

- HMAC index가 월별 생일을 노출하는 것을 허용할 수 있는지, 단순 제한 집합 scan보다 위협 대비 이득이 충분한지
- per-user/per-tenant data key 경계, root/master key 수명 주기, OS/enterprise 키 Provider 선택
- SQLCipher 유무 및 상용 라이선스/배포·호환성
- 백업 암호화 포맷·키 복구·보존 기간·삭제 후 restore 정책
- 대화 본문 및 revision 보유·법률상 보존/삭제

실제 구현 전 이 항목을 ADR/위협 모델로 승인하고 합성 데이터에 대한 nonce uniqueness, 암호문 무결성, 교차 Tenant AAD, 키 회전/복원, 로그 비노출 테스트를 마련합니다.
