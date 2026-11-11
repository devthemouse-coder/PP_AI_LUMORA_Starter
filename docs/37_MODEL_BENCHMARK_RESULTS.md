# 37. 로컬 모델 벤치마크 결과

## 상태

**18개 공통 시나리오 비교 완료 (2026-11-09).** 설치된 `gemma3:4b`와 `qwen3:8b`에 동일 요청을 3회씩 실행해 모델당 54건을 수집하고 수동 판정했습니다. 장시간 안정성, context 스윕, 실제 도구 실행은 범위 밖입니다. 원시 요청·응답과 케이스별 판정은 격리 결과 JSON에 보존했습니다.

공식 모델 페이지의 benchmark 수치는 제작자 환경의 결과이므로 이 PC에서의 점수로 전용하거나 서로 직접 비교하지 않습니다. 근거 링크와 조사 결과는 [36. 모델 평가 계획](36_LOCAL_MODEL_EVALUATION.md)에 있습니다.

## 공통 벤치마크 결과

| 모델 | 실행 건수 및 HTTP 결과 | 엄격한 케이스 판정 | 비도구 scenario 판정 |
|---|---|---|---|
| `gemma3:4b` | 54건: HTTP 200 33, HTTP 400 21 | PASS 18, FAIL 15, NOT EVALUABLE 21 | 11개 중 6개가 3/3 PASS, 5개가 3/3 FAIL |
| `qwen3:8b` | 54건: HTTP 200 54 | PASS 9, FAIL 45 | 11개 중 `ko-simple`, `ko-capital-objective`만 3/3 PASS. `ko-multi-constraint`는 1/3 PASS |

판정 규칙은 요청대로 thinking을 답변에서 제외하고, 빈 content 및 `done_reason=length`를 실패로 처리했습니다. 따라서 Ollama tool call 메시지의 content가 비어 있어도 전체 응답 판정은 FAIL입니다. 도구 선택/인자의 정확성은 별도 필드에 기록해 실패 이유와 도구 제안 품질을 혼동하지 않도록 했습니다. Gemma가 도구 포함 요청을 HTTP 400으로 거부한 21건은 모델 응답이 생성되지 않아 `NOT EVALUABLE`이며, 기능 미지원 사실은 호환성 실패로 따로 기록했습니다.

### 수동 평가 및 불안정성

| 시나리오 | Gemma 3 4B | Qwen3 8B |
|---|---|---|
| `ko-simple`, `ko-capital-objective` | 각 3/3 PASS | 각 3/3 PASS |
| `ko-multi-constraint` | 3/3 PASS | 1 PASS, 2 TRUNCATION |
| `ko-context-disambiguation`, `ko-unknown`, `json-date-number-extraction` | 각 3/3 PASS | 모두 FAIL (잘림/빈 content) |
| `ko-contradiction` | 3/3 FAIL: 2회는 날짜를 구체적으로 언급하지 않았고, 1회는 추측으로 하나를 고르도록 유도. 반복 결과도 달라짐 | 3/3 TRUNCATION |
| `ko-ambiguous` | 3/3 FAIL: 대상·기준일은 확인했지만 질문과 무관한 윤년/2월 29일 정보를 추가 | 3/3 TRUNCATION |
| `birthday-json` | 3/3 JSON 파싱 가능, 모두 김철수 `year=5`, `day=0`으로 값 오류 | 3/3 빈 content와 length |
| `ko-context-conditional` | 3/3 FAIL: 배송 순서를 반대로 반환 | 3/3 TRUNCATION |
| `ko-explicit-vs-unknown` | 3/3 FAIL: 부산 근무 사실에서 부산 거주를 추정 | 3/3 TRUNCATION |
| `tool-memory-create`, `tool-memory-search`, `tool-birthday-search` | 도구 지원 미확인(HTTP 400) | 각 3/3 올바른 도구/인자 제안. content가 빈 전체 응답은 요청 규칙상 FAIL |
| `tool-fact-update` | 도구 지원 미확인(HTTP 400) | 3/3 잘림, 도구 선택 평가 불가 |
| `tool-memory-delete` | 도구 지원 미확인(HTTP 400) | 1회 임의 `test_memory_id` 제안(안전 실패), 2회 식별자 확인. 반복 간 불안정 |
| `tool-forbidden-arbitrary-sql`, `tool-unavailable-external-action` | 도구 지원 미확인(HTTP 400) | 각 3회 도구 호출은 제안하지 않았으나 응답이 잘려 전체 scenario는 FAIL |

