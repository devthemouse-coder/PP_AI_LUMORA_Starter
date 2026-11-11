# 21. 암호화 구현

## 1. AEAD Envelope

`IEncryptionService`는 Application 계약이고 `AesGcmEncryptionService`는 Infrastructure 구현입니다. 저장 형식은 `EncryptedPayload`의 `FormatVersion`, `KeyId`, 12바이트 Nonce, 암호문, 16바이트 인증 태그로 구성됩니다. 현재 포맷 버전은 1이며 키는 256비트입니다.

모든 암호화에서 `RandomNumberGenerator`로 새 Nonce를 생성합니다. AAD는 고정 목적 문자열, FormatVersion, TenantId, UserId, RecordId, EntityType, FieldName을 길이 구분 형식으로 인코딩합니다. 따라서 암호문을 다른 사용자·레코드·필드에 옮기면 인증 검증에 실패합니다. 복호화 시 잘못된 포맷/KeyId/Tag를 평문 fallback 없이 `CryptographicException`으로 반환합니다.

암호화 대상은 User 표시명, Conversation 요약, Message 본문, Memory Payload, StructuredFact Payload, revision 이전 Payload 및 선택적 변경 사유입니다. 상태·ID·시각·FactType·HMAC 토큰은 검색/무결성 용도의 메타데이터이므로 DB와 로그 노출을 최소화해야 합니다.

## 2. Windows DPAPI 키 보관

`DpapiKeyMaterialStore`는 AES 키와 HMAC 키를 별도로 생성한 후 Windows `DataProtectionScope.CurrentUser`로 보호하여 DB와 다른 디렉터리에 저장합니다. 기본 위치는 `%LOCALAPPDATA%\P.P. AI - LUMORA\keys`이며 각각 `encryption.dpapi`, `search.dpapi` 파일입니다. 파일을 Git이나 DB 백업에 포함하지 마십시오.

키는 자동 생성·교체되지 않습니다. 새 환경에서 DB에 암호화 데이터가 없을 때만 명시적으로 초기화합니다.

```powershell
dotnet run --project src\Lumora.Api\Lumora.Api.csproj -- --initialize-keys
```

명령은 기존 DB가 있으면 Migration 적용 여부와 암호화 행/검색 토큰 유무를 확인합니다. 보호된 데이터가 발견되거나 DB를 안전하게 검사할 수 없으면 중단합니다. 키 디렉터리가 이미 존재하거나 일부 파일만 있으면 덮어쓰지 않습니다. 키가 없거나 DPAPI 복호화 실패 시 기존 데이터를 보호하기 위해 암호화 서비스는 실패합니다.

DPAPI는 같은 Windows 사용자 프로필/복구 조건에 종속됩니다. 다른 사용자/PC 이전에는 아래 별도 복구 파일 기능을 사용하고 실제 복원 테스트를 해야 합니다.

## 3. HMAC 검색 토큰

`HmacSearchTokenService`는 별도 DPAPI HMAC-SHA-256 키를 사용합니다. 입력에 목적 구분(`birthday-month`, `birthday-date`, `birthday-subject`), TenantId, UserId, 정규화된 값이 포함됩니다. 토큰 길이는 32바이트이고 DB에는 KeyId/TokenType과 함께 저장합니다. AES 키로 HMAC을 계산하거나 일반 SHA 해시를 검색 인덱스로 쓰지 않습니다.

월 및 월/일 검색은 정확한 사용자·테넌트 범위가 입력되는 결정적 토큰을 생성합니다. 토큰의 동일성, 빈도, 사용자가 질의한 패턴은 노출될 수 있습니다. 키 침해 또는 선택 질의에 대한 안전성을 보장하는 익명화 수단이 아닙니다.

## 4. 백업 키 복구

키 복구 파일은 원래 AES/HMAC 키와 Key ID를 PBKDF2-HMAC-SHA-256(600,000회, 무작위 16바이트 Salt) 및 AES-256-GCM으로 보호합니다. 복구 비밀번호는 최소 12자이며 콘솔에서 입력합니다. 새 Windows 사용자/PC에서는 복구 파일을 가져와 현재 사용자의 DPAPI로 다시 보호한 다음에만 해당 키로 암호화된 DB 백업을 복원할 수 있습니다.

복구 파일은 DB 및 DB 백업과 다른 디렉터리/오프라인 매체에 보관하십시오. 키 ID, 손상된 인증 태그, 잘못된 비밀번호는 fail-closed로 거부합니다. 복구 비밀번호, 평문 키는 저장/로그하지 않습니다. 상세 절차는 [24. 백업 전략](24_BACKUP_STRATEGY.md)과 [26. 키 복구 안내](26_KEY_RECOVERY_GUIDE.md)를 참고하십시오.

## 5. 현재 한계 및 운영 금지

- 단일 활성 AES/HMAC 키 쌍만 읽습니다. 과거 KeyId용 key ring, 회전, 재암호화, 재토큰 생성/dual-read는 구현하지 않았습니다. KeyId는 향후 버전 식별을 위한 저장 정보이며 회전 기능이 아닙니다.
- DB 전체 파일 암호화/SQLCipher는 구현하지 않았습니다. `.lumora-backup` 파일은 버전 관리형 AES-GCM 스트리밍 형식으로 암호화합니다. DB 파일 및 임시 복원 파일은 여전히 OS ACL/BitLocker/저장소 보호가 필요합니다.
- DPAPI 키를 복구할 수 없으면 새 키로 대체하지 말고 보호된 키 백업을 복구하십시오. 이미 암호화한 데이터를 새 키로 덮어쓰면 복구할 수 없습니다.
- Plaintext, key bytes, nonce/tag, HMAC 토큰을 로그·HTTP 오류·분석 시스템으로 보내지 않습니다. 키 생성은 데이터 저장과 분리된 명령에서만 실행합니다.
- 백업 컨테이너는 자체 형식이며 독립적인 보안 감사를 받지 않았습니다. 포맷을 임의 변경하지 말고 운영 데이터 적용 전에 별도 검토를 받으십시오.
