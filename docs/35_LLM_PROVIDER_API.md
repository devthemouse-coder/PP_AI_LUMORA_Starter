# 35. LLM Provider API

## API 목록

모든 경로는 Development 환경에서만 활성화되며 매 요청에 실시간 Ollama 조회를 수행합니다.

| HTTP | 경로 | 응답 |
|---|---|---|
| GET | `/api/llm/status` | Provider 연결 상태, 버전, 설치 모델 수, 확인 시각, 오류 종류 |
| GET | `/api/llm/version` | Provider 이름과 Ollama 버전 |
| GET | `/api/llm/models` | 로컬 설치 모델의 메타데이터 목록 |
| GET | `/api/llm/models/{modelName}` | 설치 여부 및 일치 모델의 메타데이터 |
| POST | `/api/chat` | 제한된 생일 의도 처리 |
| GET | `/api/conversations` | 대화 목록 |
| GET | `/api/conversations/{id}/messages` | 복호화된 대화 메시지 페이지 |

정상 응답은 `200 OK`입니다. Provider 연결 문제는 공통 Problem Details의 `503 Service Unavailable`, 잘못된 모델 이름은 `400 Bad Request`입니다. 모델 미설치는 조회 실패가 아니므로 availability 응답 본문에서 `NotInstalled`로 표현됩니다.

Chat 및 Conversation 경로 역시 Development 환경에서만 매핑됩니다. Chat은 `message`, `language`, 선택적 `conversationId`를 받습니다. 모델이 반환한 제한 JSON을 엄격하게 파싱하고 명시된 이름/날짜를 입력에서 확인한 뒤, 생일 저장·조회 UseCase만 호출합니다. 모델 오류, 잘림, 빈 content, 지원하지 않는 Intent는 fact 변경을 일으키지 않습니다.

```http
POST /api/chat
Content-Type: application/json

{
  "message": "홍길동의 생일은 5월 10일이야",
  "language": "ko"
}
```

## 응답 예시

```json
{
  "providerName": "Ollama",
  "connectionState": "Available",
  "runtimeVersion": "0.40.15",
  "installedModelCount": 0,
  "checkedAtUtc": "2025-01-01T00:00:00+00:00",
  "errorKind": null
}
```

모델 세부 정보는 Ollama가 실제로 반환한 값만 포함합니다. 예를 들어 설치 모델이 없는 경우 `/api/llm/models`는 빈 `models` 배열을 반환합니다.

## 요청 예제

VS Code REST Client 예제는 [`ollama-api.http`](../src/Lumora.Api/Examples/ollama-api.http)에 있습니다. 서버 기본 주소는 프로젝트 구성에 따라 다를 수 있으며, API 개발 서버를 외부 인터페이스에 바인딩하지 마십시오.

## 상태 의미 및 오류 처리

`connectionState`: `Available`, `Unavailable`, `TimedOut`, `InvalidResponse`.

모델 상태: `Installed`, `NotInstalled`, `RuntimeUnavailable`, `Unknown`. 설치 상태는 모델 추론 가능 여부를 나타내지 않습니다. 내부 예외 상세는 응답에서 제거하고, 오류 응답은 RFC 7807 Problem Details 형식으로 반환합니다.

## 미구현 항목

- 일반 자유 대화, 사용자 인증·인가 및 streaming
- 모델 생성, 다운로드, 삭제 및 설정 변경
- 추론 기반 readiness 검사
- 사용자 인증·인가

API에 상태·모델 정보를 추가하더라도 이번 단계에서 Ollama에 사용자 정보나 대화 내용을 전송하지 않습니다.
