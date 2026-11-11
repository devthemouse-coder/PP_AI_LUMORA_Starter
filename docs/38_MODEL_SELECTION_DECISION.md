# 38. 로컬 모델 선정 결정

## 결정 상태: 공통 벤치마크 완료, 제품 모델 선정 보류

사용자가 설치를 승인한 `qwen3:8b`와 `gemma3:4b`에 동일한 18개 합성 scenario를 3회씩 실행했습니다. 엄격 판정은 Gemma 18 PASS / 15 FAIL / 21 NOT EVALUABLE, Qwen 9 PASS / 45 FAIL입니다. 두 모델 모두 오류 유형과 미평가 영역이 제품 적용 조건을 충족하지 못하므로 기본 모델로 선정하거나 LUMORA 추론 경로에 연결하지 않습니다. 상세 원시 결과와 제한은 [37. 벤치마크 결과](37_MODEL_BENCHMARK_RESULTS.md)에 있습니다. Qwen CLI/API 단일 질문 진단은 [39. Qwen3 CLI/API 진단](39_OLLAMA_CHAT_API_DIAGNOSTIC.md)을 참고하십시오.

### 잠정 시험 순서와 호환성

1. **첫 기준 모델: `qwen3:8b` (설치 완료)** — 실제 Ollama tag 크기는 5,225,388,164 bytes, `Q4_K_M` GGUF, 8.2B parameters, Apache-2.0입니다. 반복 한국어 응답 검증은 실패했습니다. 이 설치는 시험용 기준선이지 품질 승자나 제품 승인 결정이 아닙니다.
2. **두 번째 기준 모델: `gemma3:4b` (설치 완료)** — Registry/`/api/tags` 크기 3,350,272,403 bytes, digest `68ceab7e468a31c988b44d5a4984a0e21f58c4f7158c6ce29c1804babac38df7`, `Q4_K_M`, 3.9B. 공통 benchmark에서 비도구 scenario 6개가 3/3 통과하고 5개가 3/3 실패했습니다. JSON 날짜 필드 오류가 있었고, 도구 포함 요청은 Ollama 0.40.15가 모두 HTTP 400으로 거부했습니다. Gemma Terms of Use가 적용되며 제품 채택은 보류합니다.
3. **`qwen3.5:4b-q4_K_M` (설치 및 smoke test 완료)** — 로컬 `/api/tags`는 3,324,173,934 bytes와 digest `d8b0f5e9760cd1682034f292d7ef72ec46f432149be0df7574bf2d6e92e38c04`를 보고했습니다. `Q4_K_M`, 4.2B, Apache-2.0입니다. 기본 CLI/API 요청은 작동했지만 API 1/12가 `length`로 끝났고 반복 출력이 달랐습니다. 전체 공통 평가 전이며 제품 모델로 선정하지 않았습니다.
4. **후속 후보: `qwen3.5:9b-q4_K_M` (미설치)** — Ollama Library 6.6 GB, 8.95B, `Q4_K_M`, Apache-2.0. 4K VRAM 계획치는 약 8–11 GiB로 추정되며 확인 시점의 유휴 GPU 메모리와 경계가 겹쳐 일부 CPU offload/OOM 위험이 있습니다. 4B 전체 평가 결과와 당시 자원을 확인하고 별도 승인 후 시험합니다.

### 11단계 사전 조사 결정 기록 (2026-11-11)

당시에는 **`qwen3.5:4b-q4_K_M`를 먼저 시험하도록 권고했고 아직 다운로드하지 않은 상태였습니다.** Ollama Library 표시는 3.3 GB, 4.21B, `Q4_K_M`, tools/thinking capability였으며 Registry layer 합계는 3,324,173,757 bytes, license layer는 Apache-2.0이었습니다. 제작자 카드의 native context 262,144는 모델 metadata이지 이 PC의 VRAM에서 해당 길이를 처리한다는 보증이 아닙니다. 이후 설치에서 확인된 `/api/tags` 크기는 3,324,173,934 bytes로 Registry layer 합계와 177 bytes 차이가 있습니다. 설치 크기는 로컬 API 값을 기준으로 기록합니다.

후속 후보 `qwen3.5:9b-q4_K_M`는 6.6 GB, 8.95B, `Q4_K_M`, Apache-2.0이며 같은 tools/thinking 표시가 있습니다. 다만 layer 합계 6,550,825,373 bytes 및 4K VRAM 계획치 약 8–11 GiB로, 확인 당시 여유 VRAM 9,849–10,233 MiB 범위와 겹쳐 첫 시험으로는 risk가 더 큽니다. 4B 시험 결과 및 실행 직전 자원을 확인하고 별도 승인을 받아 진행하는 편이 합리적입니다.

