# 16. Memory 설계

## 구현 상태

Memory 및 StructuredFact의 CRUD·검색·수정 이력은 [28. Memory UseCase](./28_MEMORY_USECASE_DESIGN.md), [29. StructuredFact](./29_STRUCTURED_FACT_DESIGN.md), [30. API 계약](./30_API_CONTRACT.md), [31. 검색 설계](./31_SEARCH_DESIGN.md)에 구현되어 있습니다. 다만 사용자 인증/인가, 일반 Tenant/User 프로비저닝, 자동 대화 추출, LLM Tool, Memory 본문 전문 검색은 없습니다. API는 개발 환경의 명시적 고정 Tenant/User scope에서만 작동하고 운영 인증을 제공하지 않습니다.

현재 Memory 검색은 유형·활성 여부·UTC 시간 조건만 지원합니다. StructuredFact 검색은 FactType·대상·정확 값·날짜 범위·생일 월을 지원하며 검색 토큰 정규화는 구현 계약을 따릅니다. 키 회전 및 과거 키 병행 검색은 아직 구현되지 않았습니다. 설계 문서에 미래형으로 기술된 동작은 구현 완료로 간주하지 마십시오.

## 1. 대화와 장기 기억의 경계

| 데이터 | 목적 | 저장 모델 | 수명 주기 |
|---|---|---|---|
| Conversation / Message | 사용자가 진행한 대화, 화면 이력, 세션 문맥 | `Conversation`, `ConversationMessage` | 독립 보존·삭제 정책; 모든 대화를 매 요청 모델에 재전송하지 않음 |
| Long-term Memory | 재사용하도록 승인·검증된 사실/선호/절차 | `MemoryItem`, Payload, Source, Revision, Token | 사용자 소유 기억의 별도 수정·검증·삭제 수명 주기 |

대화 전문은 기억의 기본 저장 위치가 아닙니다. Memory는 특정 대화가 끝나거나 보존 기간이 지나도 남을 수 있지만, 사용자의 정정·철회·삭제는 대화 보존과 별도로 처리합니다.

## 2. Memory 유형과 구조화 기준

| 유형 | 예 | 모델 |
|---|---|---|
| Profile | 이름·사용자 설정 | 타입이 있는 Memory Payload; 최소 정보만 |
| Preference | 답변 언어·형식 선호 | 분류된 구조화 값 |
| Fact | 생일·주소·관계 등 | FactType + 버전 있는 typed Payload |
| Work | 프로젝트/업무의 지속 사실 | 업무 범위와 소유권을 명시 |
| Procedure | 반복 작업 단계 | 절차 버전과 승인 상태 |
| Decision | 과거 의사결정 | 결정/근거/유효 여부를 분리 |
| ConversationExtract | 특정 대화에서 승격한 기억 | Source로 원본 대화 메시지와 출처 시점을 연결 |

구조화 값은 날짜·수량·상태를 application schema에서 검증합니다. DB 검색에 필요하지 않은 정보까지 평문 열로 펼치지 않습니다. 복호화 후에만 해석되는 JSON은 DB native JSON 검색 기능으로 질의하지 않습니다.

## 3. 신뢰·출처·검증

- `ConfidenceScore`는 0..10,000 정수 척도로 저장하는 후보입니다. 이 숫자는 확률의 정확성으로 오해하지 않도록 출처·계산 정책을 함께 문서화해야 합니다.
- confidence(모델/출처의 평가), verification status(사용자 확인/반려 등), lifecycle(active/valid interval)는 별개 속성입니다.
- 사용자의 명시 입력은 `DirectInput` 출처로 기록하고, 대화 추출은 Message 참조와 `ConversationExtract` 유형을 기록합니다. 출처 전문을 다시 복사하지 않습니다.
- 사용자 정정·승인 전 LLM 추출물은 Proposed 상태로 유지하며 답변의 확정 사실이나 쓰기 대상으로 사용하지 않습니다.
- 상충하는 값을 자동 덮어쓰지 않습니다. 현재 값, 이전 값, 각각의 근거 및 유효 구간을 확인해 수정 의도를 해결합니다.

## 4. 저장·갱신 흐름

1. 입력에서 기억 후보를 만들되 본문 전체를 저장 후보에 복제하지 않습니다.
2. 스키마 버전과 허용 FactType에 대해 검증하고, 날짜·숫자·범위를 확인합니다.
3. API가 인증 문맥에서 Tenant/User를 설정하고 기존 활성 값과 충돌을 찾습니다.
4. 모호·민감·충돌 값은 필요한 사용자 확인을 얻기 전까지 확정 상태로 올리지 않습니다.
5. Payload를 앱 계층에서 암호화하고, 허용된 검색 토큰을 별도 계산합니다.
6. MemoryItem 버전 증가, MemoryPayload 변경, 기존값의 encrypted revision, source 및 token의 추가/폐기를 하나의 트랜잭션으로 저장합니다.
7. 성공 이후 Memory ID·상태 등 최소 결과를 반환합니다. 복호문은 로그 및 광범위한 DTO에 넣지 않습니다.

`Version` 기반 낙관 동시성 검사가 실패하면 최신 값을 다시 조회하고, 사용자의 변경 의도가 아직 유효한지 재확인합니다. 무조건 마지막 쓰기를 승자로 취급하지 않습니다.

## 5. 구조화 사실: 생일

`홍길동은 7월 15일이 생일`을 구조화할 때:

