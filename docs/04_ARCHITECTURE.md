# 04. 시스템 아키텍처

## 1. 아키텍처 스타일

초기에는 **모듈형 모놀리스(Modular Monolith)** 를 사용합니다. 하나의 ASP.NET Core 애플리케이션 안에서 책임과 의존성 경계를 분리합니다. 초기부터 마이크로서비스를 도입하지 않습니다.

## 2. 논리 구조

```text
Vue 3 + TypeScript
       |
       | HTTPS/HTTP JSON, OpenAPI 계약
       v
ASP.NET Core API
       |
       +-- Application
       |     +-- Chat
       |     +-- Memory
       |     +-- Query Planning
       |     +-- Authorization
       |
       +-- Domain
       |     +-- Conversation
       |     +-- MemoryItem
       |     +-- MemorySource
       |
       +-- Infrastructure
             +-- LLM Providers
             +-- Persistence Providers
             +-- Encryption / Key Protection
             +-- Logging / Backup
```

## 3. 의존성 규칙

- Domain은 외부 인프라 패키지를 참조하지 않습니다.
- Application은 Domain과 추상화 계약을 참조합니다.
- Infrastructure는 Application/Domain의 추상화를 구현합니다.
- API는 Application을 호출하며 DB나 LLM SDK를 직접 사용하지 않습니다.
- Frontend는 API 계약만 사용합니다.

## 4. 권장 프로젝트 구조

```text
src/
  Lumora.Api/
  Lumora.Application/
  Lumora.Domain/
  Lumora.Infrastructure/
  Lumora.Contracts/
  Lumora.Worker/             # 필요성이 확인된 후 추가
frontend/
  lumora-web/
tests/
  Lumora.Domain.Tests/
  Lumora.Application.Tests/
  Lumora.Infrastructure.Tests/
  Lumora.Api.Tests/
```

초기 구현에서 프로젝트 수가 과도해지면 합리적으로 통합할 수 있으나, 계층 경계는 유지합니다.

## 5. 교체 지점

| 대상 | 추상화 경계 | 초기 구현 | 대체 후보 |
|---|---|---|---|
| LLM | IChatCompletionProvider | Ollama | 다른 로컬 런타임 |
| 저장소 | Repository 계약 | SQLite | MSSQL, PostgreSQL |
| 키 보호 | IKeyProtectionProvider | Windows 보호 기능 | OS/서버 키 관리 |
| 시간 | IClock | SystemClock | 테스트용 고정 시계 |
| 임베딩 | IEmbeddingProvider (후속) | 미구현 | 로컬 임베딩 모델 |

## 6. 설정

- 제품 표시명, 환경, 저장소 경로, LLM endpoint/model 등은 설정으로 관리합니다.
- 비밀정보는 일반 `appsettings.json`에 평문으로 저장하지 않습니다.
- 개발·운영 환경별 설정을 분리합니다.
- 사용자 데이터가 포함된 설정과 로그를 Git에 커밋하지 않습니다.

## 7. 통신

- Frontend와 API 사이의 계약은 버전 관리합니다.
- 요청/응답 DTO는 Domain Entity를 그대로 노출하지 않습니다.
- 오류 응답은 일관된 문제 상세 형식(Problem Details 등)을 사용합니다.
- 스트리밍은 취소 가능한 HTTP 스트리밍으로 설계하며 프론트엔드와 프로토콜을 문서화합니다.

## 8. 데이터 흐름

기억 저장은 LLM의 추출 결과를 바로 DB에 넣지 않습니다.

```text
User Input
   -> LLM extraction proposal
   -> schema validation
   -> authorization / ownership check
   -> conflict detection
   -> user confirmation when needed
   -> encryption
   -> repository save
```

조회는 다음과 같습니다.

```text
User Question
   -> intent/query classification
   -> allow-listed query handler
   -> authorization check
   -> parameterized DB query
   -> decrypt authorized result
   -> LLM answer composition
   -> response with evidence
```

LLM이 임의 SQL을 생성·실행하는 구조는 금지합니다.
