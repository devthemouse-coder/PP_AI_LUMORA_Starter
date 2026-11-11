# 41. Ollama 업데이트 및 Gemma 3 시험 사전 준비

## 목적과 실행 경계

2026-11-09 사전 점검 당시 이 PC의 Ollama 환경과 `qwen3:8b` 상태를 확인하고, `gemma3:4b` 시험에 Ollama 업데이트가 필요한지 공식 자료와 대조했습니다. 아래 환경 표는 Gemma 설치 전 snapshot입니다.

- 사전 점검에서는 Ollama 업데이트, 모델 다운로드/삭제, 설정·환경 변수·방화벽 변경을 하지 않았습니다.
- SQLite, 백업 파일, 사용자 데이터에는 접근하지 않았습니다.
- 추론 확인은 `127.0.0.1:11434`에 합성 질문 한 건만 보냈습니다.
- 후속 별도 승인에 따라 정확히 `gemma3:4b`를 설치하고 기본 추론을 확인했습니다. 아래 설치 결과를 참조하십시오.

## 후속 9단계: Gemma 설치 확인 (2026-11-09)

- 실행한 다운로드 명령은 `ollama pull gemma3:4b`뿐입니다. 다른 tag/model은 다운로드하지 않았습니다.
- 다운로드 전·후 Ollama 버전은 `0.40.15`로 동일했습니다. Gemma와 기존 `qwen3:8b`가 모두 `ollama list`에 있습니다.
- 설치 크기 `3,350,272,403` bytes, model digest `68ceab7e468a31c988b44d5a4984a0e21f58c4f7158c6ce29c1804babac38df7`; `ollama show`는 Gemma 3, 3.9B, `Q4_K_M`, context 131,072, vision capability를 표시했습니다.
- License metadata는 Gemma Terms of Use라고 표시했고, 내장 사본의 수정일은 2024-02-21입니다. 최신 공식 약관 페이지는 2026-04-01 수정 표기를 하므로 현행 조건은 [Gemma Terms of Use](https://ai.google.dev/gemma/terms)를 기준으로 확인해야 합니다.
- CLI 응답은 `대한민국의 수도는 서울입니다.`였고 최초 로딩을 포함해 약 53.5초 걸렸습니다. 별도 thinking 출력은 판정하지 않았습니다.
- 이후 loopback REST API는 HTTP 200, `done=true`, `done_reason=stop`, 동일한 한국어 정답, thinking 필드 없음이었습니다. CLI 직후 warm API wall time은 약 1.83초 (`total_duration` 약 1.73초)로 cold CLI와 직접 비교할 수 없습니다.
- GPU 사용량은 다운로드 직전 `4,454 / 12,288 MiB`, 추론 이후 `8,697 / 12,288 MiB`. `ollama ps`는 Gemma runner 2.9 GB, `100% GPU`, context 4096을 보였습니다. 차이는 비통제 system-wide 관찰이지 모델 단독 메모리 수치가 아닙니다.
- 모델 설치와 1회 기본 추론만 확인했습니다. 18개 scenario benchmark, JSON/tool calling, 긴 context 및 안정성은 미실시입니다. 자세한 결과는 [36. 모델 평가 계획](36_LOCAL_MODEL_EVALUATION.md), [37. 벤치마크 결과](37_MODEL_BENCHMARK_RESULTS.md), [38. 선정 결정](38_MODEL_SELECTION_DECISION.md)을 참고하십시오.

## 현재 환경 확인 결과

다음 항목은 Gemma 설치 전 사전 점검 당시 기록입니다.

| 항목 | 확인 결과 |
|---|---|
| Ollama 버전 | `ollama version is 0.40.15`; 작업 전후 변경 없음 |
| 실행 파일 | `C:\Users\n-mol\AppData\Local\Programs\Ollama\ollama.exe` |
| 로컬 API | `127.0.0.1:11434` 응답. 시험 요청은 HTTP 200 |
| 설치 모델 | `qwen3:8b` 하나, `ollama list` 표시 5.2 GB, ID `500a1f067a9f` |
| Qwen 메타데이터 | `ollama show`: 8.2B, GGUF, `Q4_K_M`, context metadata 40,960 |
| 모델 저장 경로 | `OLLAMA_MODELS` 사용자 환경 변수 미설정. 기본 경로 `C:\Users\n-mol\.ollama\models` 존재 |
| 기존 모델 manifest | `manifests-v2\ollama.com\library\qwen3\8b` 존재. 파일 크기 859 bytes |
| 실행 중 적재 상태 | `ollama ps`: Qwen 약 5.6 GB, `100% GPU`, context 4096 |
| GPU 메모리 | 확인 시 RTX 3060 `7,186 / 12,288 MiB` 사용 중. 이는 Qwen 적재 중 관찰값이며 Gemma 측정값이 아님 |
| Provider 구현 범위 | [OllamaLlmProvider](../src/Lumora.Infrastructure/Providers/Ollama/OllamaLlmProvider.cs)는 `/api/version`, `/api/tags` 조회 및 설치 모델 판별을 구현. 추론/채팅 endpoint는 구현하지 않음 |

### Qwen3 현재 smoke test

요청은 `POST http://127.0.0.1:11434/api/chat`으로 보냈고 모델은 `qwen3:8b`, 기본 thinking, `stream=false`였습니다. 개인정보나 DB 데이터는 포함하지 않았습니다.

- HTTP 요청 성공: `200`
- Ollama 완료: `done=true`, `done_reason=stop`
- Thinking 필드 존재
- 경과 시간: 약 6.75초 (`total_duration` 약 6.63초)
- 내용: 질문이 깨졌다고 영어로 재질문을 요청하는 응답. 대한민국 수도 질문에 답하지 못함.

따라서 Ollama runtime/API 연결과 생성 완료 자체는 확인했지만, 이번 한국어 질문에 대한 모델 동작은 정상 답변으로 볼 수 없습니다. 앞선 반복 진단에서도 Qwen3의 한국어 실패가 확인되어 있습니다. 이는 Gemma 3의 예상 성능이나 Qwen3의 원인을 확정하지 않습니다.

## 업데이트 필요성 및 권장 버전

**Gemma 3 사용을 위한 Ollama 업데이트는 현재 필요하지 않습니다.**

- Ollama의 공식 [Gemma 3 Library](https://ollama.com/library/gemma3)는 Ollama `0.6` 이상을 요구합니다.
- 공식 [Ollama v0.6.0 릴리스 노트](https://github.com/ollama/ollama/releases/tag/v0.6.0)는 Gemma 3 모델 추가를 명시합니다.
- 현재 버전 `0.40.15`는 v0.6.0보다 이후 릴리스입니다. `0.40.15` 릴리스 노트는 Qwen3/Qwen3.5 long-context 모델 지원도 기술합니다.

권장 사항은 **현재 `0.40.15`를 유지한 채 Gemma 시험을 준비하는 것**입니다. 불필요한 업데이트는 평가 변수를 늘리므로, 보안 수정이나 구체적인 호환성 문제가 확인되지 않는 한 Gemma 기준 시험 전에 업데이트하지 않는 편이 낫습니다. 공식 최소 버전 확인은 Gemma 3의 정확한 `4b` tag가 이 PC에서 정상 적재·추론된다는 증거가 아니므로, 별도 승인 후 로컬 smoke test와 평가 harness 실행은 여전히 필요합니다.

## Gemma 3 4B 후보 정보

| 항목 | 확인 결과 및 범위 |
|---|---|
| 정확한 Ollama tag | `gemma3:4b` |
| Registry manifest 총 크기 | 3,350,272,403 bytes (약 3.35 GB, 십진 단위; 약 3.12 GiB) |
| Manifest layer | 모델 2,499,012,288 bytes, vision projector 851,251,104 bytes, template/license/params/config 나머지 |
| 형식·양자화 | Manifest가 GGUF 및 `gemma3:4b-it-q4_K_M` 기반 모델 layer를 가리킴 |
| 컨텍스트 | Google Gemma 3 모델 카드와 Ollama Library가 4B 모델의 128K 문맥을 설명. 이 PC의 전체 문맥 메모리 운용은 미측정 |
| 언어 | Google은 140개 이상 언어를 설명하지만, 확인한 자료만으로 한국어별 품질은 보증할 수 없음 |
| 라이선스 | [Gemma Terms of Use](https://ai.google.dev/gemma/terms). Apache-2.0으로 취급하지 않음 |
| 상업적 사용 | Google은 Gemma의 상업적 사용을 허용한다고 설명하지만 약관과 사용 제한을 따라야 함. 재배포 시 제한 고지, 약관 사본, 변경 파일 표시 및 Notice 파일 등 의무를 검토해야 함 |
| JSON | Ollama runtime은 schema 기반 structured output 기능을 문서화함. 이는 이 모델 tag의 schema 준수율을 보장하지 않으며 실제 모델 출력 검증이 필요 |
| Tool Calling | Ollama runtime 일반 기능 문서는 존재하나, 확인한 Gemma 3 모델 자료에서 Gemma 3의 공식 tool calling 지원을 확인하지 못함. 모델 호환성을 미확인으로 취급 |
| VRAM 계획 | 기존 문서의 4K 약 5–7 GB는 추정치일 뿐 실측이 아님. 모델 파일 크기와 VRAM 사용량은 같지 않으며 context/cache/runner가 추가 메모리를 사용 |

Registry 크기는 [gemma3:4b manifest](https://registry.ollama.ai/v2/library/gemma3/manifests/4b)에서 확인한 layer 크기 합입니다. 로컬에 받은 크기나 전송량을 측정한 값이 아니며, 실제 설치 시 표시 크기는 manifest 및 Ollama 표기와 다시 대조해야 합니다.

## 업데이트 영향과 호환성 범위

- Ollama Windows 문서는 설치 프로그램을 `%LOCALAPPDATA%\Programs\Ollama`, 모델·설정을 `%HOMEPATH%\.ollama`에 저장한다고 설명합니다. 현재 실행 파일과 기본 모델 경로도 이 분리된 위치에 있습니다.
- 공식 Windows 문서는 `OLLAMA_MODELS`로 모델 경로를 변경할 수 있다고 설명합니다. 이 PC에서는 해당 사용자 변수가 설정되지 않아 기본 경로를 사용합니다.
- 바이너리 설치 경로와 모델 저장 경로가 별도라는 사실은 업데이트 시 기존 모델 보존을 보장하지 않습니다. 업데이트/제거 과정의 보존 동작은 이번 작업에서 실제 검증하지 않았으므로, 모델이 자동으로 유지된다고 가정하지 않습니다.
- 공식 API 문서는 `/api/chat` 및 `/api/tags` 등의 요청 형식을 제공합니다. 조사한 자료에서 모든 버전 간 API 호환성 보장이나 downgrade 호환성 보장은 확인하지 못했습니다.
- 현재 LUMORA Provider는 상태·버전·모델 목록 API만 사용합니다. Qwen3 진단 harness는 로컬 `/api/chat`을 직접 사용하지만, LUMORA 추론 경로는 아직 구현되지 않았습니다.
- 따라서 업데이트가 필요하지 않은 이번 시험 준비에서는 runtime을 바꾸지 않는 것이 가장 단순한 기준선입니다. 나중에 별도 이유로 업데이트한다면 Provider status/models 기능과 Qwen3 로컬 smoke test를 다시 실행해야 합니다.

## 업데이트 승인 시에만 수행할 복구 계획

이번에는 아래 절차를 실행하지 않았습니다. 향후 업데이트가 별도로 승인된 경우의 계획입니다.

1. 현재 `ollama --version`, `ollama list`, `ollama show qwen3:8b`, manifest/digest, `OLLAMA_*` 변수, API status 및 GPU 사용량을 기록합니다.
2. 공식 배포처에서 업데이트 대상과 정확한 버전, 릴리스 노트, Windows 설치/복구 파일을 확인합니다. “최신” 자동 업데이트에 맡기지 않고 승인한 버전을 고정합니다.
3. Ollama 트레이 앱을 종료해 파일이 사용 중이지 않게 한 뒤, 전체 `C:\Users\n-mol\.ollama\models`를 별도 위치에 복사합니다. 복사 전후 총 크기와 Qwen manifest 및 blob 파일 존재를 대조합니다. 백업 디스크 여유 공간을 먼저 확인합니다.
4. 현재 버전 `0.40.15`의 공식 설치 파일을 보존할 수 있는지 확인합니다. 정확한 설치 파일을 확보하지 못하면 업데이트를 진행하지 않습니다.
5. 업데이트를 별도로 승인받은 경우에만 적용한 후 버전, `ollama list`, Qwen manifest/model availability, `/api/version`, `/api/tags`, Qwen synthetic smoke test, `ollama ps`/GPU 상태를 확인합니다.
6. 문제가 생기면 Ollama를 종료하고 보존한 `0.40.15` 설치 파일로 재설치하는 것을 우선 복구 경로로 삼습니다. 모델 백업은 원본을 덮어쓰지 말고 파일 손상·누락이 확인될 때만 별도 검토합니다.

Ollama 공식 문서는 Windows 자동 업데이트와 수동 설치 방법을 안내하지만, 이 환경의 업데이트를 되돌리는 원클릭 rollback이나 낮은 버전 daemon과 높은 버전이 만진 모든 model metadata의 호환성을 보증한다고 확인하지 못했습니다. 따라서 재설치는 가능한 복구 시도이지, 성공이 검증된 롤백 보장은 아닙니다. 실제 변경 전 별도 동의가 필요합니다.

## 별도 승인 후 진행할 다음 단계

권장 순서는 Ollama 업데이트 없이 진행하는 것입니다.

1. Gemma 3 적용 전 현행 약관의 사용·재배포 제한을 검토합니다.
2. 설치된 `gemma3:4b`와 `qwen3:8b`의 tag 및 자원 여유를 확인하고, [40. 모델 비교 평가 준비](40_MODEL_COMPARISON_PROTOCOL.md)의 동일한 18개 scenario를 각각 실행합니다. 사용자 데이터와 실제 DB는 사용하지 않습니다.
3. Tool Calling은 모델이 호출을 제안하는지만 평가하고 실제 tool/DB 작업은 연결하거나 실행하지 않습니다. JSON은 schema 적합성과 값 정확성을 별도로 채점합니다.
4. Qwen3.5 등 아직 설치되지 않은 모델을 시험하려면 정확한 tag와 라이선스를 검토하고 별도 다운로드 승인을 받습니다.

## 공식 자료

- [Ollama Gemma 3 Library](https://ollama.com/library/gemma3) — 최소 Ollama 버전, 정확한 model tag, context 및 언어 안내
- [Ollama v0.6.0 release notes](https://github.com/ollama/ollama/releases/tag/v0.6.0) — Gemma 3 추가
- [Ollama v0.40.15 release notes](https://github.com/ollama/ollama/releases/tag/v0.40.15) — 현재 설치 버전 및 Qwen3/Qwen3.5 관련 변경
- [Gemma 3 model card](https://ai.google.dev/gemma/docs/core/model_card_3) 및 [Gemma Terms of Use](https://ai.google.dev/gemma/terms) — 언어/컨텍스트 설명 및 이용 조건
- [gemma3:4b Registry manifest](https://registry.ollama.ai/v2/library/gemma3/manifests/4b) — tag layer 크기와 형식
- [Ollama Windows 안내](https://docs.ollama.com/windows) — 설치/모델 경로
- [Ollama FAQ](https://docs.ollama.com/faq) — Windows/macOS 업데이트 절차
- [Ollama chat API](https://docs.ollama.com/api/chat), [structured outputs](https://docs.ollama.com/capabilities/structured-outputs), [tool calling](https://docs.ollama.com/capabilities/tool-calling) — runtime 기능 계약. 모델별 성공 보장은 아님

문서상 모델 제작자 주장, Registry manifest, 로컬에서 확인한 사실을 구분했습니다. Gemma 3의 기본 한국어 smoke test는 통과했지만, 실제 품질 전반·장문·JSON/tool calling 결과는 미검증입니다. 모델 설치 후 전체 성능 벤치마크도 아직 수행하지 않았습니다.
