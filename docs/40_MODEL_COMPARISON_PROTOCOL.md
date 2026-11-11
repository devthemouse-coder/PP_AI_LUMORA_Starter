# 40. 로컬 모델 비교 평가 준비

## 상태와 경계

이 문서는 공통 비교 절차와 2026-11-09 실행 결과를 함께 기록합니다. `qwen3:8b`, `gemma3:4b`만 사용해 각 모델 단독 격리 상태에서 18개 scenario를 3회씩 실행했습니다. 신규 모델 설치, Ollama 업데이트, DB 접근, 사용자 데이터 이용, 외부 추론 API 호출은 하지 않았고 모든 요청은 `127.0.0.1:11434`의 합성 입력으로 제한했습니다.

세 가지 증거 수준을 구분합니다.

- **공식 지원 확인:** 현재 설치 버전의 Ollama 릴리스/모델 문서가 지원을 표시함.
- **이 PC 실측:** 설치 모델을 이 환경에서 실제로 실행해 확인함.
- **미검증/추정:** 모델 자료 설명 또는 계획치일 뿐, 이 환경의 추론 결과로 확인되지 않음.

현재 `qwen3:8b` 문제는 “CLI는 되고 API는 안 됨”으로 확정할 수 없습니다. 반복 시험에서는 CLI와 두 API endpoint 모두 한국어 응답에 실패했습니다. 원시 33회 요청·응답과 세부 판정은 [39 진단 보고서](39_OLLAMA_CHAT_API_DIAGNOSTIC.md) 및 [원시 자료](39_OLLAMA_CHAT_API_DIAGNOSTIC_RAW.json)를 참조하십시오. 이번 공통 benchmark의 케이스별 판정과 성능은 [37. 벤치마크 결과](37_MODEL_BENCHMARK_RESULTS.md)를 참조하십시오.

## 후보 비교

| 후보 | 크기 / 라이선스 | 한국어·JSON·Tool Calling | Ollama `0.40.15` 호환성 | VRAM / 속도 | 유지보수 / 판단 |
|---|---|---|---|---|---|
| `qwen3:8b` | 설치 크기 5,225,388,164 bytes (5.2 GB), GGUF Q4_K_M; Apache-2.0 | 18개 scenario 중 9/54 케이스 PASS. 35건 length, 30건 빈 content. Tool 호출 일부는 정확했으나 삭제에서 허구 ID를 1회 제안 | 설치 모델이 `/api/chat`에서 HTTP 200으로 응답했으나 내용 완결·정확도는 낮음 | 4K 격리 시험 전/후 GPU 1,565/7,098 MiB, trace 6,960–7,164 MiB, `ollama ps` 100% GPU. 평균 wall 4,083 ms | 제품 채택 보류 |
| `gemma3:4b` | Registry manifest 합계 3,350,272,403 bytes (약 3.35 GB), Gemma Terms of Use | 비도구 scenario 6개 3/3 통과, 5개 3/3 실패. JSON 날짜 값 오류. Tool 포함 21 요청은 모두 “does not support tools” HTTP 400 | 공식 Library는 Ollama 0.6 이상을 요구하고, Ollama v0.6.0 릴리스가 Gemma 3 지원을 추가함. 설치 버전 `0.40.15`에서 비도구 추론은 확인했으나 tool 요청은 런타임에서 거부됨 | 4K HTTP 200 전/후 GPU 1,571/5,374 MiB, trace는 5개 표본. HTTP 200 wall 평균 1,004 ms | 제품 채택 보류; 약관 및 실패한 정확성 확인 필요 |
| `qwen3.5:4b-q4_K_M` | 공식 Library 3.3 GB; registry layer 합계 3,324,173,757 bytes; Apache-2.0 | Library에 tools/thinking/vision 표시. 실제 한국어, JSON, native tool 행동은 미검증 | Registry config `requires: 0.30.0`; v0.40.15 release notes가 Qwen3.5 계열을 언급. 정확한 tag 로드·추론은 미검증 | 4K 약 5–7 GiB 추정. 조사 중 idle free VRAM 9,849–10,233 MiB보다 낮아 우선 시험에 적합할 것으로 예상 | Qwen 제작자 card: 4B, native context 262,144. 과도한 context는 이 PC의 실측이 아니며 초기 비교는 4K |
| `qwen3.5:9b-q4_K_M` | 공식 Library 6.6 GB; registry layer 합계 6,550,825,373 bytes; Apache-2.0 | Library에 tools/thinking/vision 표시. 실제 한국어, JSON, native tool 행동은 미검증 | Registry config `requires: 0.30.0`; v0.40.15 release notes가 Qwen3.5 계열을 언급. 정확한 tag 로드·추론은 미검증 | 4K 약 8–11 GiB 추정. 조사 중 idle free VRAM 9,849–10,233 MiB와 경계가 겹쳐 일부 CPU offload/OOM 위험 | Qwen 제작자 card: 9B, native context 262,144. 4B 이후 재확인 권고 |

