# 11. 솔루션 구조

## 1. 전체 구조

```text
Lumora.sln
├── src/
│   ├── Lumora.Api/
│   ├── Lumora.Application/
│   ├── Lumora.Contracts/
│   ├── Lumora.Domain/
│   └── Lumora.Infrastructure/
├── tests/
│   ├── Lumora.Api.IntegrationTests/
│   ├── Lumora.Application.UnitTests/
│   ├── Lumora.Domain.UnitTests/
│   └── Lumora.Infrastructure.IntegrationTests/
└── frontend/
    ├── public/
    └── src/
        ├── assets/
        ├── components/
        ├── composables/
        ├── i18n/
        │   └── locales/
        ├── layouts/
        ├── router/
        ├── services/
        ├── stores/
        ├── types/
        ├── utils/
        └── views/
```

## 2. 백엔드 프로젝트 책임

| 프로젝트 | 책임 |
|---|---|
| `Lumora.Domain` | 핵심 도메인 모델 및 업무 규칙. 외부 기술에 의존하지 않습니다. |
| `Lumora.Application` | 유스케이스와 저장소·LLM 등의 추상화 계약. HTTP DTO를 알지 않습니다. |
| `Lumora.Infrastructure` | 데이터 저장소, LLM Provider, 암호화 등 외부 기술의 구현. 현재 구현체는 없습니다. |
| `Lumora.Contracts` | API 경계에서 사용하는 요청·응답 DTO. 현재 Health Check 응답을 정의합니다. |
| `Lumora.Api` | ASP.NET Core 진입점, HTTP 처리, 의존성 주입의 Composition Root. Health Check와 개발용 OpenAPI만 제공합니다. |

## 3. 테스트 프로젝트 책임

| 프로젝트 | 범위 |
|---|---|
| `Lumora.Domain.UnitTests` | Domain 단위 테스트 |
| `Lumora.Application.UnitTests` | Application 단위 테스트 |
| `Lumora.Infrastructure.IntegrationTests` | 실제 인프라 Provider 도입 후 통합 테스트 |
| `Lumora.Api.IntegrationTests` | HTTP 계약·API 동작 통합 테스트 |

기능 구현 전인 계층에는 가짜 업무 동작을 추가하지 않습니다. 초기 테스트는 계층 참조 경계와 API Health Check·개발 환경 OpenAPI를 검증하며, 실제 업무 테스트는 기능 구현 단계에서 추가합니다.

## 4. 프런트엔드 폴더 책임

| 폴더 | 책임 |
|---|---|
| `assets` | 스타일·이미지 등 번들 자산 |
| `components` | 재사용 UI 구성요소 |
| `layouts` | 공통 화면 레이아웃 |
| `views` | 라우트 단위 화면 |
| `router` | Vue Router 설정 |
| `stores` | Pinia 상태 저장소 |
| `services` | REST API 통신 |
| `composables` | 재사용 Composition API 로직 |
| `types` | 프런트엔드 타입 |
| `i18n/locales` | 한국어·영어 번역 자료 |
| `utils` | 순수 유틸리티 함수 |
| `public` | 빌드 과정에서 그대로 복사되는 정적 파일 |

## 5. 확장 원칙

- 초기에는 ASP.NET Core 모듈형 모놀리스로 유지하며 Worker나 별도 서비스는 필요성 확인 후 추가합니다.
- DB·LLM 구현은 Infrastructure에 국한하고 Domain/Application 경계를 유지합니다.
- 프런트엔드는 독립적인 Vue SPA로 두며 백엔드 프로젝트를 참조하지 않고 REST API만 사용합니다.
- 실제 ORM과 데이터베이스 선택은 별도 설계·ADR 검토 후 진행합니다.