- `MemoryStructuredFact.FactType`: `Person.Birthday`
- 암호화된 schema-versioned Payload: 정규화된 대상 이름, 월=7, 일=15, 연도(사용자가 제공하고 필요할 때만)
- `MemorySource`: 출처 종류, 입력 시각, 해당 Message 참조(대화 추출인 경우), 신뢰 정보
- `MemoryLookupToken`: 허용된 HMAC 검색 토큰
- `MemoryItem`: 소유자, 상태, confidence, 민감도, 버전, 유효 구간

윤년·불완전한 날짜 등 데이터 품질 규칙은 FactType schema validator에 둡니다. 월 단위 질의의 결과는 해시 토큰으로 먼저 제한하고, 권한 범위와 활성 상태를 확인한 후 필요한 페이지를 복호화합니다. 대상 이름은 exact HMAC token으로 대상을 좁힙니다. 토큰은 값을 직접 검색하거나 복호화할 수 없고, 알고 있는 낮은 엔트로피 값에 대한 추측 위험 및 반복값 누출은 별도 분석이 필요합니다.

월별 토큰은 사용자/테넌트 범위가 분리된 HMAC 키/입력 문맥을 사용하고, token type 및 key version을 저장합니다. 키 회전 중에는 새/이전 버전 토큰을 일시 병행 생성·검색할 수 있으나 마이그레이션 완료 후 구버전 토큰을 제거합니다. 정확한 token canonicalization은 구현 ADR/암호화 계약 테스트에서 고정합니다.

## 6. 허용 조회와 LLM 경계

- DB 조회는 Application의 목적별 UseCase(예: 월 생일 조회, 특정 대상의 생일 조회)와 typed 인자만 받습니다.
- SQL·predicate·컬럼 이름을 LLM이 생성하도록 하지 않습니다. 사용자 입력이나 모델 출력으로 동적 SQL을 조립하지 않습니다.
- UseCase는 사용자/Tenant 범위를 서버 문맥으로 전달하고, provider 쿼리에 매개변수 바인딩·상한·정렬을 적용합니다.
- LLM에는 명시적으로 허용된, 최소 범위의 조회 결과만 전달합니다. 답변은 그 결과에 근거하며 결과 없음/불확실성을 숨기지 않습니다.
- 페이지네이션 결과를 전체 메시지/메모리로 합쳐 무제한 전달하지 않습니다.

## 7. 대화에서 기억으로 승격

- 모든 메시지를 자동 장기기억으로 승격하지 않습니다. 제품 UX와 인증된 사용자의 의도에 따라 후보를 명시적으로 확인하는 흐름을 둡니다.
- 승격 후보는 source message reference, 추출 시각, extraction schema/model metadata(민감 프롬프트 원문은 제외)를 기록합니다.
- Schema/권한/충돌 검증 및 사용자 확인 전에는 Memory 상태를 Active로 전환하지 않습니다.
- 대화 전문의 삭제는 별도 대화 정책에 따릅니다. 메시지 원문이 지워진 뒤에도 사용자가 유지한 Memory는 독립 수명 주기를 가질 수 있습니다. 해당 memory 삭제는 모든 source links, tokens, payloads, revisions를 제거합니다.
- 대화 요약은 최근·관련 문맥에 대한 제한적 보조 자료이며, 원본 사실이나 영구 기억으로 자동 신뢰하지 않습니다.

## 8. 수정·비활성화·삭제

- 수정은 이전에 활성인 암호화 Payload를 Revision snapshot으로 보존하고, 새 값·출처·토큰을 같은 DB 트랜잭션에 반영합니다.
- 되돌릴 수 있는 제품 수준 논리 삭제는 `IsActive=false`, `DeletedAtUtc` 및 `ChangeType=Deleted` Revision으로 표시합니다. 이때 이전 Payload를 포함하는 Revision이 남으므로 논리 삭제는 개인정보 완전 삭제가 아닙니다.
- 개인정보 완전 삭제/법적 파기 요청은 Memory row, 활성 Payload, structured metadata, lookup tokens, 모든 revision snapshots, sources와 파생 검색 데이터를 물리 삭제합니다. 백업·로그·복제본은 별도 보존/파기 절차에 반영하고 사용자에게 복구 가능 기간을 알립니다.
- 암호화된 revision도 개인정보이므로 무기한 보존하지 않습니다. revision 보존 기간과 법률상 예외는 제품/조직 정책에서 정해야 합니다.
- 삭제 이력 보존 여부·보존 기간·법률상 예외는 미결정입니다. 최소 감사 기록이 필요하다면 기억 원문/ID를 보관하는 Revision과 분리해 목적, 범위, 접근 통제, 보존 기한을 정하고 식별 가능한 metadata를 최소화합니다.
- 완전 삭제 후 백업 복원 시 삭제 ledger와 법적 보존 기간을 검증하고 해당 기록을 다시 삭제합니다. 별도 정책 없이 삭제된 기억을 자동 재수입하지 않습니다.

## 9. 미결정 항목

- Confidence 계산식/임계값, 기억 승격 UX 및 사용자 확인이 필요한 민감 등급
- fact predicate registry와 범용 구조화 schema 버전 정책
- 기억·대화·revision 보존 기간과 법률상 hold/삭제 정책
- 공유·조직 업무 기억의 접근 권한 및 상속 규칙
- HMAC 토큰 유형, 사용자별 또는 테넌트별 키 범위, 회전 일정과 실제 사전 공격 위협 모델
- 대화 요약 정책, 원본 메시지 삭제와 Memory 출처의 최종 상호작용

이 항목은 구현 단계에서 사용자/조직의 구체적인 보안 요구를 확인하고, 확정 전 임의 기본값으로 고정하지 않습니다.