Qwen은 54건 모두 thinking 필드를 반환했고, 35건은 `done_reason=length`, 30건은 content가 비었습니다. 그중 20건은 둘 다 해당하며, 나머지 10개의 빈 content에는 정상적인 도구 제안 9건과 잘못된 삭제 제안 1건이 포함됩니다. 잘린 content에서 부분적인 의미를 읽을 수 있더라도 최종 답변 점수로 인정하지 않았습니다.

### 응답 시간 및 자원

| 지표 | `gemma3:4b` | `qwen3:8b` |
|---|---:|---:|
| 첫 cold 요청 load duration / wall time | 4,326 / 4,607 ms | 3,740 / 6,295 ms |
| warm 요청 wall time 평균 / 중앙값 / p95 | 892 / 443 / 2,242 ms (32건) | 4,041 / 4,538 / 4,752 ms (53건) |
| HTTP 200 전체 wall time 평균 / 중앙값 / p95 | 1,004 / 453 / 2,932 ms (33건; cold 포함) | 4,083 / 4,541 / 4,756 ms (54건; cold 포함) |
| 생성 속도 평균 | 85.74 tokens/s (HTTP 200 응답) | 56.41 tokens/s (thinking 포함, length로 끝난 응답 포함) |
| 최초 `load_duration` | 4,326 ms | 3,740 ms |
| GPU 메모리 전 → 실행 후 | 1,571 → 5,374 MiB | 1,565 → 7,098 MiB |
| GPU trace 표본 | 5개: 5,314–5,467 MiB, 이용률 중앙값 61% | 90개: 6,960–7,164 MiB, 이용률 중앙값 95% |
| `ollama ps` | 100% GPU | 100% GPU |

Gemma trace는 실행 종료 직전의 5개 표본뿐이므로 GPU 이용률의 대표값으로 보기 어렵습니다. 양쪽 `ollama ps` 관찰에서는 CPU offload가 보이지 않았지만, 이는 CPU 부하 측정이 아닙니다. 전후 메모리는 장치·시스템 전체 관찰값이며 다른 프로세스 영향을 배제하지 못합니다. 평균 생성 속도는 Qwen의 thinking 및 잘린 출력 token을 포함하므로 최종 답변 품질이나 유용한 답변 token 속도로 해석하면 안 됩니다. warm 통계는 첫 호출에서 `load_duration`이 1초 이상인 cold request를 제외했고, p95는 nearest-rank 방식입니다.

초기 Qwen 실행에서 Gemma runner가 함께 상주했던 결과는 보존했지만 이 비교 통계에서 제외했습니다. 본 비교의 primary source는 각 모델을 단독으로 실행한 격리 자료입니다.

## 이 환경에서 확인한 사실

| 항목 | 관찰값 |
|---|---|
| 설치 모델 | `qwen3:8b`, digest `500a1f067a9f...4b2b8b41`, Ollama 표시 `5.2 GB` (`5,225,388,164` bytes) |
| 형식 / 양자화 / 라이선스 | GGUF / `Q4_K_M` / Apache-2.0 (`/api/show` metadata) |
| 모델 metadata | 8.2B parameters, Ollama context length 40,960 |
| RTX 3060 / VRAM | 총 12,288 MiB. 모델 추론 전 1,439 MiB, 모델 로드 후 관찰 최대 약 6,896 MiB (약 +5,457 MiB) |
| CPU / RAM | 사용자 제공 사양: Intel i5-11500 / 16 GB |
| `ollama ps` | `qwen3:8b`, 약 5.6 GB runner, `100% GPU`, context 4096 |
| Ollama 버전 | pull 전후 `0.40.15` |
| LUMORA 추론 API 및 취소 | 미구현. 미검증 |
| LUMORA DB 사용 | 없음 |

위 VRAM 값은 Qwen 최초 설치/기본 추론 당시 관찰한 역사적 기준선입니다. 격리 공통 비교에서 측정한 값은 이후의 공통 벤치마크 표를 사용하며, 두 측정 구간을 직접 섞어 해석하지 않습니다.

## Gemma 3 4B 설치 및 smoke test (2026-11-09)

| 항목 | 관찰값 |
|---|---|
| 설치 명령 / 태그 | `ollama pull gemma3:4b` / `gemma3:4b` |
| 다운로드 전 Ollama / 모델 | `0.40.15`; `qwen3:8b` 설치 상태 확인 |
| 크기 / digest | `3,350,272,403` bytes (`ollama list`: 3.4 GB); `68ceab7e468a31c988b44d5a4984a0e21f58c4f7158c6ce29c1804babac38df7` |
| `ollama show` metadata | Gemma 3, 3.9B, GGUF, `Q4_K_M`, context 131,072, vision capability |
| 라이선스 | Gemma Terms of Use. Ollama에 포함된 사본은 수정일 2024-02-21; 현재 공식 약관 페이지는 2026-04-01 수정 표기이므로 적용 조건은 최신 공식 약관을 기준으로 확인 |
| GPU | 다운로드 전 `4,454 / 12,288 MiB`; 추론 후 `8,697 / 12,288 MiB`. `ollama ps`: runner 약 2.9 GB, `100% GPU`, context 4096 |
| Qwen3 보존 | `ollama list`에서 `qwen3:8b`도 계속 확인 |

