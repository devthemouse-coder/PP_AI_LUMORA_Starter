# 42. 자연어 대화 MVP

## 범위

Development 환경의 한국어/영어 채팅 화면에서 생일 저장, 특정 이름의 생일 조회, 이번 달 생일자 조회를 지원합니다. 그 외 요청은 거절합니다. 수정·삭제·일반 대화·실제 tool 실행은 지원하지 않습니다.

## 데이터 흐름

1. Vue 화면은 `POST /api/chat`으로 메시지, 현재 UI 언어와 선택적 Conversation ID를 보냅니다.
2. API는 설정된 개발 Tenant/User scope만 사용합니다. 요청 본문에서 scope를 받지 않습니다.
3. Application은 사용자 입력과 최근 메시지 최대 2개를 역할별로 분리해 `ILlmChatService`에 전달합니다.
4. Ollama Adapter는 `127.0.0.1` 또는 `::1`의 포트 11434에만 비스트리밍 요청을 보냅니다.
5. 의도 JSON은 허용된 필드/Intent만 파싱합니다. 이름·날짜가 사용자 입력 및 같은 문장 구간에 명시되어 있는지 검증하고, 날짜 유효성도 백엔드에서 확인합니다.
6. 생일 사실은 기존 `IStructuredFactUseCases`를 통해 생성·검색합니다. LLM은 SQL/Repository/실제 Tool에 접근하지 않습니다.
7. 최종 답변은 저장소가 확인한 사실을 바탕으로 Application에서 생성합니다. 정상적으로 파싱된 대화 교환은 기존 암호화된 Conversation/Message에 저장됩니다.

## 제한 및 기본값

- Ollama 모델 설정: `Llm:ModelName`; 기본값은 `qwen3.5:4b-q4_K_M`.
- 추론 제한: `temperature=0`, `num_ctx=4096`, `num_predict=256`, `think=false`, JSON 출력.
- Provider timeout은 60초, 프런트엔드 요청 timeout은 70초입니다.
- 모델 누락/연결 실패는 안전한 503, 잘못된 응답/잘림도 503으로 처리하며 대화 저장이나 fact 변경을 하지 않습니다.
- Fact 검색은 현재 로그인 사용자를 대체하지 않는 고정 개발 Tenant/User scope에 한정됩니다. 인증·인가 미구현으로 외부/운영 노출 금지입니다.
- 기존 Conversation/Message 테이블을 재사용합니다. 이번 작업에서 Migration 또는 DB 스키마 변경은 없습니다.

## 검증

- Application 단위 테스트에서 합성 LLM 응답을 주입해 저장, 조회, 현재 월 조회, 미등록 이름, 잘못된 날짜, 입력 grounding 오류, 복수 인물 오배정, UNKNOWN/지원하지 않는 Intent, Provider 오류, 잘림과 빈/잘못된 JSON을 검사합니다.
- Ollama Adapter 테스트는 HTTP 요청 옵션, loopback 요청 경로, 완료/빈/잘림/잘못된 응답, timeout과 cancellation을 검증합니다.
- 2026-11-11, Ollama 0.40.15와 `qwen3.5:4b-q4_K_M`의 loopback smoke test에서 UTF-8 바이트 요청이 HTTP 200을 반환했습니다. 합성 입력 `홍길동의 생일은 5월 10일이야`에 대해 최종 시스템 prompt와 동일한 `SAVE_BIRTHDAY` 및 이름·월·일 JSON을 반환했고 `done_reason=stop`이었습니다. Ollama `ollama ps`는 모델 3.1 GB, 100% GPU, context 4096을 표시했습니다. 모델이 적재된 상태에서 경과시간 737ms, Ollama `total_duration` 710,770,700ns, `load_duration` 13,184,500ns, prompt/eval 토큰은 각각 453/27이었습니다. DB 연결 및 fact 저장은 수행하지 않았습니다.
- 빌드·테스트와 동시에 실행한 별도 동일 prompt 요청은 39.16초가 걸렸습니다. 이는 격리된 성능 측정이 아니며, 동시 부하에 따른 큰 변동 가능성을 보여주므로 대표 성능치로 사용하지 않습니다.
- 초기 PowerShell 문자열 요청 결과는 한글 이름이 `????`로 표시되어 UTF-8 바이트 요청으로 재시험했습니다. 해당 첫 응답은 품질 결과에 포함하지 않았습니다.
- 이 단일 smoke test는 기능 확인일 뿐 모델 품질·성능 평가나 전체 벤치마크가 아닙니다.
