# 33. Ollama 로컬 연동

## 적용한 API와 확인 범위

현재 Adapter는 Ollama 공식 API의 다음 읽기 전용 경로를 사용합니다.

| Ollama 경로 | 용도 |
|---|---|
| `GET /api/version` | 런타임 버전 |
| `GET /api/tags` | 설치 모델 목록 및 API가 제공하는 메타데이터 |

`/api/show`와 생성·pull 관련 API는 호출하지 않습니다. 로컬 설치 환경에서는 Ollama `0.40.15`가 확인되었고, 점검 당시 설치 모델은 0개였습니다. 버전은 운영 환경에 따라 달라질 수 있습니다.

Ollama 공식 API 문서: [API](https://docs.ollama.com/api), [Version](https://docs.ollama.com/api/version), [List models](https://docs.ollama.com/api/tags).

## 애플리케이션 설정

`src/Lumora.Api/appsettings.json`의 기본값은 다음과 같습니다.

```json
{
  "Llm": {
    "Ollama": {
      "BaseAddress": "http://127.0.0.1:11434/"
    }
  }
}
```

애플리케이션은 기본값 외에도 `http://127.0.0.1:11434/` 또는 `http://[::1]:11434/`만 허용합니다. DNS 호스트명, 외부·공인 IP, 다른 포트, HTTPS, 사용자 정보, 경로·쿼리·fragment가 지정된 주소는 등록 단계에서 거부됩니다.

## 모델 목록과 상태 해석

목록에는 이름, ID, digest, 크기, 수정 시각, format, family, parameter size와 quantization 정보를 반환합니다. 각 선택 정보는 Ollama 응답에 존재하는 경우에만 반환합니다. 원격 모델로 표시된 항목은 노출하지 않습니다.

모델 상태 조회는 설치 카탈로그에서 이름·ID·digest의 정확한 일치를 확인합니다. 모델이 설치되었다는 사실은 추론 성공을 보증하지 않습니다. 이번 단계는 추론을 실행하지 않으므로 모델의 실제 실행 가능성은 검증하지 않습니다.

## 실행 및 수동 확인

PowerShell에서 서비스와 모델 목록을 수동 확인할 수 있습니다.

```powershell
ollama --version
ollama list
Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/version'
Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/tags'
```

마지막 두 명령은 loopback에만 연결합니다. `ollama pull` 또는 모델 다운로드 명령은 자동 실행하지 않습니다.

## HTTP 오류 및 리소스 제한

- 연결 제한 2초, 요청 제한 8초
- 1 MiB를 초과하는 성공 응답은 거부
- 외부 redirect 자동 추적 및 시스템 프록시 사용 비활성화
- CancellationToken 전파
- 오류는 연결 불가, timeout, HTTP 오류, 잘못된 응답으로 분류
- 재시도 및 상태 캐시 없음

연결 거부는 모델 목록이 비어 있다는 뜻이 아니라 runtime unavailable입니다. HTTP 실패 본문과 내부 예외는 API 응답에 노출하지 않습니다.