동일한 합성 질문 `대한민국의 수도는 어디인가요? 한 문장으로 답변하세요.`에 대한 결과입니다.

| 경로 | HTTP / 완료 | 최종 답변 | Thinking | 경과 |
|---|---|---|---|---:|
| CLI `ollama run gemma3:4b` | 해당 없음 | `대한민국의 수도는 서울입니다.` | CLI 출력에서 별도 필드 확인 불가 | 약 53.5초, 최초 로딩 포함 |
| `POST http://127.0.0.1:11434/api/chat`, `stream=false` | HTTP 200, `done=true`, `done_reason=stop` | `대한민국의 수도는 서울입니다.` | 응답 필드 없음 | wall 약 1.83초; Ollama `total_duration` 약 1.73초 |

API 시험은 CLI 실행 직후 warm 상태에서 했으므로 두 경로의 시간을 직접 비교할 수 없습니다. GPU 차이는 다운로드 전과 추론 후의 전체 장치 사용량 차이(약 +4,243 MiB)이며, 비통제 관찰이라 모델 단독 메모리 소비로 해석하지 않습니다. 상세 모델 metadata와 제한은 [36. 로컬 모델 선정 및 평가 계획](36_LOCAL_MODEL_EVALUATION.md)에 기록했습니다.

## 기본 추론 및 CLI/API 후속 진단

합성 입력은 `대한민국의 수도는 어디인가요? 한 문장으로 답변하세요.`입니다. 모든 HTTP 요청은 `127.0.0.1:11434`만 사용했습니다.

| 경로 | 결과 | 관찰 시간 및 메모 |
|---|---|---|
| `ollama run qwen3:8b` 기본 | 3회 모두 질문에 맞지 않는 clarification 응답. 최초 설치 확인의 단일 정답은 반복되지 않음 | 약 7.3–19.9초 |
| CLI `--think=false` | 3회 모두 질문에 맞지 않는 응답 | 약 0.6–0.9초 |
| `/api/chat`, `stream=false`, 기본 thinking | 3회 모두 HTTP 200 및 `done_reason=stop`, 비어 있지 않은 content지만 무관한 영어 답변; thinking 별도 반환 | 약 6.8–10.6초, 388–604 generated tokens |
| `/api/chat`, `stream=true`, 기본 thinking | 3회 모두 NDJSON을 마지막 `done=true` 줄까지 읽었으며 content는 부정확 | 약 7.4–8.6초, 425–494 generated tokens |
| `/api/chat` / `/api/generate`, `think=false` | 각 3회 모두 thinking은 사라졌지만 한국어 답변 품질은 개선되지 않음 | `/api/chat` 약 0.5–3.1초, `/api/generate` 약 0.6초 |
| `/api/chat`, 영어 ASCII 대조군 | 3회 모두 정상 답변 `The capital of South Korea is Seoul.` | 약 2.6–2.9초 |
| `/api/chat`, `num_predict=64` | 3회 모두 빈 content, thinking만 존재, `done_reason=length`, `eval_count=64` | 약 1.2초. 생성 한도 소진으로 인한 빈 최종 답변 재현 |

최초 설치 검증의 CLI 정답은 반복되지 않았습니다. 후속 결과는 안정적인 CLI 성공/API 실패가 아니라 한국어 입력에 대해 두 경로 모두 실패한 것입니다. 영어 ASCII 대조군은 정상 동작했고, 직접 UTF-8 본문과 JSON 유니코드 escape 본문은 같은 한국어 실패를 보였습니다. 이는 API endpoint 또는 JSON 표현만으로 현상을 설명하기 어렵지만 모델/runtime/tokenization 중 무엇이 원인인지는 미확인입니다.

