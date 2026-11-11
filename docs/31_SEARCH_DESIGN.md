# 31. 검색 설계

## 1. 검색 경로

검색은 Application이 입력을 검증하고 HMAC 토큰을 생성한 다음, Infrastructure가 Tenant/User 조건 및 인덱스 가능한 토큰을 SQLite 쿼리로 적용하는 방식입니다. 암호화 본문을 복호화해 전체 테이블을 훑지 않습니다.

## 2. 지원 검색

| 엔터티 | 검색 조건 |
|---|---|
| Memory | 유형, 생성/수정 UTC 시간 범위, 활성 여부, 정렬, 페이지 |
| StructuredFact | FactType, Subject 정확 일치, ExactValue 정확 일치, 날짜 범위, 생일 월, 정렬, 페이지 |

- FactType은 분류 메타데이터의 정확 비교입니다.
- Subject 및 ExactValue는 Unicode Form KC / trim / invariant uppercase 정규화 후 scope·용도 분리 HMAC으로 비교합니다.
- 날짜 범위는 양 끝을 포함하는 최대 367일 범위입니다. CalendarDate 토큰은 연도가 명시된 값에만 생성됩니다.
- 월별 생일은 `Person.Birthday`를 대상으로 1~12월을 받고 연도 없는 값도 포함합니다. 2월 29일은 연도 없는 월·일 또는 실제 윤년에만 허용됩니다.
- 목록은 `PageRequest`에서 페이지 번호와 크기를 검증하고 최대 200개를 반환합니다. 기본 API 페이지는 1, 크기는 50입니다.

## 3. 제한과 보안

- 자유 텍스트/부분 문자열/유사도 검색, Memory 본문 검색, 벡터 검색, RAG는 구현하지 않았습니다.
- 대상 검색은 완전 일치이며 접두사·부분 문자열 검색은 불가합니다.
- HMAC 토큰은 동일한 scope와 키 버전에서 같은 검색 값에 대해 결정적이므로 동등성 및 빈도 패턴이 노출됩니다. HMAC 토큰이 원문이나 일반 해시보다 안전한 대안이지만, 사전 대입 위험 및 DB 접근 패턴까지 제거하지는 않습니다.
- 검색 키 회전 시 토큰 재생성·이중 키 조회·마이그레이션은 구현되지 않았습니다. 검색 키를 교체하면 기존 토큰 검색이 동작하지 않을 수 있으므로 기존 키를 덮어쓰지 마십시오.
- Tenant/User 범위는 모든 repository 쿼리에 명시됩니다. 전역 Query Filter를 단독 보안 장치로 사용하지 않습니다.

## 4. 검증

SQLite 통합 테스트는 월별 생일, 연도 없는 날짜, 2000년 2월 29일, 연도 포함 날짜 범위, 대상 검색, 다른 User 차단, 페이지 처리 및 중복/충돌 판단을 합성 값으로 확인합니다. 실행 방법은 [28. Memory UseCase](./28_MEMORY_USECASE_DESIGN.md)와 [29. StructuredFact](./29_STRUCTURED_FACT_DESIGN.md)를 참고하십시오.