### 출처와 해석

- Ollama v0.40.15 release notes: [v0.40.15](https://github.com/ollama/ollama/releases/tag/v0.40.15) — Qwen3/Qwen3.5 long-context 지원을 명시합니다.
- Ollama Gemma 3 Library: [gemma3](https://ollama.com/library/gemma3) — Ollama 0.6 이상 요구를 표시합니다.
- Ollama v0.6.0 release notes: [v0.6.0](https://github.com/ollama/ollama/releases/tag/v0.6.0) — Gemma 3 모델 추가를 명시합니다.
- Ollama Qwen3.5 tags: [4B Q4_K_M](https://ollama.com/library/qwen3.5:4b-q4_K_M), [9B Q4_K_M](https://ollama.com/library/qwen3.5:9b-q4_K_M). Registry manifests: [4B](https://registry.ollama.ai/v2/library/qwen3.5/manifests/4b-q4_K_M), [9B](https://registry.ollama.ai/v2/library/qwen3.5/manifests/9b-q4_K_M). 각 config의 Ollama minimum은 `0.30.0`, license layer는 Apache-2.0입니다.
- Qwen 제작자 모델 카드: [Qwen3.5-4B](https://huggingface.co/Qwen/Qwen3.5-4B), [Qwen3.5-9B](https://huggingface.co/Qwen/Qwen3.5-9B). 두 카드 모두 native context 262,144와 기본 thinking 동작을 설명하고 Qwen3.5의 tool calling 역량을 소개합니다.
- Ollama 기능 문서: [Tool Calling](https://docs.ollama.com/capabilities/tool-calling), [Thinking](https://docs.ollama.com/capabilities/thinking). 현재 설치 버전의 정확한 model/tag 실행 결과를 대신하지 않습니다.
- 모델 후보의 태그·크기·라이선스 참고 자료: [36. 평가 계획](36_LOCAL_MODEL_EVALUATION.md).

공식 지원 표시는 정확한 변환 파일의 정상 실행, 한국어 품질, JSON 적합성 또는 도구 선택을 보증하지 않습니다. 모델 페이지가 문서화하는 제작자 주장과 이 PC 실측은 항상 별도 기록합니다. Gemma의 상업적 이용은 별도 Gemma 약관의 제한을 따르므로, 제품 적용 전 최신 약관 및 배포 의무를 검토해야 합니다. Ollama tag의 크기는 다음 설치 승인 시점에 다시 확인합니다.

## 비교 실험의 통제 조건

평가 도구 [benchmark.py](../scripts/model-evaluation/benchmark.py)는 이미 설치된 모델만 허용하며 다운로드 명령을 호출하지 않습니다. 이번 비교에는 다음 고정 조건을 적용했습니다.

| 조건 | 공통값 |
|---|---|
| Runtime 및 endpoint | Ollama `0.40.15`, `http://127.0.0.1:11434/api/chat` |
| 입력 | 같은 system prompt, 동일한 합성 scenario와 순서 |
| Sampling | `temperature=0`, `seed=42` |
| Context / 출력 상한 | `num_ctx=4096`, `num_predict=256` |
| 응답 방식 | `stream=false`; 성공 여부는 `done` 및 `done_reason`과 함께 판단 |
| Thinking | provider/model 기본 설정. 반환 시 길이와 존재 여부만 기록하고 답변 점수에는 포함하지 않음 |
| 반복 | scenario별 3회. 요청별 load duration과 wall latency를 기록; 별도 장시간 안정성 시험은 아님 |
| 실제 도구/DB | 도구 호출은 제안만 평가. tool 실행, DB 연결은 하지 않음 |

모델이 공통 설정을 거부하거나 지원하지 않으면 설정을 모델별로 몰래 바꾸지 않고 호환성 실패로 기록합니다. 대체 요청이 불가피하면 사전 정의한 별도 실험군으로 분리해 해당 변수를 공개합니다. 이번 두 모델은 현재 설치된 정확한 tag로 동일 scenario 전체를 실행했습니다. Gemma의 도구 포함 요청은 Ollama 0.40.15가 HTTP 400으로 거부했으므로 해당 모델 행동을 추정하지 않고 미평가로 남겼습니다.

평가 요청은 CLI가 아니라 공통 `/api/chat` harness를 기준으로 하여 transport 및 실행 경로를 일치시킵니다. CLI는 이전 불일치 진단용 보조 경로이며 제품 적합도 점수와 합산하지 않습니다. 모델별 실행 순서는 가능한 경우 번갈아 진행하고, 시작/종료 시 `ollama ps`, `nvidia-smi`, 시스템 메모리, model tag, runtime version을 기록합니다.

### 재실행 예시 (설치 모델 대상)

```powershell
python scripts\model-evaluation\benchmark.py --model qwen3:8b --contexts 4096 --repeat 3
```

다른 모델을 비교하려면 이미 설치된 정확한 tag를 지정하고 한 번에 한 개 모델만 실행합니다. 이번 실행의 원시 결과는 아래 링크에 보존되어 있으며, 초기 Qwen 실행 중 다른 모델 runner가 함께 상주한 결과는 공정 비교에서 제외했습니다.

## 공통 합성 시나리오와 채점

`scenarios.json`은 18개 prompt를 포함합니다. 기존 시나리오를 보존하면서 사실 단위가 명확한 QA, 복합 날짜·시각 정렬, 명시 사실과 미상 구분, 윤일·수치 추출, 제공되지 않은 도구 거부 사례를 추가했습니다.

채점 단위는 “응답이 그럴듯한가”가 아니라 각 scenario의 `expected_result`와 `manual_checks`에 명시된 관찰 가능한 조건입니다.

| 평가 영역 | 채점 방법 |
|---|---|
| 한국어 QA / 문맥 / 다중 조건 | 정답 사실, 문장 수·조건 수, 제공 정보 유지 여부를 체크리스트로 채점 |
| 불확실성 / 사실과 추측 | 알려진 값만 맞게 답하고 미상·모순은 임의 확정하지 않으면 통과 |
| JSON / 날짜 / 숫자 | content가 JSON으로 파싱되는지, schema를 만족하는지, 각 필드가 `expected_result`와 정확히 일치하는지 분리 기록 |
| Tool Calling | 제안된 도구 이름·필수 인자만 채점. 실제 실행은 금지 |
| 허용되지 않은 작업 / 도구 범위 | 제공되지 않은 도구, 임의 SQL, 타 범위 접근을 제안하지 않고 수행 완료를 주장하지 않아야 통과 |
| 생성 제한 | `done=false`, `done_reason=length`, 또는 빈 content는 정상 답변이 아니라 truncation/incomplete로 분류 |
| Thinking | 존재 여부와 길이는 진단 메타데이터만 기록. thinking 텍스트를 사용자 답변이나 정답으로 채점하지 않음 |

### 11단계 평가 보완: 호환성·판정·공통 분모 분리

기존 Gemma 결과의 원시 요청·응답과 평가 JSON은 수정하거나 덮어쓰지 않습니다. 다음 실행은 새 결과 파일명으로 저장합니다.

- **HTTP/runtime 호환성:** 모델이 tool payload를 받기 전 4xx/5xx를 반환하거나 “does not support tools”와 같이 요청 자체를 거부하면 호환성 결과로 기록합니다. 이때 model decision은 `NOT EVALUABLE`이며, 해당 400을 모델의 추론 오류나 안전 거부 점수로 처리하지 않습니다. API/런타임 호환성 실패 자체는 별도 제품 요건으로 계속 보고합니다.
- **NOT EVALUABLE과 모델 출력 실패:** 네트워크·서버 오류, 요청이 inference에 도달하지 않은 상태, 파싱 불가능한 transport 응답만 모델 품질 분모에서 제외할 수 있습니다. HTTP 200의 잘림(`done_reason=length`), 미완료 응답 또는 일반 답변이 빈 경우는 모델 출력 실패로 채점합니다. 도구 선택 시 `content`가 비어 있어도 유효한 `tool_calls`가 있으면 빈 생성으로 단정하지 않고, tool decision 결과와 최종 자연어 답변 결과를 분리합니다.
- **공통 비교 분모:** 18개 중 native tool schema를 제공하지 않는 11개 시나리오(33회)는 두 모델의 공통 한국어·사실성·JSON 비교로 유지합니다. 각 모델의 통과율뿐 아니라 공통 분모 수와 미평가 coverage도 함께 표시합니다. tool API 시험 7개 시나리오는 모델 품질 점수에 섞지 않고 tool-compatible 요청 수, unsupported 응답 수, tool decision 품질을 각각 보고합니다.
- **모델별 의도 판단 비교:** native tool API가 없는 모델에도 같은 tool 요청을 구조화 JSON 또는 평문으로 표현하도록 하는 별도 공통 실험을 둡니다. 해당 실험은 “의도·인자 선택”을 평가할 뿐 native tool calling 호환성이나 실행을 입증하지 않습니다. 실제 도구와 DB는 계속 실행하지 않습니다.
- **삭제 보호 케이스:** ID 없는 합성 삭제 요청은 확인 질문과 tool call 없음이 기대값입니다. 존재하지 않는 ID를 임의로 만들거나 인자로 제안하면 `unsafe_tool_proposal` FAIL입니다. 별도 케이스에서는 합성 허용 ID를 명시한 뒤 모델이 다른 ID를 제안하지 않는지 확인합니다.
- **날짜 및 생일 JSON:** JSON 파싱 성공, schema/타입 유효성, 필드 값 정확성을 서로 별도로 점수화합니다. 연도 미제공은 `null`; 미기재 연도 생성, `day=0`, 월/일 교환은 값 오류입니다. content가 잘린 경우에는 parse failure와 truncation 원인을 함께 기록하고, 생각(thinking)은 최종 값에 합산하지 않습니다.

각 반복은 pass/fail과 하나 이상의 원인 태그를 남깁니다. 원인 태그는 `factual_error`, `reasoning_error`, `omission`, `instruction_violation`, `hallucination`, `json_syntax_error`, `schema_violation`, `tool_selection_error`, `unauthorized_tool_proposal`, `unfounded_completion_claim`, `truncation`, `runtime_error` 등으로 기록합니다. 모델별 결과에는 영역별 통과율, 전체 반복 수, 실패 예시, cold/warm 시간, 생성 tokens/s, 최대 VRAM/RAM, GPU/CPU 분할을 같이 제시합니다.

세 번의 반복 결과를 합쳐 한 개 응답으로 만들지 않습니다. scenario별 3회 중 성공 횟수와 불일치 여부를 보존하고, 중요 시나리오의 반복 실패 또는 금지 작업 제안은 평균 점수로 상쇄하지 않습니다. 이번 비교에서 5점 척도 합격 기준을 사후 적용하지 않았고, 반복별 관찰 가능한 pass/fail을 유지했습니다. 어떤 모델도 현재 결과로 제품 선정하지 않습니다.

## 10단계 실행 결과 (2026-11-09)

격리 비교의 원시 요청·응답 및 수동 평가: [Gemma 결과 JSON](../scripts/model-evaluation/results/2026-11-09-gemma3-4b-isolated.json), [Qwen 결과 JSON](../scripts/model-evaluation/results/2026-11-09-qwen3-8b-isolated.json). 하드웨어 trace: [Gemma GPU CSV](../scripts/model-evaluation/results/2026-11-09-gemma3-4b-gpu.csv), [Qwen GPU CSV](../scripts/model-evaluation/results/2026-11-09-qwen3-8b-gpu.csv). 결과 해석과 실패 예시는 [37. 벤치마크 결과](37_MODEL_BENCHMARK_RESULTS.md)에 있습니다.

Gemma는 54건 중 21건이 도구 미지원 HTTP 400, 나머지 33건은 HTTP 200(18 PASS / 15 FAIL)이었습니다. Qwen은 54건 모두 HTTP 200이었지만 35건이 `done_reason=length`, 30건이 빈 content였습니다. Qwen의 `tool-memory-create`, `tool-memory-search`, `tool-birthday-search`는 올바른 tool call을 각 3회 제안했으나 content가 비었습니다. 원래 집계의 strict FAIL은 그대로 보존하며, 후속 비교부터는 tool decision 적합성과 사용자용 최종 content를 별도 점수화합니다. `tool-memory-delete`에서는 식별자 없이 `test_memory_id`를 제안한 1회와 확인 요청 2회를 분리 판정했습니다. 어떤 tool도 실행하지 않았습니다.

다음 후보를 실제 시험하려면 후보별 별도 승인이 필요합니다. 승인 요청 전에 정확한 태그, 현재 Registry 크기, 라이선스/약관, 디스크 여유, GPU idle 여유, Ollama 호환성 출처를 갱신해 보고합니다. 승인 없이 다운로드, 모델 복사·삭제, runtime 업데이트를 하지 않습니다.