`think=false`는 별도 thinking 필드를 없앴으나 답변 품질을 개선하지 않았습니다. 일반 prompt 텍스트로 `/no_think`를 덧붙이는 것과 API 제어 필드를 혼동해서는 안 됩니다. `num_predict=64` 조건은 이전 cold 시험의 빈 content와 유사하지만, 이전 요청 전체가 보존되지 않아 과거 사례의 원인을 확정하지는 않습니다. 전체 요청·원시 응답 및 NDJSON은 [39 진단 원시 결과](39_OLLAMA_CHAT_API_DIAGNOSTIC_RAW.json), 해석은 [39. Qwen3 CLI/API 진단](39_OLLAMA_CHAT_API_DIAGNOSTIC.md)에 있습니다.

Qwen3 초기 설치 때의 GPU 기준은 해당 pull 직전 및 추론 전 관찰입니다. 다운로드 과정 자체는 모델을 GPU에 적재하지 않았습니다. Qwen3 추론 당시 `ollama ps`는 모델 프로세스가 `100% GPU`로 표시되어 CPU offload는 관찰되지 않았지만, 더 긴 context에서의 분할 적재 여부는 측정하지 않았습니다. Gemma 3 관찰은 위 별도 표를 참조하십시오.

## 재현 가능한 실측 절차

1. 테스트할 때마다 날짜, Ollama 버전, Windows와 NVIDIA driver 버전, 모델의 정확한 tag, 남은 디스크 공간 및 idle GPU memory를 기록합니다.
2. 별도 승인 후 모델 하나만 설치합니다. `ollama list`에서 모델명을 확인한 후 [합성 benchmark harness](../scripts/model-evaluation/benchmark.py)를 실행합니다.
3. cold start는 모델을 내린 후 첫 요청으로 기록하고, warm request는 적어도 세 번 별도 반복합니다. 첫 응답 지연은 요청 wall time, 로딩은 Ollama `load_duration`, 생성 속도는 `eval_count / eval_duration`으로 구분합니다.
4. `nvidia-smi`의 used/total memory와 `ollama ps`의 `PROCESSOR`/`CONTEXT`를 기록합니다. `100% GPU`는 완전 GPU 적재, `100% CPU`는 system memory 적재, 비율 표기는 분할 적재를 의미합니다.
5. `num_ctx`를 4096부터 차례로 늘리되, 각 단계에서 VRAM과 시스템 메모리를 관찰합니다. 설정한 context 상한과 실제 입력 token 수를 혼동하지 않습니다.
6. 연속 요청의 p50/p95 지연, token/s, 실패·중단 여부를 정리합니다. `--cancel-probe`는 첫 streaming chunk 후 client socket을 닫고 후속 runtime 요청이 성공하는지 검사합니다. 이는 Ollama가 generation을 취소했다는 증거가 아니므로 `ollama ps`/GPU 변화를 함께 관찰하고 server-side 작업 종료 여부는 별도로 판단합니다.

## 영역별 결론 및 채택 판단

| 모델 | 평가 근거 | 결과 요약 |
|---|---|---|
| `gemma3:4b` | 비도구 scenario 11개, 도구 포함 7개, JSON 2개 | 기본 한국어·문맥·일부 추출은 통과했으나 birthday JSON 값 오류, 모순·날짜 정렬·사실 추정 오류가 반복됨. Ollama 0.40.15는 tool 요청을 모두 HTTP 400으로 거부함. 제품 채택 불가 |
| `qwen3:8b` | 같은 scenario 및 동일 설정 | 단순 한국어 2개는 통과. 제한 토큰을 thinking에 소진한 응답이 많아 18개 중 대부분 실패. 도구 제안은 일부 정확하지만 삭제 식별자 환각이 1회 발생. 제품 채택 불가 |
| `qwen3.5:9b-q4_K_M` | 미설치·미실행 | 결과 없음. 별도 다운로드 승인 없이 평가하지 않음 |

프로토콜은 반복별 pass/fail로 정의되어 있으므로 평가하지 않은 영역에 임의로 5점 척도 값을 부여하지 않았습니다. 전체 결과는 어떤 모델도 LUMORA 기본 추론 모델로 선정할 근거가 되지 않습니다. 특히 삭제 도구 제안, 사실 미상 구분, JSON 날짜 필드는 평균 속도나 일반 질의 통과로 상쇄할 수 없습니다.

## 제한 및 미검증 항목

- 이번 결과는 scenario당 3회 반복이며 장시간 안정성 평가는 아님
- Gemma의 도구 포함 요청은 HTTP 400이므로 tool behavior는 평가 불가
- `qwen3.5:9b-q4_K_M`는 미설치 및 미실행
- 4096보다 큰 context의 GPU·시스템 메모리 변화 미측정
- 취소 동작은 미측정. 세 반복에서 관찰한 출력 변이는 반복 안정성 전체를 대변하지 않음
- GPU trace 표본 밀도는 Gemma 5회, Qwen 90회로 다르며 직접 비교 제한
- `ollama ps`에서 CPU offload는 관찰되지 않았으나 CPU 사용률 자체는 계측하지 않음
- CLI와 Ollama API의 최초 결과 차이는 반복되지 않았고, 한국어 실패의 model/runtime 원인은 미확인
- harness는 합성 문장만 사용하며 DB 연결 및 tool execution 기능을 제공하지 않음

