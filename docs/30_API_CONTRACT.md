# 30. Memory 및 StructuredFact API 계약

## 1. 보안 상태

현재 업무 API에는 인증·인가가 없습니다. TenantId/UserId는 요청 본문이나 query string으로 받지 않습니다. API는 `Development` 환경에서 `MemoryApi:EnableDevelopmentScope=true`와 유효한 `MemoryApi:TenantId`, `MemoryApi:UserId`가 명시적으로 설정된 경우에만 고정 범위를 사용합니다. 그 외에는 503으로 fail-closed 합니다.

이 개발 범위는 테스트·격리된 개발용일 뿐 운영용 인증이 아닙니다. 저장 대상 Tenant/User 및 키가 사전 구성되어야 하고, 본 저장소에는 일반 사용자/테넌트 생성 API나 초기 프로비저닝 명령이 없습니다. 외부에 노출하지 마십시오.

## 2. HTTP 엔드포인트

| Method | 경로 | 의미 | 결과 |
|---|---|---|---|
| POST | `/api/memories` | 기억 생성 | 201, 생성된 ID 위치 헤더 |
| GET | `/api/memories` | 유형·UTC 시각·정렬·페이지 검색 | 200 |
| GET | `/api/memories/{id}` | 기억 상세 조회 | 200 또는 404 |
| PUT | `/api/memories/{id}` | 기억 수정 및 Revision 기록 | 200, 404 |
| DELETE | `/api/memories/{id}` | 기억 및 관련 정보 물리 삭제 | 204, 404 |
| POST | `/api/structured-facts` | 구조화 사실 생성 | 201, 입력 400, 중복/충돌 409 |
| GET | `/api/structured-facts` | 유형·대상·정확 값·날짜·월 검색 | 200 |
| GET | `/api/structured-facts/{id}` | 구조화 사실 상세 조회 | 200 또는 404 |
| PUT | `/api/structured-facts/{id}` | 사실 변경 및 Revision 기록 | 200, 404, 중복/충돌 409 |
| DELETE | `/api/structured-facts/{id}` | 사실과 관련 기억 물리 삭제 | 204, 404 |
| GET | `/api/structured-facts/birthdays?month=11` | 연도와 무관한 월별 생일 검색 | 200, 월 입력 오류 400 |
| POST | `/api/chat` | 로컬 LLM 의도 추출 후 허용된 생일 UseCase 실행 | 200, 입력 오류 400, Provider 실패 503 |
| GET | `/api/conversations` | 개발 scope의 대화 목록 조회 | 200 |
| GET | `/api/conversations/{id}/messages` | 암호화 저장된 대화 메시지 페이지 조회 | 200 또는 404 |

Chat 및 Conversation 경로는 인증이 구현되기 전까지 Development 환경에서만 등록됩니다. 기존 고정 `MemoryApi` 개발 scope를 사용하며, 요청으로 Tenant/User ID를 지정할 수 없습니다.

## 2.1. 로컬 자연어 대화

`POST /api/chat`은 `message`, `language` (`ko` 또는 `en`), 선택적 `conversationId`를 받습니다. 첫 요청에서 `conversationId`를 생략하면 새 대화가 만들어집니다. 모델은 로컬 Ollama에 제한된 JSON 의도 추출만 수행하고, 백엔드는 허용 목록 및 사용자 메시지의 이름·날짜 근거를 검증한 다음 기존 StructuredFact UseCase를 호출합니다. 모델이 사실 응답이나 DB 성공 여부를 직접 결정하지 않습니다.

```json
{
  "message": "홍길동의 생일은 5월 10일이야",
  "language": "ko"
}
```

