# 39. Qwen3 CLI와 Ollama API 응답 진단

## 조사 범위

- 실행 시점: 2026-11-09
- Ollama: `0.40.15`
- 모델: 설치된 `qwen3:8b`만 사용
- 질문: `대한민국의 수도는 어디인가요? 한 문장으로 답변하세요.`
- 모든 HTTP 요청: `http://127.0.0.1:11434`에만 전송
- DB, 설정, 방화벽, 설치 모델은 변경하지 않음. 모델 다운로드, Ollama 업데이트, 외부 네트워크 호출도 하지 않음

CLI와 API는 기본값을 우선 비교했습니다. 설치된 Modelfile은 `temperature 0.6`, `top_k 20`, `top_p 0.95`, `repeat_penalty 1`을 지정합니다. 당시 `ollama ps`의 context 표시는 4096이었습니다. API 기본 요청에서는 `options`를 생략했습니다. `ollama run` one-shot 도움말에는 seed, temperature, context를 직접 맞추는 옵션이 없으므로 모든 sampling 조건을 CLI와 API에서 완전히 동일하게 고정할 수는 없었습니다. 이 차이는 결과 해석에 반영했습니다.

## 반복 시험

원시 요청, HTTP 상태 코드, 응답 JSON/NDJSON, CLI 표준 출력 및 실행 시간을 [진단 원시 결과](39_OLLAMA_CHAT_API_DIAGNOSTIC_RAW.json)에 보존했습니다. 기본 질문과 thinking 조건은 각 3회 반복했습니다.

| 조건 | 반복 결과 | 관찰 |
|---|---|---|
| `ollama run qwen3:8b` 기본 | 0/3이 요청에 맞는 답변 | 세 번 모두 입력이 불명확하거나 문자가 혼합된 것 같다는 취지의 응답. 약 7.3–19.9초 |
| CLI `--think=false` | 0/3이 요청에 맞는 답변 | 세 번 모두 부정확한 clarification 응답. 약 0.6–0.9초 |
| `/api/chat`, `stream=false`, `think` 미지정 | 0/3 | 모두 HTTP 200, `done=true`, `done_reason=stop`, content 비어 있지 않음. thinking도 별도 반환됐으나 최종 content는 질문과 무관한 영어 답변. 약 6.8–10.6초 |
| `/api/chat`, `stream=true`, `think` 미지정 | 0/3 | NDJSON을 끝까지 읽은 결과 세 번 모두 마지막 줄에 `done=true`, `done_reason=stop`. 합친 content는 부정확. 약 7.4–8.6초 |
| `/api/chat`, `think=false`, `stream=false` | 0/3 | thinking 필드는 없었지만 답변은 여전히 부정확. 약 0.5–3.1초 |
| `/api/generate`, thinking 기본 및 `think=false` | 각각 0/3 | 기본 thinking에서 부정확한 응답, `think=false`에서도 부정확한 응답. Endpoint를 바꿔도 한국어 문제가 해결되지 않음 |
| `/api/chat`, 영어 ASCII 질문 | 3/3 | 모두 `The capital of South Korea is Seoul.` 반환. 약 2.6–2.9초 |
| `/api/chat`, JSON 유니코드 escape 본문 | 0/3 | UTF-8 한글 본문과 논리적으로 같은 질문이지만 결과는 계속 부정확 |

최초 설치 검증에서 얻은 CLI 한국어 정답은 반복되지 않았습니다. 따라서 관찰된 현상은 안정적인 “CLI 성공 대 API 실패”가 아닙니다. 후속 시험에서 CLI와 두 API endpoint 모두 한국어 입력을 잘못 처리했습니다.

### 제한에 의한 빈 응답 재현

다음 요청 옵션으로 별도 3회 반복했습니다.

```json
{
  "temperature": 0.6,
  "seed": 42,
  "num_ctx": 4096,
  "num_predict": 64
}
```

세 번 모두 HTTP 200이었지만 `message.content`는 빈 문자열, `message.thinking`은 존재했고 `eval_count=64`, `done=true`, `done_reason=length`였습니다. 이는 생성 한도가 thinking 도중 소진되어 최종 답변이 없는 경우를 재현합니다. 이전 cold 실행에서 기록된 64 generated tokens와 빈 content도 이 현상과 부합하지만, 그 실행의 전체 원시 요청 옵션을 보존하지 않아 과거 사례의 원인을 확정하지는 않습니다.

## Ollama 0.40.15에서 관찰한 요청 및 응답 형식

다음 형식은 설치된 `0.40.15`에 loopback 요청을 보내고 응답을 직접 확인한 결과입니다. 최신 버전 동작을 소급 적용한 것이 아닙니다.

기본 단일 메시지 요청:

```json
{
  "model": "qwen3:8b",
  "messages": [
    {
      "role": "user",
      "content": "대한민국의 수도는 어디인가요? 한 문장으로 답변하세요."
    }
  ],
  "stream": false
}
```

이 런타임에서 `think:false`를 지정한 요청은 다음과 같습니다.

```json
{
  "model": "qwen3:8b",
  "messages": [
    {
      "role": "user",
      "content": "대한민국의 수도는 어디인가요? 한 문장으로 답변하세요."
    }
  ],
  "stream": false,
  "think": false
}
```

동일한 단일 prompt에 `/api/generate`를 사용할 때는 `messages` 대신 `prompt` 필드를 사용합니다. 이 경로에서도 반복 한국어 응답은 실패했습니다.