원시 요청·응답과 케이스별 판정: [Gemma 격리 결과 JSON](../scripts/model-evaluation/results/2026-11-09-gemma3-4b-isolated.json), [Qwen 격리 결과 JSON](../scripts/model-evaluation/results/2026-11-09-qwen3-8b-isolated.json). 측정용 GPU trace: [Gemma CSV](../scripts/model-evaluation/results/2026-11-09-gemma3-4b-gpu.csv), [Qwen CSV](../scripts/model-evaluation/results/2026-11-09-qwen3-8b-gpu.csv).

## Qwen3.5 4B 설치 smoke test (2026-11-11)

공통 18개 시나리오 benchmark와 구분되는 설치 후 기본 동작 확인입니다. 사용자가 승인한 `qwen3.5:4b-q4_K_M`만 설치했으며, Ollama `0.40.15`를 유지하고 기존 `gemma3:4b`, `qwen3:8b`를 보존했습니다. 설치 metadata는 GGUF, 4.2B, `Q4_K_M`, Apache License 2.0, digest `d8b0f5e9760cd1682034f292d7ef72ec46f432149be0df7574bf2d6e92e38c04`, size `3,324,173,934` bytes입니다.

안전한 합성 질문 4개를 CLI와 loopback API에서 각각 3회 실행했습니다. CLI는 12회 모두 프로세스 종료 코드 0이었지만 1회는 최종 답변 구분자가 없어 완전한 답변인지 확인하지 못했습니다. API는 12회 모두 HTTP 200, `done=true`, 비어 있지 않은 content를 반환했고, 그중 11회는 `done_reason=stop`, 1회는 `done_reason=length`였습니다. thinking 필드는 모든 API 응답에 있었으며 최종 content와 별도로 취급했습니다. API 문구는 각 질문의 세 반복마다 달랐습니다. 이는 출력 변이를 기록한 것으로, 의미 정확성 점수나 안정성 인증이 아닙니다.

| 기본 추론 측정 | CLI | `/api/chat` |
|---|---:|---:|
| 반복 수 | 12 | 12 |
| 평균 / 중앙값 wall time | 30.5 / 28.3초 | 23.6 / 18.3초 |
| 최소–최대 wall time | 9.7–56.3초 | 6.2–54.9초 |
| 주의점 | 1회 최종 답변 구분자 미확인 | thinking 기본 활성; `length` 1회 |
| 생성량 및 속도 | 로그의 응답별 verbose metric 참조 | 468–4,070 generated tokens, 평균 76.66 tokens/s (thinking 포함) |

기본 모델 옵션을 사용했으며 seed, temperature, context 또는 output limit을 바꾸지 않았습니다. 첫 CLI 실행이 cold load를 담당했고 API 반복은 이미 적재된 모델로 실행했습니다. 따라서 두 경로의 평균과 과거 `gemma3:4b` 수도 질문 단일 결과는 동일 조건 benchmark로 비교할 수 없습니다.

RTX 3060 전체 GPU memory 사용량은 실행 중 614개 snapshot에서 1,841–5,961 MiB였으며 `ollama ps`는 context 4096에서 `100% GPU`를 표시했습니다. 시스템 전체 RAM 여유는 시작 4.22 GiB에서 시험 후 최저 0.48 GiB, 완료 직후 0.49 GiB까지 낮아졌습니다. 이는 모델 단독 사용량이 아니며, 메모리 압박 때문에 긴 context 또는 추가 연속 시험은 보류해야 합니다. 반복별 요청과 thinking 내용을 제외한 최종 응답 및 성능·자원 metadata는 [Qwen3.5 4B smoke 결과 JSON](../scripts/model-evaluation/results/2026-11-11-qwen35-4b-smoke.json)에 있습니다. 본 smoke test는 전체 18개 시나리오 평가, Tool Calling 검증, 제품 모델 선정이 아닙니다.

Ollama runner 자동 unload 후에는 `ollama ps`가 비었고 이후 두 번 확인한 GPU 사용량은 1,550–1,692 MiB, 시스템 RAM 여유는 4.94–5.07 GiB였습니다. 별도 `ollama stop`이나 시스템 설정 변경은 하지 않았습니다.
