# 29. StructuredFact 설계 및 구현

## 1. 저장 모델

StructuredFact는 기존 스키마를 재사용하며 Migration 변경은 없습니다.

```text
MemoryItem (MemoryType=Fact)
 ├─ MemoryPayload.Value      # 직렬화된 StructuredFactValue의 AEAD 암호문
 ├─ StructuredFact.Value     # 같은 값의 별도 AAD 암호문 및 FactType
 ├─ MemorySource             # 직접 입력/수정 출처
 ├─ MemoryRevision            # 수정 전 MemoryPayload의 암호문
 └─ MemoryLookupToken         # 유형·대상·값·날짜의 HMAC 토큰
```

`MemoryPayload`와 `StructuredFact`는 각각 다른 Entity/Field AAD를 사용합니다. 두 값은 별도로 암호화되며 DB에는 Subject나 TextValue 평문을 저장하지 않습니다.

## 2. 값 검증 및 날짜 의미

값은 대상(`Subject`)과 둘 중 하나로 구성합니다.

- 텍스트: `TextValue`
- 달력 날짜: `Year`(선택), `Month`, `Day`

월·일은 필수이며, 연도가 없으면 임의의 연도를 생성하지 않습니다. 연도 없는 2월 29일은 윤년 기준(2000년)으로 유효한 월·일로 인정합니다. 연도를 지정하면 해당 연도의 실제 달력 날짜인지 검증하므로 2023-02-29는 거부되고 2000-02-29는 허용됩니다. 생일 월 검색은 `Person.Birthday` 유형의 연도 유무와 관계없이 월 토큰으로 조회합니다.

## 3. 생성·중복·수정·삭제

`CreateAsync`는 값 검증과 토큰 생성을 수행하고, 검색 토큰 기준 중복/충돌 검사를 저장 트랜잭션 안에서 다시 수행합니다.

- 동일 소유 범위에서 정보 유형 + 대상 + 정규화 값이 일치하면 409 중복입니다.
- 정보 유형 + 대상은 같지만 값이 다르면 409 충돌입니다.
- 서로 다른 대상의 동일한 사실 값은 중복으로 취급하지 않습니다.
- Subject와 exact-value 토큰은 Unicode Form KC 정규화, 앞뒤 공백 제거, 대문자 정규화 후 HMAC 처리합니다. 대소문자만 다른 텍스트는 같은 값으로 간주됩니다.
- 수정은 기존 `MemoryRevision`에 이전 `MemoryPayload` 값을 암호화해 저장하고, 새 값·토큰·시각을 트랜잭션으로 교체합니다.
- 삭제는 Memory와 함께 StructuredFact, 암호문, Revision, 출처, 검색 토큰을 물리 삭제합니다. 백업 안의 이전 값은 자동 수정되지 않습니다.

SQLite 트랜잭션 내에서 중복 검사를 수행하지만 데이터베이스에 별도의 사실 식별자 유일 제약은 없습니다. 향후 다른 Provider나 동시성 정책 변경 시 동시 생성 경쟁에 대한 유일성 보강을 별도 검토해야 합니다.

## 4. 검색 토큰

| 토큰 | 검색 목적 | 데이터 평문 저장 |
|---|---|---|
| FactType | 정보 유형 | 유형 이름은 `FactType` 메타데이터에도 저장 |
| FactSubject | 대상 정확 일치 | 저장하지 않음 |
| ExactValue | 정규화된 정확 값 | 저장하지 않음 |
| BirthdayMonth / BirthdayDate | 생일 월 및 월·일 | 저장하지 않음 |
| CalendarDate | 연도가 있는 날짜 범위 | 저장하지 않음 |

토큰은 AES 키와 분리된 HMAC 검색 키로 생성하며 Tenant/User 범위와 용도를 입력에 포함합니다. 생일 월 검색은 1~12월을 검증하고 `Person.Birthday`만 대상으로 합니다. 날짜 범위는 시작·종료 날짜가 모두 있어야 하며 최대 367개 날짜를 포함합니다. 범위 날짜 토큰은 검색 키를 한 번만 적재해 생성합니다.

HMAC은 원문 저장을 피하는 검색 수단이지 완전한 개인정보 익명화가 아닙니다. 동일성·빈도 및 검색 패턴이 노출되고, 검색 키를 가진 실행 환경에서는 후보 대입 위험이 있습니다. 키 회전 및 과거 키로 토큰을 재생성하는 기능은 아직 없습니다.

## 5. 테스트 및 제한

통합 테스트는 SQLite 파일에 합성 데이터만 저장해 연도 없는 생일, 2000년 2월 29일, 월·날짜 범위·대상 검색, 다른 User 격리, 중복/충돌, 잘못된 윤일, Revision, 물리 삭제 및 암호문 비노출을 검증합니다. 인증·인가와 실제 운영 사용자 프로비저닝은 미구현입니다.