비스트리밍 `/api/chat` 응답에서 확인한 필드는 `message.role`, `message.content`, `message.thinking`(있는 경우), `done`, `done_reason`, `prompt_eval_count`, `prompt_eval_duration`, `eval_count`, `eval_duration`, `total_duration`, `load_duration`입니다. 설치 런타임 응답에는 추가로 `prompt_eval_cached_count`가 포함될 수 있습니다. `/api/generate`는 `response`와 top-level `thinking`을 사용합니다.

`stream=true`는 하나의 완성 JSON이 아니라 줄 단위 JSON(NDJSON)을 반환합니다. 각 줄의 메시지 조각을 누적하고 마지막 `done=true` 줄을 확인해야 합니다. 종료 줄에는 완료 사유와 token/duration 집계 필드가 포함됐습니다. 중간 조각만 표시하거나 마지막 줄 전에 종료하면 content를 최종 답변으로 오인하거나 집계 정보를 잃을 수 있습니다.

## Thinking 동작

- `think`를 생략한 기본 요청에서는 응답의 `message.thinking`(또는 `/api/generate`의 top-level `thinking`)과 최종 `message.content`가 분리되어 반환됐습니다. thinking은 사용자 답변으로 취급하면 안 됩니다.
- `think:false`는 이 설치 런타임에서 별도 thinking 출력을 끄는 효과가 관찰됐습니다. 그러나 한국어 응답 품질은 개선하지 않았습니다. 모델 템플릿은 thinking 설정 시 `/think` 또는 `/no_think` 제어 문자열을 생성합니다.
- 사용자 메시지 끝에 일반 텍스트로 `/no_think`만 덧붙인 조건에서는 thinking 필드가 계속 나타났습니다. 이를 API의 thinking 제어와 동일하다고 가정하지 않습니다.
- CLI의 확인된 비추론 옵션은 `--think=false` 형식입니다. `--think false`로 분리해 전달한 시험은 `false`가 prompt에 들어간 것으로 해석되어 유효한 no-think 비교에서 제외했습니다.

## 원인 판정

| 구분 | 판정 |
|---|---|
| 확인된 사실 | 반복된 한국어 입력에서 CLI, `/api/chat`, `/api/generate`가 모두 실패했고, ASCII 영어 `/api/chat` 대조군은 3/3 성공 |
| 확인된 사실 | UTF-8 직접 전송과 같은 JSON의 유니코드 escape 전송 모두 한국어 실패 |
| 확인된 사실 | `stream=false`와 `stream=true`의 최종 의미 결과는 모두 부정확. 스트리밍은 완료 줄까지 읽었으며 정상 종료를 확인 |
| 확인된 사실 | `think=false`는 thinking 필드를 없앴지만 정답을 복구하지 못함 |
| 확인된 사실 | `num_predict=64`에서는 세 번 모두 `done_reason=length`, `eval_count=64`, 빈 최종 content가 재현됨 |
| 추정 | 단순한 `/api/chat` body 형식이나 JSON escape 방식만의 문제일 가능성은 낮음. 한국어 입력의 model/runtime/tokenization 호환성 또는 모델의 해당 입력 처리 문제가 유력 후보 |
| 미확인 | Ollama `0.40.15`의 특정 버그인지, 모델 artifact·tokenizer/runtime 호환성인지, sampling/프롬프트 요인인지 확정하지 못함. 설치 및 설정 변경이 금지된 범위에서만 조사함 |

그러므로 가장 유력한 설명은 “API만 실패한다”가 아니라 “현재 조합에서 합성 한국어 입력 처리가 불안정하며 최초 CLI 성공은 재현되지 않았다”입니다. 정확한 근본 원인을 확인했다고 보고하지 않습니다.

## LUMORA Provider 영향

현재 `OllamaLlmProvider`와 `ILlmProvider`는 runtime 상태, 버전, 설치 모델 조회만 구현합니다. `LlmChatRequest`, `LlmChatResponse`, `ILlmChatService`는 계약만 있고 추론 구현은 없습니다. 따라서 현재 Provider는 thinking, content 공백, NDJSON streaming, `done_reason=length`를 처리하는 추론 경로가 없으며, 본 조사에서 이를 구현하거나 수정하지 않았습니다.

추론을 별도 단계에서 구현할 때에는 다음을 요구사항으로 남깁니다.

1. 최종 답변은 `message.content`에서만 가져오고 thinking은 사용자 응답·로그에 노출하지 않습니다.
2. `stream=true`이면 NDJSON 각 줄을 검증·누적하고 최종 `done=true` 확인 후에만 성공 처리합니다.
3. 빈 content와 `done_reason=length`는 성공으로 변환하지 말고 제한/비정상 응답으로 구분합니다.
4. 요청 timeout, caller cancellation, HTTP 오류, 잘못된 JSON을 민감한 응답 본문 없이 명시적으로 분류합니다.
5. API 반복 한국어 테스트가 통과하기 전까지 이 모델을 LUMORA 한국어 추론에 연결하지 않습니다.

## 검증 및 다음 단계

- `dotnet test .\tests\Lumora.Infrastructure.IntegrationTests\Lumora.Infrastructure.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~OllamaLlmProviderTests"`: **15개 통과**. 기존 테스트는 runtime/model 상태 Adapter만 검증하며 chat 추론 테스트는 포함하지 않습니다.
- 모델, Ollama 버전, DB, 설정, 방화벽은 변경하지 않았고 Git commit도 하지 않았습니다.
- 제품 통합 전에, 사용자 승인 범위에서 현재 모델/runtime의 한국어 입력 처리를 별도 격리 테스트로 조사해야 합니다. Ollama 업데이트나 다른 모델 설치는 이번 작업 범위가 아니며 별도 승인이 필요합니다.