사전 조사 당시 호환성 판단은 공식 자료에 기반했습니다. 이후 승인된 정확한 4B tag가 Ollama `0.40.15`에서 설치되고 기본 추론에 응답함을 확인했습니다. 이 결과는 해당 tag의 전체 기능, vision, tool call, 전체 한국어 품질까지 검증한 것은 아닙니다.

사전 조사 중 `nvidia-smi` 반복 snapshot은 RTX 3060 12,288 MiB 중 1,881–2,265 MiB 사용 / 9,849–10,233 MiB 여유였고, `ollama ps`에는 실행 모델이 없었습니다. 시스템 메모리는 확인 당시 총 약 15.81 GiB, 여유 약 3.01 GiB였습니다. 4B의 4K 5–7 GiB 및 9B의 8–11 GiB는 모두 추정치입니다. 예전 Gemma 전체 GPU 사용량 8,697 MiB 관찰과 최근 격리 benchmark의 실행 후 5,374 MiB 관찰은 측정 시점·조건이 달라 모델별 요구량으로 직접 비교하지 않습니다. 4B는 9B보다 전체 GPU 적재 가능성이 높고 기존 Gemma 4B와의 동급 비교에도 적합합니다. 자세한 근거와 평가 개선안은 [36. 로컬 모델 선정 및 평가 계획](36_LOCAL_MODEL_EVALUATION.md)을 참조하십시오.

후보별 근거, 버전별 호환성 및 공통 비교 방법은 [36. 모델 평가 계획](36_LOCAL_MODEL_EVALUATION.md), [37. 벤치마크 결과](37_MODEL_BENCHMARK_RESULTS.md), [40. 모델 비교 평가 준비](40_MODEL_COMPARISON_PROTOCOL.md)를 참고하십시오.

## 범위와 다음 단계

- 현재 설치·승인된 모델은 `qwen3:8b`, `gemma3:4b`, `qwen3.5:4b-q4_K_M`
- 다음 후보 `qwen3.5:9b-q4_K_M` 설치에는 별도 사용자 승인이 필요
- Ollama 업데이트, 모델 캐시 삭제, 설정 변경을 하지 않음
- LUMORA Application/Infrastructure의 추론 계약, 모델 설정, 동시성 제어를 변경하지 않음
- 이번 단계에서는 모델 제품 선정·Provider 추론 구현·DB 연결을 수행하지 않음

### 다음 권고

1. `qwen3:8b`, `gemma3:4b`의 전체 공통 benchmark 결과에 따라 두 모델을 제품 기본값으로 채택하지 않았습니다. Qwen3.5 4B는 smoke test만 완료했으므로 선정 결정은 계속 보류합니다.
2. 다음은 `qwen3.5:4b-q4_K_M`에 공통 18개 scenario를 같은 조건으로 실행하는 것입니다. smoke test의 1회 응답은 길이 제한으로 끝났으므로 전체 평가에서 출력 상한과 thinking을 기존 프로토콜대로 관리하고, 실행 직전 자원을 확인합니다. 현재 runner 자동 unload 후 RAM 여유 4.94–5.07 GiB와 빈 `ollama ps`를 확인했습니다.
3. `qwen3.5:9b-q4_K_M`는 미설치 상태이며 별도 사용자 승인 전 다운로드하지 않습니다. 이번 단계에서 Ollama 업데이트, 기존 모델 삭제, 설정 변경은 하지 않았습니다.
4. 후속 모델 비교에서는 동일 합성 시나리오와 엄격한 완료 규칙을 유지하고, tool 호환성을 모델 판단 점수와 분리하며 실제 도구는 실행하지 않습니다.
5. 제품 적용 전에는 한국어 사실성, 모순·미상 처리, JSON 값 정확도, 권한 범위 위반 방지와 응답 취소·자원 사용을 별도 검증합니다.

## LUMORA 적용 조건 (향후 검토)

현재 `ILlmProvider`는 runtime 상태와 모델 목록만 다루며 추론 계약은 구현되어 있지 않습니다. 추론 단계에서 별도 설계·승인을 거쳐 모델명을 설정으로 받되, 설치 여부 확인, 호출 timeout/CancellationToken, 요청 동시성 상한, 모델별 `num_ctx`/생성 제한을 분리해야 합니다. 모델 미설치·로딩 실패는 fallback 없이 명시적으로 실패시켜야 합니다. 사용자의 기억을 전달하기 전에는 scope/권한 검증, 최소 문맥 전달, 로그 비노출, 취소 시 서버 요청 종료, Tool 입력 재검증을 추가해야 합니다.