지원 Intent는 생일 저장, 특정 이름의 생일 조회, 이번 달 생일자 조회, UNKNOWN입니다. 알 수 없는 정보는 기존 저장 사실이 없다고 응답합니다. 잘못된/빈/잘린 JSON, 외부 도구 의도, grounding에 실패한 날짜는 fact 변경으로 이어지지 않습니다. 정상적으로 파싱된 대화 교환은 기존 Conversation/Message 구조에 암호화해 저장합니다. 실제 인증·인가 및 운영 배포는 미구현이므로 외부에 노출하지 마십시오.

목록 기본값은 1페이지, 페이지 크기 50이며 최대 크기는 200입니다. 정렬 값은 `CreatedAscending`, `CreatedDescending`, `UpdatedAscending`, `UpdatedDescending`입니다. Memory 유형/민감도/검증 상태는 Domain enum 이름을 받습니다.

## 3. 요청 예시

### Memory 생성

```http
POST /api/memories
Content-Type: application/json
```

```json
{
  "type": "Preference",
  "content": "합성 테스트 선호 정보",
  "confidenceScore": 10000,
  "sensitivity": "Personal"
}
```

생성 성공 응답은 201이며, `Location`은 생성한 `/api/memories/{id}` 경로를 가리킵니다.

```json
{
  "id": "00000000-0000-0000-0000-000000000001",
  "type": "Preference",
  "content": "합성 테스트 선호 정보",
  "confidenceScore": 10000,
  "verificationStatus": "Verified",
  "sensitivity": "Personal",
  "createdAtUtc": "2026-01-01T00:00:00+00:00",
  "updatedAtUtc": "2026-01-01T00:00:00+00:00",
  "isActive": true,
  "version": 1
}
```

### 생일 생성

```http
POST /api/structured-facts
Content-Type: application/json
```

```json
{
  "factType": "Person.Birthday",
  "value": {
    "subject": "합성 대상",
    "month": 5,
    "day": 12
  }
}
```

생성 성공 응답은 201이며, `Location`은 생성한 `/api/structured-facts/{memoryId}` 경로를 가리킵니다.

```json
{
  "memoryId": "00000000-0000-0000-0000-000000000002",
  "factType": "Person.Birthday",
  "value": {
    "subject": "합성 대상",
    "textValue": null,
    "year": null,
    "month": 5,
    "day": 12
  },
  "createdAtUtc": "2026-01-01T00:00:00+00:00",
  "updatedAtUtc": "2026-01-01T00:00:00+00:00",
  "confidenceScore": 10000,
  "version": 1
}
```

`year`가 생략되면 연도 없는 생일로 저장됩니다. 별도 문서의 [HTTP 요청 예제](../src/Lumora.Api/Examples/memory-api.http)를 참고하십시오.

## 4. 오류 및 데이터 노출

- 미설정 개발 scope: Problem Details 503.
- 범위 내 존재하지 않거나 다른 User/Tenant 소유인 ID: 동일한 404.
- 잘못된 입력: Problem Details 400.
- 사실 중복/충돌 또는 낙관적 동시성 충돌: Problem Details 409.
- 키가 없거나 복호화가 실패하는 내부 오류는 일반적인 Problem Details 500으로 응답합니다. 응답과 로그에 민감한 예외 메시지·경로·키 값은 포함하지 않습니다.

API는 요청 scope ID를 권한 판단에 사용하지 않으며 DTO도 Domain Entity를 직접 노출하지 않습니다. 예외 로그에는 예외 형식과 상태 코드만 기록하고, 요청 원문/암호문/예외 메시지를 포함하지 않습니다.

## 5. 검증 및 미구현

`Lumora.Api.IntegrationTests`는 scope가 없을 때 503, 개발 scope로 생성·조회·삭제, 요청 body에 다른 Tenant/User ID를 추가해도 서버 설정 범위만 저장되는지 검증합니다. 실제 인증, 사용자 권한, rate limiting, API를 통한 Tenant/User 프로비저닝은 미구현입니다. Swagger OpenAPI 설명은 제공하지만, 이것이 인증 보호를 의미하지 않습니다.
