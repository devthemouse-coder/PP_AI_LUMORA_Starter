# 32. LLM Provider 아키텍처

## 목적 및 범위

현재 로컬 런타임·모델 조회 외에 제한된 자연어 대화 MVP도 제공합니다. 대화는 Development API에서만 호출되며 로컬 Ollama로만 추론합니다. 일반 자유 대화, 스트리밍 및 자동 대체 Provider는 구현하지 않습니다.

## 계층과 호출 흐름

| 계층 | 책임 |
|---|---|
| Application | `ILlmProvider`, `ILlmChatService`, Provider 오류 계약, 의도 검증 및 기존 UseCase 연결 |
| Infrastructure | loopback Ollama 상태·추론 Adapter, HTTP/JSON 변환, 연결 제한, 오류 유형화 및 DI 등록 |
| Contracts | HTTP API 응답 DTO |
| API | 개발 환경의 상태·버전·모델 조회 엔드포인트 |
| Domain | LLM/Provider 의존성 없음 |

흐름은 상태 조회의 경우 `HTTP API → ILlmProvider → OllamaLlmProvider → Ollama localhost API`, 대화의 경우 `HTTP API → ChatUseCases → ILlmChatService → OllamaLlmChatService → Ollama localhost API`입니다. 의도 검증을 통과한 생일 작업만 `IStructuredFactUseCases`로 전달됩니다. Application은 Ollama DTO나 HTTP 응답 객체를 참조하지 않습니다. API 요청·응답 DTO도 Domain 엔티티를 노출하지 않습니다.

`OllamaLlmChatService`는 `POST /api/chat`에 비스트리밍 JSON 요청을 보내고 60초 제한시간, `think:false`, `temperature:0`, `num_ctx:4096`, `num_predict:256`을 적용합니다. 요청과 응답 크기는 제한되고 redirect/proxy를 비활성화합니다. 모델의 `done_reason`을 보존하며 빈 content, 미완료 응답과 잘못된 JSON을 Provider 오류로 분류합니다. Thinking은 최종 content로 사용하지 않습니다.

## Provider 상태

- 상태 API는 버전과 모델 목록을 매번 조회합니다. 결과 캐시는 없으며 `CheckedAtUtc`는 해당 조회를 완료한 시각입니다.
- `Available`, `Unavailable`, `TimedOut`, `InvalidResponse`를 구분합니다.
- 모델 설치 여부(`Installed`, `NotInstalled`)는 추론 성공 여부와 별도입니다. 런타임 조회 실패 시 `RuntimeUnavailable` 또는 확인 불가 `Unknown`을 반환할 수 있습니다.
- 오류 응답과 로그에 내부 예외 원문, URL, 응답 본문을 넣지 않습니다.

## 새 로컬 Provider 추가

새 런타임을 추가할 때에는 Application 계약을 구현하는 Infrastructure Adapter 및 명시적인 전송 정책을 추가하고, 외부 기술 모델을 Application에 노출하지 않습니다. 실제 추론은 별도 보안·데이터 흐름 검토와 테스트 후에만 추가해야 합니다. 외부 클라우드 Provider 또는 자동 fallback은 허용하지 않습니다.

## 현재 제한

- 일반 자유 대화, 스트리밍, 모델 다운로드 및 자동 fallback 미구현
- 대화 API는 현재 Development 환경에서만 매핑되며 사용자 인증·인가가 없음
- 모델 카탈로그 조회에 Ollama `/api/tags` 사용. Ollama가 제공한 `details`만 전달하며 누락된 정보는 추정하지 않음
- `remote_model` 또는 `remote_host`가 표시된 항목은 로컬 모델 목록에서 제외
- 인증·인가 미구현. LLM/Chat/Conversation 엔드포인트는 Development 환경에서만 등록
- 다른 로컬 프로세스가 loopback Ollama에 접근할 수 있는 문제는 애플리케이션 Adapter의 URL 제한으로 해결되지 않음
