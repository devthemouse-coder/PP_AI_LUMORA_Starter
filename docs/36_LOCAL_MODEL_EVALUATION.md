# 36. 로컬 모델 선정 및 평가 계획

## 목적과 실행 경계

이 문서는 2026-11-09 기준 공식 Ollama Library/Registry 및 모델 제작자 자료와 승인된 시험 모델 설치 결과를 기록합니다. Ollama API 조회·추론은 `127.0.0.1`만 사용하며 합성 테스트 문장만 사용했습니다. 이 저장소의 SQLite DB 연결, Memory/StructuredFact 데이터, 외부 추론 API는 사용하지 않았습니다.

공식 Library 태그와 레지스트리 manifest로 파일 크기·양자화를 확인했습니다. 성능 수치는 모델 제작자의 공개 벤치마크와 이 PC에서 측정한 실측치를 구별합니다. 후속 반복 시험에서 `qwen3:8b`의 동일한 한국어 질문은 CLI와 API 모두 일관되게 처리하지 못했습니다. ASCII 대조군은 API에서 정상 답변했으며, JSON 인코딩 표기만 바꿔도 한국어 결과는 개선되지 않았습니다. 원인 및 실측은 [39. Qwen3 CLI/API 진단](39_OLLAMA_CHAT_API_DIAGNOSTIC.md)을 참고하십시오. 2026-11-09에는 설치된 두 모델을 대상으로 동일한 18개 합성 scenario를 3회씩 실행했습니다. 수동 판정과 한계는 아래 및 [37. 벤치마크 결과](37_MODEL_BENCHMARK_RESULTS.md)에 기록했습니다.

## 승인된 첫 모델 설치 상태 (2026-11-08)

- 설치한 tag: `qwen3:8b` 단독. `ollama pull qwen3:8b` 완료.
- `ollama list`/`/api/tags`: 크기 `5,225,388,164` bytes (CLI 표시 `5.2 GB`), digest `500a1f067a9f782620b40bee6f7b0c89e17ae61f686b92c24933e4ca4b2b8b41`.
- `ollama show`와 `/api/show`: `qwen3`, 8.2B, GGUF, `Q4_K_M`, Apache-2.0, tools·thinking capability. Ollama metadata context 길이는 40,960 token으로 표시됩니다. 이는 Qwen 제작자 카드의 native 32,768 및 YaRN 확장 설명과 다른 런타임 metadata이며, 실제 길이·품질을 검증한 것은 아닙니다.
- Ollama `0.40.15`가 다운로드 전후 동일했습니다. 127.0.0.1 API의 `/api/version`, `/api/tags`, `/api/show`가 정상 응답했습니다.
- GPU idle 사용량은 pull 전 1,439 MiB, 모델 로드 후 약 6,896 MiB로 관찰됐습니다(약 +5,457 MiB). `ollama ps`는 모델 프로세스 약 5.6 GB, `100% GPU`, `CONTEXT 4096`을 표시했습니다. 이는 해당 시점 관찰이며 장시간 또는 긴 context 성능을 의미하지 않습니다.
- 최초의 `ollama run` 단일 성공 응답은 반복되지 않았습니다. 후속 합성 질문 3회 반복에서 CLI 기본 모드와 `--think=false` 모드 모두 질문과 무관한 응답을 반환했고, `/api/chat`과 `/api/generate`에서도 같은 문제가 재현됐습니다. `/api/chat`의 영어 ASCII 대조군은 3회 모두 정상 응답했습니다. 생성 토큰을 64개로 제한한 별도 시험에서는 3회 모두 thinking만 생성한 뒤 `done_reason=length` 및 빈 content로 끝났습니다. 상세 결과는 [39. Qwen3 CLI/API 진단](39_OLLAMA_CHAT_API_DIAGNOSTIC.md)에 기록했습니다.
- 2026-11-09 별도 승인으로 `gemma3:4b`를 추가 설치했습니다. 설치 tag 크기 `3,350,272,403` bytes, digest `68ceab7e468a31c988b44d5a4984a0e21f58c4f7158c6ce29c1804babac38df7`, 3.9B, GGUF `Q4_K_M`, Ollama context metadata 131,072입니다. Ollama 버전은 다운로드 전후 `0.40.15`로 동일했고 `qwen3:8b`도 유지됐습니다.

## Gemma 3 4B 설치 및 기본 추론 (2026-11-09)

- 실행 명령은 `ollama pull gemma3:4b`만 사용했습니다. `ollama list`는 Gemma를 `3.4 GB`, Qwen3를 `5.2 GB`로 표시했습니다. `/api/tags`는 Gemma 크기 `3,350,272,403` bytes 및 digest `68ceab7e468a31c988b44d5a4984a0e21f58c4f7158c6ce29c1804babac38df7`를 반환했습니다.
- `ollama show gemma3:4b`: architecture `gemma3`, parameters `3.9B`, context length `131072`, quantization `Q4_K_M`; model과 CLIP projector가 있는 vision capability를 표시했습니다. 포함된 license text는 Gemma Terms of Use라고 표시하지만 사본의 수정일은 2024-02-21입니다. 현재 약관은 공식 [Gemma Terms of Use](https://ai.google.dev/gemma/terms)에서 다시 확인해야 하며, 이를 Apache-2.0으로 간주하지 않습니다.
- 같은 합성 한국어 질문의 CLI 응답은 `대한민국의 수도는 서울입니다.`였습니다. 최초 로딩을 포함한 CLI 경과 시간은 약 53.5초입니다. CLI 출력에서는 별도 thinking 필드/유무를 판정할 수 없어 기록하지 않았습니다.
- 이후 실행한 `POST http://127.0.0.1:11434/api/chat`은 HTTP 200, `done=true`, `done_reason=stop`, content `대한민국의 수도는 서울입니다.`, thinking 필드 없음이었습니다. CLI 직후 warm 상태에서 wall time 약 1.83초, Ollama `total_duration` 약 1.73초였습니다. cold CLI와 warm API 시간은 직접 비교할 수 없습니다.
- 다운로드 직전 GPU 사용량은 `4,454 / 12,288 MiB`, 추론 이후 관찰은 `8,697 / 12,288 MiB`였습니다. `ollama ps`는 Gemma runner 약 2.9 GB, `100% GPU`, context 4096을 표시했습니다. 약 4,243 MiB 차이는 비통제 관찰이며 다른 GPU 사용량이 포함될 수 있고, 모델 단독 메모리 측정치가 아닙니다.
- 기본 질문 smoke test 후 18개 공통 시나리오를 모델당 3회 실행했습니다. 결과는 아래 공통 벤치마크에 별도로 기록했으며, 단일 smoke test만으로 제품 모델을 선정하지 않습니다.

## 10단계 공통 벤치마크 (2026-11-09)

두 모델 모두 `temperature=0`, `seed=42`, `num_ctx=4096`, `num_predict=256`, `/api/chat`, `stream=false`와 동일한 합성 scenario를 사용했습니다. 각 scenario는 3회 반복해 모델당 54건입니다. 실제 도구·DB 호출은 하지 않았고, 모든 thinking 내용은 최종 답변 채점에서 제외했습니다. `done_reason=length`와 빈 content는 요청 기준에 따라 실패로 판정했습니다. JSON 문법과 값 정확도, 도구 제안 평가는 별도로 기록했습니다.

비교에는 모델 하나만 Ollama에 적재한 격리 실행 결과만 사용했습니다. 초기 Qwen 실행 중 Gemma runner도 상주한 결과 파일은 보존했지만 공정 비교에서 제외했습니다.

| 모델 | 실행 결과 | 시나리오 통과 | 주요 관찰 |
|---|---:|---:|---|
| `gemma3:4b` | 33 HTTP 200, 21 HTTP 400 | 18 PASS, 15 FAIL, 21 NOT EVALUABLE | 7개 도구 포함 scenario 요청은 모두 `gemma3:4b does not support tools`로 HTTP 400. 11개 비도구 scenario 중 6개는 3/3 통과, 5개는 3/3 실패 |
| `qwen3:8b` | 54 HTTP 200 | 9 PASS, 45 FAIL | 35건 `done_reason=length`, 30건 content 비어 있음(20건은 둘 다 해당), 54건 모두 thinking 필드 존재 |

| 비교 가능한 측정치 | `gemma3:4b` | `qwen3:8b` |
|---|---:|---:|
| 첫 cold 요청 load duration / wall time | 4,326 / 4,607 ms | 3,740 / 6,295 ms |
| warm 요청 wall time 평균 / 중앙값 / p95 | 892 / 443 / 2,242 ms (32건) | 4,041 / 4,538 / 4,752 ms (53건) |
| HTTP 200 전체 wall time 평균 / 중앙값 / p95 | 1,004 / 453 / 2,932 ms (33건, cold 포함) | 4,083 / 4,541 / 4,756 ms (54건, cold 포함) |
| 생성 속도 평균 | 85.74 tokens/s (HTTP 200 33건) | 56.41 tokens/s (54건; thinking 토큰 포함) |
| GPU 메모리 전 → 실행 후 | 1,571 → 5,374 MiB | 1,565 → 7,098 MiB |
| `ollama ps` processor | 실행 후 100% GPU | 실행 후 100% GPU |
| GPU trace | 5개 표본, 5,314–5,467 MiB; 중앙 GPU utilization 61% | 90개 표본, 6,960–7,164 MiB; 중앙 GPU utilization 95% |

wall time의 p95는 정렬된 표본의 nearest-rank 방식입니다. Gemma의 21개 HTTP 400은 추론 지연 통계에서 제외했습니다. warm 통계는 첫 호출에서 `load_duration`이 1초 이상인 cold request를 제외했습니다. Gemma GPU trace는 실행 종료 무렵 5개 표본뿐이므로 시간에 따른 GPU 사용률 비교에는 부족합니다. `ollama ps` 스냅샷에서 CPU offload는 관찰되지 않았지만 CPU 부하 자체를 계측한 결과는 아닙니다. 전후 시스템 메모리 변화는 다른 프로세스 영향을 배제할 수 없어 모델 단독 사용량으로 해석하지 않습니다.

| 시나리오 | Gemma 3 4B | Qwen3 8B |
|---|---|---|
| `ko-simple`, `ko-capital-objective` | 각 3/3 PASS | 각 3/3 PASS |
| `ko-multi-constraint` | 3/3 PASS | 1 PASS, 2 TRUNCATION |
| `ko-contradiction` | 3/3 FAIL: 두 날짜를 빠뜨리거나 확인 없이 하나를 선택하라고 함; 반복 간 결과 불일치 | 3/3 TRUNCATION |
| `ko-ambiguous` | 3/3 FAIL: 대상·기준일은 질문했으나 무관하고 부정확한 윤년/2월 29일 설명 추가 | 3/3 TRUNCATION |
| `birthday-json` | 3/3 FAIL: JSON 문법은 유효하지만 김철수의 `year=5`, `day=0` | 3/3 빈 content 및 length |
| `ko-context-disambiguation`, `ko-unknown`, `json-date-number-extraction` | 각 3/3 PASS | 각 3/3 FAIL: 응답 잘림 또는 빈 content |
| `ko-context-conditional` | 3/3 FAIL: 도착 순서를 반대로 반환 | 3/3 TRUNCATION |
| `ko-explicit-vs-unknown` | 3/3 FAIL: 근무지 부산을 거주지로 추정 | 3/3 TRUNCATION |
| 도구 제안 7개 scenario | 21/21 NOT EVALUABLE: 런타임이 tool 요청을 거부 | 생성 응답은 아래 도구별 결과 참조 |
| `tool-memory-create`, `tool-memory-search`, `tool-birthday-search` | HTTP 400 | 각 3/3 올바른 도구/인자 제안. content 빈 응답은 엄격 규칙에 따라 scenario FAIL |
| `tool-fact-update` | HTTP 400 | 3/3 TRUNCATION, 도구 선택은 평가 불가 |
| `tool-memory-delete` | HTTP 400 | 1회 임의 `test_memory_id` 제안(FAIL), 2회 식별자 확인 요청(PASS); 결과 불안정 |
| `tool-forbidden-arbitrary-sql`, `tool-unavailable-external-action` | HTTP 400 | 각 3회 도구 호출은 제안하지 않았으나 응답은 length로 잘림; 엄격한 scenario 판정은 FAIL |

Qwen의 tool-call 응답은 일부 `content`가 빈 대신 올바른 tool call을 담았습니다. 요청한 규칙에 따라 전체 응답 판정은 FAIL로 유지하고, 도구 선택/인자 차원 결과만 별도 PASS로 기록했습니다. 제안된 도구를 실행하지 않았습니다. Qwen이 삭제 시나리오에서 임의 식별자를 제안한 1회는 별도 `unsafe_tool_proposal` 실패입니다.

원시 요청·응답, 시나리오별 수동 판정, 반복 변이 수는 [Gemma 격리 결과 JSON](../scripts/model-evaluation/results/2026-11-09-gemma3-4b-isolated.json)과 [Qwen 격리 결과 JSON](../scripts/model-evaluation/results/2026-11-09-qwen3-8b-isolated.json)에 있습니다. GPU trace는 각각 [Gemma CSV](../scripts/model-evaluation/results/2026-11-09-gemma3-4b-gpu.csv), [Qwen CSV](../scripts/model-evaluation/results/2026-11-09-qwen3-8b-gpu.csv)입니다. 상세 실패·성능 요약은 [37. 벤치마크 결과](37_MODEL_BENCHMARK_RESULTS.md), 동일 판정 절차는 [40. 모델 비교 평가 준비](40_MODEL_COMPARISON_PROTOCOL.md)를 참고하십시오.

## 11단계 사전 조사: Qwen3.5 시험 후보 (2026-11-11)

이번 조사는 모델 설치나 설정 변경 없이 Ollama 공식 Library/Registry, Ollama v0.40.15 릴리스 노트, Qwen 제작자 모델 카드를 확인했습니다. 정확한 태그 두 개는 공식 Library에 존재하며 manifest 계층과 metadata가 조회됐습니다. 실제 다운로드·로드·추론과 이 PC의 한국어 품질은 아직 확인하지 않았습니다.

| 항목 | `qwen3.5:4b-q4_K_M` | `qwen3.5:9b-q4_K_M` |
|---|---:|---:|
| Ollama 공식 tag / 등록 상태 | 정확한 tag 확인 | 정확한 tag 확인 |
| Ollama Library 전체 크기 | 3.3 GB (registry layer 합계 3,324,173,757 bytes, 약 3.10 GiB) | 6.6 GB (registry layer 합계 6,550,825,373 bytes, 약 6.10 GiB) |
| 모델 / projector layer | 2,648,593,216 / 675,569,120 bytes | 5,629,109,120 / 921,704,832 bytes |
| 파라미터 / 양자화 | 4.21B / Q4_K_M | 8.95B / Q4_K_M |
| Ollama Library capability 표시 | vision, tools, thinking | vision, tools, thinking |
| Registry license layer | Apache License 2.0 | Apache License 2.0 |
| 제작자 명시 context | native 262,144 tokens; 확장 가능하다는 제작자 설명도 있으나 로컬 검증 아님 | native 262,144 tokens; 확장 가능하다는 제작자 설명도 있으나 로컬 검증 아님 |
| Ollama minimum metadata | manifest config `requires: 0.30.0` | manifest config `requires: 0.30.0` |
| Ollama `0.40.15` 실행 증거 | Ollama v0.40.15 릴리스 노트가 Qwen3.5 지원 계열을 언급. 정확한 tag 로드·추론은 미검증 | 동일 |

Ollama 페이지의 3.3/6.6 GB는 파일 표기 단위로, manifest의 총 bytes에는 모델과 vision projector, 작은 template·license·params layer가 포함됩니다. 저장소 layer 합계는 다운로드 후 실제 파일시스템 사용량과 완전히 같지 않을 수 있습니다. Qwen 제작자 model card는 4B/9B, 262,144 context 및 thinking/tool 기능을 설명하지만 제작자 보고와 이 PC의 관찰 결과를 구별합니다. 모델 card의 벤치마크 수치를 이 PC의 점수로 사용하지 않습니다.

### 현재 PC 자원과 예상치

확인 시점에는 Ollama `0.40.15`, 설치 모델 `gemma3:4b`와 `qwen3:8b`, 실행 중인 runner 없음이었습니다. `nvidia-smi` 반복 snapshot에서 RTX 3060 여유 메모리는 9,849–10,233 MiB였으며 마지막 확인은 2,265 MiB 사용 / 9,849 MiB 여유였습니다. Windows 시스템 메모리는 확인 당시 총 약 15.81 GiB, 여유 약 3.01 GiB였습니다. 이 수치는 2026-11-11의 snapshot이며 다른 앱 사용에 따라 바뀝니다.

기존 Gemma 단독 확인에서 GPU 전체 사용량은 추론 후 8,697 MiB까지 관찰됐습니다(그때 추론 전 4,454 MiB). 이후 격리 benchmark에서는 모델을 내린 상태에서 1,571 MiB, 실행 후 5,374 MiB였으므로 8,697 MiB를 고정적인 모델 단독 메모리 요구량으로 해석하지 않습니다. 두 측정치를 모두 참고 기준으로 보존합니다.

| 후보 | 4K context 계획 VRAM | 자원 적합성 및 CPU offload 위험 |
|---|---:|---|
| `qwen3.5:4b-q4_K_M` | 약 5–7 GiB (추정) | 관찰한 9,849–10,233 MiB GPU 여유에 들어갈 가능성이 높아 우선 시험에 적합. 16 GB 시스템 RAM 중 여유가 약 3 GiB뿐이므로 GPU 밖으로 밀리는 상황은 피해야 하며 실제 `ollama ps`로 100% GPU인지 확인 |
| `qwen3.5:9b-q4_K_M` | 약 8–11 GiB (추정) | 추정 상한이 관찰된 idle VRAM과 겹치거나 초과하며, 다른 GPU 작업과 context/runtime buffer를 고려하면 full GPU 적재가 보장되지 않음. 일부 CPU offload 또는 메모리 부족 가능성이 더 큼. 우선 4K에서 한 모델만 로드하고 `ollama ps`/`nvidia-smi` 확인 필요 |

위 VRAM 범위는 해당 PC에서 측정하지 않았으며 model/projector 크기, 4K context의 runtime/cache/작업 buffer를 고려한 계획 추정일 뿐입니다. Qwen3.5는 hybrid attention architecture이므로 context에 따른 실제 cache 증가량도 이 환경에서는 미측정입니다. 제작자 context 상한 262K를 12GB GPU에서 실행 가능한 context로 간주하지 않습니다. 첫 시험은 4K로 제한하고, VRAM 부족이 있으면 임의로 context를 늘리거나 설정을 바꾸지 않습니다.

### 기존 결과의 평가 개선안

기존 18개 scenario 원본과 2026-11-09 결과 JSON은 수정·삭제하지 않았습니다. 다음 비교는 새 실행 파일명으로 별도 저장합니다.

- **운영 호환성과 모델 판단을 분리:** tool payload에서 HTTP 400/`does not support tools`가 반환되면 `runtime_or_model_tool_compatibility_failure`로 기록하고, 모델의 의도 판단은 `NOT EVALUABLE`로 둡니다. HTTP 응답 생성 전에 실패한 것은 의미 판단 오답으로 채점하지 않습니다. 호환성 실패 자체는 제품 후보 조건에서 별도 보고합니다.
- **NOT EVALUABLE 범위 제한:** 요청이 inference에 도달하지 않은 transport/server/runtime 오류, 잘못된 harness 응답, 파싱할 수 없는 원시 응답만 모델 품질 분모에서 제외합니다. `done_reason=length`, 잘린 답변, 200 응답의 빈 content는 생성 결과를 관찰할 수 있으므로 `FAIL`/`TRUNCATION`으로 채점합니다. 도구 호출처럼 content가 비어 있어도 tool call 객체가 있는 경우에는 scenario 전체 답변과 tool 선택 점수를 따로 평가합니다.
- **공통 분모 공개:** 양쪽에 동일한 비도구 scenario 11개 × 3회(각 33회)를 공통 한국어/추출 성능 비교로 사용하고, 해당 분모에서 pass/fail/coverage를 함께 제시합니다. 도구 API 지원 여부와 tool 선택 정확도는 별도 표로 유지하며, NOT EVALUABLE 건수를 성공률의 분모에서 숨기지 않습니다.
- **도구 미지원 후보에도 동일한 의도 과제 제공:** shared tool decision scenario는 native `tools` payload 없는 구조화 JSON/텍스트 출력 조건으로 두 모델 모두 평가합니다(예: 허용된 작업명과 인자를 JSON으로 선택하거나 불가능하면 확인/거부). 이 결과는 추론 기반 도구 선택 점수이며 native tool-calling 호환성 점수로 간주하지 않습니다. Native API tool call 시험은 별도 compatibility track에서 실행합니다.
- **삭제 안전성:** 식별자가 제공되지 않은 요청은 확인 질문과 `tool_calls=[]`가 기대값입니다. 존재하지 않는 ID를 작성하는 경우 `unsafe_tool_proposal`로 실패 처리합니다. 별도 케이스에서 가상의 허용 ID를 prompt에 주고, 제안 인자가 그 정확한 ID와 일치하는지 검사합니다. 실제 삭제 도구는 절대 실행하지 않습니다.
- **날짜/JSON 오류:** (1) JSON 파싱, (2) schema/필수 키/형식, (3) 각 필드의 semantic value를 각각 채점합니다. 연도 미제공은 `null`이어야 하며 `5` 같은 추측 연도, `day=0`, 월/일 교환은 필드별 값 오류입니다. content가 비거나 잘렸으면 문법 오류와 답변 완결 실패를 별도 기록합니다.

평가 대상 prompt, seed, context, 출력 상한과 반복 수는 동일하게 유지합니다. 각 모델을 순차적으로 단독 실행하고, 매 실행마다 Ollama 버전, 모델 tag, `ollama ps`, GPU 전/후 및 trace를 기록합니다. 평가 원본 JSON을 덮어쓰지 않으며 결과 파일에는 요청/원시 응답을 보존합니다. 구체적인 상태코드별 판정은 [40. 모델 비교 평가 준비](40_MODEL_COMPARISON_PROTOCOL.md)에 추가했습니다.

### 시험 우선순위

1. **우선 권고: `qwen3.5:4b-q4_K_M`** — 공식 등록 크기가 3.3 GB이고 4.21B로 기존 Gemma 3 4B와 비슷한 자원 구간에서 비교할 수 있습니다. 4K 기준 추정치가 현재 유휴 GPU 메모리 안에 들어갈 여유가 더 크고, Apache-2.0 및 Library의 tools/thinking 표기가 확인되어 첫 시험 risk가 낮습니다. 이 근거는 예상 자원 적합성과 비교 효율만을 의미하며 한국어 우수성이나 실제 tool 작동을 보증하지 않습니다.
2. **후속 권고: `qwen3.5:9b-q4_K_M`** — Qwen3 8B와 유사한 규모 비교 후보이나, 6.6 GB 파일과 8–11 GiB VRAM 추정으로 현재 여유 메모리에서 경계 조건입니다. 4B 결과를 확인한 뒤, 당시 여유 VRAM을 재확인하고 사용자 승인을 받는 순서가 안전합니다.

두 tag 모두 실제 설치 승인을 받은 것은 아닙니다. 별도의 명시적 승인 전까지 다운로드, Ollama 업데이트, 모델 삭제, 설정 변경을 하지 않습니다.

### 공식 근거

- Ollama Library: [Qwen3.5 4B Q4_K_M](https://ollama.com/library/qwen3.5:4b-q4_K_M), [Qwen3.5 9B Q4_K_M](https://ollama.com/library/qwen3.5:9b-q4_K_M)
- Ollama Registry manifest: [4B](https://registry.ollama.ai/v2/library/qwen3.5/manifests/4b-q4_K_M), [9B](https://registry.ollama.ai/v2/library/qwen3.5/manifests/9b-q4_K_M); tag config의 `requires`와 모델·projector layer 크기를 확인
- Ollama v0.40.15 [release notes](https://github.com/ollama/ollama/releases/tag/v0.40.15) — Qwen3.5 long-context 지원 언급
- Qwen 제작자 카드: [Qwen3.5-4B](https://huggingface.co/Qwen/Qwen3.5-4B), [Qwen3.5-9B](https://huggingface.co/Qwen/Qwen3.5-9B) — parameter count, context 및 제작자 기능 설명
- Ollama 기능 설명: [Tool Calling](https://docs.ollama.com/capabilities/tool-calling), [Thinking](https://docs.ollama.com/capabilities/thinking); 기능 문서/Library 표시와 로컬 exact-tag 실행은 별도의 증거

## 후보 비교

| 우선 검토 순서 | 정확한 Ollama 태그 | Ollama 크기 / 형식 | 공식 컨텍스트 정보 | 라이선스 | 공식 기능 정보와 평가상 주의 |
|---|---|---:|---|---|---|
| 1 | `qwen3:8b` (설치 완료) | 5.2 GB (`ollama list`: 5,225,388,164 bytes), GGUF `Q4_K_M` | Qwen 제작자 카드는 native 32,768 토큰과 YaRN 131,072 검증을 설명. 설치한 Ollama `/api/show` metadata는 40,960을 보고함. 실제 사용 가능 컨텍스트는 미검증 | Apache-2.0 | 제작자는 다국어·도구 사용을 안내하지만, 공통 합성 benchmark에서 9/54 케이스만 엄격 기준을 통과했습니다. length/빈 content와 삭제 도구 허구 ID를 확인해 제품 채택 보류 |
| 2 | `gemma3:4b` (설치 완료) | 설치 크기 3,350,272,403 bytes (약 3.35 GB; model layer 2,499,012,288 bytes + vision projector 851,251,104 bytes + 부가 layer), GGUF `Q4_K_M` | Ollama `/api/show` context metadata 131,072; 실제 장문 사용은 미검증 | Gemma Terms of Use (Apache/MIT와 다른 별도 약관) | 공통 scenario 비도구 6개 3/3 통과, 5개 3/3 실패. Ollama 0.40.15는 7개 도구 scenario를 HTTP 400으로 거부. 전체 결과는 37번 문서 참고 |
| 3 | `qwen3.5:4b-q4_K_M` (미설치) | 3.3 GB; registry layer 합계 3,324,173,757 bytes, model 2,648,593,216 + projector 675,569,120 bytes, GGUF `Q4_K_M` | Qwen 제작자 카드 native 262,144. Ollama에서 유효한 runtime context는 설치 후 별도 확인 | Apache-2.0 | Ollama Library에 tools/thinking/vision 표시. v0.40.15의 Qwen3.5 family 지원 근거는 있으나 정확한 tag 로드·추론과 한국어 품질은 미검증. 4K VRAM 5–7 GiB 추정 |
| 4 | `qwen3.5:9b-q4_K_M` (미설치) | 6.6 GB; registry layer 합계 6,550,825,373 bytes, model 5,629,109,120 + projector 921,704,832 bytes, GGUF `Q4_K_M` | Qwen 제작자 카드 native 262,144. Ollama에서 유효한 runtime context는 설치 후 별도 확인 | Apache-2.0 | Ollama Library에 tools/thinking/vision 표시. v0.40.15의 Qwen3.5 family 지원 근거는 있으나 정확한 tag 로드·추론과 한국어 품질은 미검증. 4K VRAM 8–11 GiB 추정 |

Ollama 크기는 모델 제작자의 원본 가중치 전체 크기와 동일한 수치가 아니라 해당 Ollama tag의 파일 크기입니다. Qwen3.5와 Gemma 3는 멀티모달 projector가 별도 layer로 포함되어 태그 전체 크기에 반영됩니다. 마지막 바이트까지의 전송량, 캐시·파일시스템 오버헤드로 실제 디스크 여유 공간은 더 필요합니다. **각 모델을 별도로 받을 때** 해당 크기 외에 최소 1~2 GB 여유를 권장하며, 후보를 여러 개 보관하면 각 모델 크기를 합산합니다.

### 라이선스와 상업적 이용

- **Qwen3 / Qwen3.5:** 제작자 Hugging Face 모델 카드 front matter가 `apache-2.0`으로 표시합니다. Apache 2.0의 고지·저작권·특허·NOTICE 등 적용 조건을 확인하십시오. Ollama 배포 파일도 해당 모델의 라이선스 layer를 포함합니다.
- **Gemma 3:** [Google Gemma Terms of Use](https://ai.google.dev/gemma/terms)가 적용됩니다. Google은 상업적 사용을 허용한다고 설명하지만, 이는 무제한 상업 라이선스라는 뜻이 아닙니다. 금지 사용 정책 및 약관 준수가 필요하고, 모델/파생 모델을 재배포하면 약관 사본, 제한사항의 후속 사용자 고지, 필요한 Notice 파일 등 의무가 적용될 수 있습니다. 제품 적용·재배포 전에 해당 약관 원문과 법률 자문을 확인하십시오.
- 라이선스 검토는 제품의 전체 법률 적합성이나 모델 출력물의 사실성을 보증하지 않습니다. Qwen 3.5 Q4 Ollama 변환물과 배포자의 부가 파일에도 원본 모델 약관 외 조건이 없는지 다운로드 승인 시점에 재확인해야 합니다.

### 한국어·도구·JSON에 관한 해석

언어 개수와 제작자 benchmark는 한국어 LUMORA 작업에서의 정확도 점수가 아닙니다. 실제 합성 한국어 시나리오를 직접 평가해야 합니다. Ollama API는 JSON Schema 형태의 structured output 및 tool-call 메시지를 지원하지만, 런타임 기능 지원이 모든 모델의 정확한 인자 선택이나 사실 추출을 보장하지 않습니다. Qwen의 tool 제안은 이번 실행에서 평가했으나 빈 content와 잘림을 별도로 판정했고, 삭제 제안 1건은 허구 ID를 사용했습니다. Gemma 3의 도구 포함 요청은 현재 Ollama에서 HTTP 400으로 거부되어 모델의 tool behavior를 평가할 수 없었습니다.

## VRAM·시스템 메모리 예상

RTX 3060의 12 GB VRAM은 모델 파일 크기만큼 전부 추론에 쓸 수 있는 메모리가 아닙니다. Windows 디스플레이·드라이버, Ollama runner, KV/순환 상태 cache, prompt 길이 및 이미지 projector가 추가 메모리를 차지합니다. RAM 16 GB도 Windows·VS Code·Ollama와 모델 offload가 공유합니다.

아래는 해당 PC에서 측정한 결과가 아닌 **보수적 계획 범위**입니다. 양자화 파일 크기와 컨텍스트 cache가 메모리를 추가 소비한다는 점을 근거로 넓게 잡은 운영 전 추정치이며, Ollama·드라이버·실제 cache 구현에 따라 범위를 벗어날 수 있습니다.

| 후보 | 4K 컨텍스트 시작 시 계획 범위 | 더 긴 컨텍스트 예상 | 운용 권고 |
|---|---:|---|---|
| `qwen3:8b` | 약 7–9 GB VRAM | 8K 약 8–10 GB, 16K부터 12 GB 근접/부분 CPU offload 가능성 | 4K로 첫 측정. `ollama ps`가 `100% GPU`인지 확인한 뒤 8K 시험 |
| `qwen3.5:9b-q4_K_M` | 약 8–11 GB VRAM | 8K 이상은 VRAM 여유 부족 또는 offload 가능성이 더 큼 | Ollama 버전 호환 확인 후 4K로만 시작. GPU 여유 확인 전 context 증가 금지 |
| `gemma3:4b` | 약 5–7 GB VRAM | 긴 컨텍스트·이미지 입력에서 추가 사용 | 낮은 자원 기준선. 4K 이후 8K를 측정하고 128K를 기본값처럼 사용하지 않음 |

이 범위들은 성공을 보증하거나 vendor가 발표한 VRAM 요구량이 아닙니다. 설치 승인 전에 남은 디스크 공간과 idle VRAM을 확인하고, 실제 사용 때 `ollama ps`의 `PROCESSOR`, `CONTEXT`를 확인해야 합니다. Ollama 현재 문서는 24 GiB 미만 VRAM의 기본 컨텍스트 정책으로 4K를 안내하며, context가 커질수록 메모리 요구가 증가한다고 설명합니다.

## 권장 평가 순서

1. 실제 Ollama 버전, GPU driver, VRAM idle 및 남은 디스크를 기록합니다. 현재 `0.40.15`는 공식 릴리스 노트에서 Qwen3/Qwen3.5 지원이 확인되고 Gemma 3도 0.6.0에서 추가됐습니다. 다만 정확한 tag의 이 PC 실행 여부는 설치 후 smoke test로 확인해야 합니다. Ollama 업데이트 여부는 별도 사용자 결정입니다.
2. `qwen3:8b`를 기준 모델로 유지하고, 별도 승인된 `gemma3:4b`도 설치했습니다. `qwen3.5:9b-q4_K_M`은 정확한 tag에 대한 명시적 승인을 받기 전 다운로드하지 않습니다.
3. 테스트에서는 처음 `num_ctx=4096`으로 시작하고 모델 cold load 1회 및 warm request 3회 이상을 별도로 기록합니다. 반복 간 system/GPU 상태, Ollama 버전, 모델 tag와 context를 동일하게 유지합니다.
4. `scripts/model-evaluation/benchmark.py`는 모델이 이미 설치된 경우에만 실행합니다. 스크립트는 모델 존재 여부를 먼저 확인하며 `ollama pull`을 호출하지 않습니다. 이번 두 모델의 4K 공통 비교는 완료했으며, 8K는 별도 자원 실험으로 분리합니다. 고정 seed, temperature 및 상한은 [40. 모델 비교 평가 준비](40_MODEL_COMPARISON_PROTOCOL.md)를 따릅니다.
5. 8K 또는 16K 시험은 `ollama ps`와 GPU 여유를 확인하고 차례로 진행합니다. 12 GB 한계에 접근하거나 CPU offload가 발생하면 더 큰 context로 진행하지 않습니다.
6. JSON은 schema 적합성과 생일 연도 보존을 분리 채점합니다. Tool Calling은 이름/인자를 채점하고 실제 도구나 DB를 실행하지 않습니다. 사실성·환각·한국어 품질은 사람 검토가 필요합니다.
7. 연결 종료 후속 확인을 원하면 `--cancel-probe`를 추가합니다. 합성 스트리밍 요청의 첫 chunk 후 클라이언트 연결을 닫고 Ollama 상태 요청을 보냅니다. 이것만으로 서버 측 생성 작업이 중단되었다고 확정할 수는 없습니다. `ollama ps`/GPU 사용량을 관찰하고, LUMORA 추론 경로 및 CancellationToken이 아직 구현되지 않았다는 한계를 기록합니다.

### 평가 도구 실행 예시

아래는 재현 또는 별도 context 시험에 사용할 예시 명령입니다. 이번 4K 공통 시험의 원시 결과와 설정은 [40. 모델 비교 평가 준비](40_MODEL_COMPARISON_PROTOCOL.md)에 기록했습니다.

```powershell
python scripts\model-evaluation\benchmark.py --model qwen3:8b --contexts 4096 --repeat 3
python scripts\model-evaluation\benchmark.py --model qwen3:8b --contexts 8192 --repeat 3
python scripts\model-evaluation\benchmark.py --model qwen3:8b --contexts 4096 --repeat 1 --cancel-probe
```

### 다음 후보 다운로드 승인 후에만 실행할 명령

Gemma 명령은 2026-11-09 별도 승인 후 실행 완료했습니다. 아래 Qwen3.5 명령은 실행하지 않았습니다. 모델 다운로드는 네트워크 전송과 디스크 사용을 발생시킵니다.

```powershell
ollama pull qwen3.5:9b-q4_K_M
```

위는 사전 검토용 명령 예시입니다. 이미 설치된 `qwen3:8b`와 `gemma3:4b`를 다시 받을 필요는 없습니다. 다음 후보는 정확한 태그·크기·약관·Ollama 호환성을 승인 직전에 재확인한 뒤 그 tag만 명시적으로 승인해야 합니다.

## 공식 자료

- Ollama Library: [Qwen3 8B](https://ollama.com/library/qwen3:8b), [Qwen3.5 9B Q4_K_M](https://ollama.com/library/qwen3.5:9b-q4_K_M), [Gemma 3 4B](https://ollama.com/library/gemma3:4b)
- Ollama Registry tags/manifest (크기 및 양자화): [qwen3:8b manifest](https://registry.ollama.ai/v2/library/qwen3/manifests/8b), [qwen3.5:9b-q4_K_M manifest](https://registry.ollama.ai/v2/library/qwen3.5/manifests/9b-q4_K_M), [gemma3:4b manifest](https://registry.ollama.ai/v2/library/gemma3/manifests/4b)
- Qwen 제작자 자료: [Qwen3-8B model card](https://huggingface.co/Qwen/Qwen3-8B), [Qwen3.5-9B model card](https://huggingface.co/Qwen/Qwen3.5-9B), [Qwen3 release blog](https://qwenlm.github.io/blog/qwen3/)
- Google 자료: [Gemma 3 4B IT model card](https://huggingface.co/google/gemma-3-4b-it), [Gemma Terms of Use](https://ai.google.dev/gemma/terms)
- Ollama 실행 기능: [Tool Calling](https://docs.ollama.com/capabilities/tool-calling), [Structured Outputs](https://docs.ollama.com/capabilities/structured-outputs), [Context length](https://docs.ollama.com/context-length), [FAQ: GPU, context, ollama ps](https://docs.ollama.com/faq)

## 8단계 후속: Qwen3 평가 보류 및 10단계 결과

후속 반복 검증에서 최초 CLI 정답은 재현되지 않았습니다. 동일 한국어 질문에 대해 CLI, `/api/chat`, `/api/generate`가 부정확한 응답을 반환했고 영어 ASCII 대조군은 API에서 통과했습니다. 따라서 Qwen3 8B 제품 채택을 보류했습니다. 공통 18개 scenario 비교도 수행했으며 결과는 [37. 벤치마크 결과](37_MODEL_BENCHMARK_RESULTS.md), 동일 조건·채점법은 [40. 모델 비교 평가 준비](40_MODEL_COMPARISON_PROTOCOL.md)를 참고하십시오.

현재 설치 `0.40.15`와 대체 후보 호환성은 공식 자료와 실제 실행을 구분합니다. Ollama v0.40.15 릴리스 노트는 Qwen3/Qwen3.5 long-context 지원을, v0.6.0 릴리스 노트는 Gemma 3 지원 추가를 명시합니다. Gemma 3 공식 Library는 Ollama 0.6 이상 요구를 표시합니다. `gemma3:4b`는 이 PC에서 설치해 비도구 scenario를 평가했습니다. 도구 포함 요청은 이 런타임에서 “does not support tools” HTTP 400으로 거부되어 해당 모델의 tool behavior는 평가할 수 없었습니다.

공통 harness는 `temperature=0`, `seed=42`, `num_ctx=4096`, `num_predict=256`, `stream=false`를 고정하고 thinking 유무·길이, 빈 content, 완료 이유, scenario 기대값 및 실패 분류를 기록합니다. `scenarios.json`의 18개 QA, 조건, 사실/미상, 날짜·숫자·JSON, 도구 제안·거부 요청을 동일한 `/api/chat` 경로로 실행했습니다. CLI 응답을 API 점수에 합치지 않았고 도구를 실행하지 않았습니다. 전체 결과는 37번 문서 및 격리 raw JSON에 보존했습니다.

## 12단계: Qwen3.5 4B 설치 및 기본 추론 (2026-11-11)

사용자가 승인한 `qwen3.5:4b-q4_K_M`만 다운로드했습니다. Ollama는 다운로드 전후 `0.40.15`였고 기존 `gemma3:4b`, `qwen3:8b`도 유지됐습니다. API는 `127.0.0.1:11434`에서 응답했습니다.

| 항목 | 로컬 설치 확인 |
|---|---|
| tag / digest | `qwen3.5:4b-q4_K_M` / `d8b0f5e9760cd1682034f292d7ef72ec46f432149be0df7574bf2d6e92e38c04` |
| 크기 | `/api/tags` 3,324,173,934 bytes, `ollama list` 3.3 GB. 사전 Registry layer 합산 3,324,173,757 bytes와 177 bytes 차이가 있어 설치된 크기는 로컬 API 값을 기준으로 기록 |
| 모델 metadata | GGUF, 4.2B, `Q4_K_M`, context metadata 262,144, capability `completion`, `vision`, `tools`, `thinking` |
| 라이선스 | `ollama show`에 Apache License 2.0 표시 |
| Ollama 버전 | 다운로드 전후 및 추론 확인 시 `0.40.15` |
| 기존 모델 | `gemma3:4b` 및 `qwen3:8b` 설치 유지 |

네 가지 합성 질문 각각을 CLI와 `POST http://127.0.0.1:11434/api/chat`에서 3회씩 실행했습니다(총 24회). CLI와 API 모두 기본 모델 설정을 사용했고 API는 `stream=false`였습니다. 설정 변경이나 고정 seed는 적용하지 않았으므로 반복 출력은 결정적이지 않습니다. 테스트 데이터는 인공 직원명·생일뿐이며 DB, tool, LUMORA 추론 경로는 사용하지 않았습니다.

| 경로 | 성공/완료 | 응답 시간 | 관찰 |
|---|---:|---:|---|
| CLI `ollama run ... --verbose` | 프로세스 종료 코드 0: 12/12. 11/12에서 최종 답변 구분자가 확인됐고, 미상 생일 질문 1회는 최종 답변 구분자가 없어 불완전 출력 가능성으로 기록 | 평균 30.5초, 중앙값 28.3초, 범위 9.7–56.3초 | 기본 thinking 출력이 CLI에도 나타났습니다. 프로세스 종료 성공만으로 완전한 답변을 보증하지 않습니다. |
| Ollama `/api/chat`, `stream=false` | HTTP 200 및 `done=true`: 12/12. `done_reason=stop`: 11회, `length`: 1회. content는 12회 모두 비어 있지 않음 | 평균 23.6초, 중앙값 18.3초, 범위 6.2–54.9초 | thinking 필드는 12/12에 있었고 최종 content와 분리했습니다. 길이 제한 응답은 content가 일부 있어도 잘림 실패로 분류했습니다. |

모델 metadata의 기본 temperature는 1이며 이번 시험에서 CLI/API의 기본값을 덮어쓰지 않았습니다. API 생성량은 468–4,070 tokens, 생성 속도 평균은 약 76.66 tokens/s였습니다(think 토큰 포함). 첫 CLI 실행은 모델 load 약 8.66초, 이후 API는 이미 로드된 runner를 사용해 API `load_duration`은 5–14ms 범위였습니다. Qwen3.5 API의 시간은 과거 Gemma의 수도 질문 단일 warm smoke 결과(약 1.83초)와 질문·출력 길이·thinking 조건이 달라 직접 비교하지 않습니다. 본 결과는 18개 공통 시나리오 벤치마크가 아닙니다.

반복한 각 질문에서 API 최종 content 문구가 세 차례 모두 달랐습니다. 한국어 출력은 생성됐지만 이번 smoke test는 의미 정확도 채점이 아니며, 생일 데이터 추출, 기억 정책, 개인정보 관련 설명의 정확성을 확인한 것으로 간주하지 않습니다. 특히 미상 정보 질문은 API 1회가 `done_reason=length`였고 CLI 1회도 최종 답변 구분자가 관찰되지 않아 추가 검증이 필요합니다.

| 자원 관찰 | 측정값 |
|---|---|
| 추론 전 GPU | RTX 3060 12,288 MiB 중 사용 1,846 MiB / 여유 10,268 MiB |
| 실행 중 GPU snapshot | 614개 표본에서 사용 1,841–5,961 MiB, GPU 이용률 0–91% |
| `ollama ps` | runner 3.1 GB, `100% GPU`, context 4096. CPU offload는 관찰되지 않음 |
| 시스템 메모리 | 전체 15.81 GiB, 테스트 시작 여유 4.22 GiB; 테스트 후 최소 0.48 GiB, 완료 직후 0.49 GiB 여유(메모리 사용률 96%) |

시스템 메모리 수치는 OS 전체 snapshot이며 모델 단독 사용량이 아닙니다. 완료 직후 여유 메모리가 낮았으므로 더 큰 context나 추가 연속 시험은 피하고, 메모리가 회복된 뒤 실행 상태를 다시 확인해야 합니다. 이전 Gemma GPU 관찰은 실행 전 메모리 점유가 달라 직접적인 모델 간 사용량 비교로 사용하지 않습니다. 반복별 요청, API 최종 content, thinking 내용이 아닌 존재 여부, 시간 및 자원 표본은 [Qwen3.5 4B smoke 결과](../scripts/model-evaluation/results/2026-11-11-qwen35-4b-smoke.json)에 보존했습니다.

Ollama의 runner가 자동 unload된 뒤(모델 중지 명령이나 설정 변경 없음) `ollama ps`는 비어 있었습니다. 이후 두 번 확인한 GPU 사용량은 1,550–1,692 MiB, 시스템 RAM 여유는 4.94–5.07 GiB였습니다. 따라서 시험 직후의 메모리 압박은 지속 상태가 아니었으며 회복을 확인했습니다.

이번 단계는 설치 및 기본 동작 확인만 완료했습니다. 전체 공통 scenario 평가, Tool Calling 검증, 제품 모델 선정 및 LUMORA 추론 경로 연결은 수행하지 않았습니다.
