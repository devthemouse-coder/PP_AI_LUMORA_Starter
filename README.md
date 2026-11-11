# P.P. AI - LUMORA

> Private Personal AI Platform  
> 초기 프로젝트명이며, 상표·도메인 확인 후 변경할 수 있습니다.

## 프로젝트 목표

사용자가 알려준 정보를 안전하게 기억하고, 저장된 사실과 과거 맥락을 활용해 질문에 답하고 업무를 돕는 로컬 우선 개인형 AI 플랫폼입니다.

## 핵심 원칙

1. **데이터 외부 전송 금지:** 사용자 데이터와 프롬프트를 외부 LLM API로 보내지 않습니다.
2. **로컬 추론:** 초기 LLM 런타임은 Ollama이며, 공급자 교체가 가능하도록 인터페이스를 분리합니다.
3. **데이터와 모델 분리:** 기억은 일반 DB에 구조화해 저장하며, LLM 가중치에 사용자 기억을 저장하지 않습니다.
4. **DB 이식성:** 초기 SQLite, 확장 MSSQL. 업무 로직은 DB별 SQL에 종속되지 않도록 합니다.
5. **암호화:** 개인정보·민감 기억은 애플리케이션 계층에서 암호화하고 키는 DB와 분리합니다.
6. **문서 우선:** 설계 변경은 문서와 ADR(Architecture Decision Record)에 기록합니다.
7. **검증 우선:** AI Agent가 생성한 코드는 빌드·테스트·보안 검토를 통과해야 합니다.

## 현재 기술 기준

| 영역 | 초기 선택 |
|---|---|
| Backend | C#, ASP.NET Core 10 |
| Frontend | Vue 3, TypeScript, Vite |
| Local LLM | Ollama |
| 개인용 DB | SQLite |
| 기업용 DB | Microsoft SQL Server |
| ORM | Entity Framework Core, DB별 Provider 분리 |
| API 계약 | OpenAPI |
| 개발 환경 | Windows, VS Code |
| 테스트 | xUnit, 프론트엔드 단위 테스트 도구는 구현 시 확정 |

## 시작 전 읽을 문서

1. `AGENTS.md`
2. `docs/00_PROJECT_OVERVIEW.md`
3. `docs/01_REQUIREMENTS.md`
4. `docs/04_ARCHITECTURE.md`
5. `docs/05_DATABASE_DESIGN.md`
6. `docs/06_LLM_PROVIDER_DESIGN.md`
7. `docs/08_SECURITY_DESIGN.md`
8. `docs/09_PERFORMANCE_GUIDE.md`

## 첫 실행

백엔드 솔루션과 Vue 프런트엔드 기반에 SQLite/EF Core 저장소, Memory/StructuredFact 업무 API, 개발 환경 전용 Ollama 상태·모델 조회 및 생일 저장/조회 자연어 대화 MVP가 포함됩니다. 사용자 인증과 운영용 권한 시스템은 아직 구현되지 않았습니다. 업무·대화 API는 명시적으로 설정한 개발 scope에서만 사용할 수 있으며 외부에 노출하지 마십시오. 대화 흐름과 제한사항은 [42. 자연어 대화 MVP](docs/42_CHAT_MVP.md)를 참고하십시오.

개발 환경 버전 확인 명령:

```powershell
dotnet --info
dotnet --list-sdks
git --version
node --version
npm --version
ollama --version
ollama list
nvidia-smi
```

모델이 없으면 임의로 다운로드하지 말고, GPU 메모리와 모델 라이선스를 확인한 후 선택합니다.

### 백엔드 실행

```powershell
dotnet restore
$env:LUMORA_DATA_DIRECTORY = Join-Path $env:LOCALAPPDATA 'P.P. AI - LUMORA\data'
dotnet tool install --global dotnet-ef --version 10.0.14
dotnet ef database update --project src\Lumora.Infrastructure\Lumora.Infrastructure.csproj --startup-project src\Lumora.Api\Lumora.Api.csproj --context Lumora.Infrastructure.Persistence.Context.LumoraDbContext
dotnet run --project src\Lumora.Api\Lumora.Api.csproj -- --initialize-keys
dotnet build
dotnet test
dotnet run --project src\Lumora.Api\Lumora.Api.csproj -- --urls http://127.0.0.1:5080
```

`LUMORA_DATA_DIRECTORY`를 생략하면 DB는 `%LOCALAPPDATA%\P.P. AI - LUMORA\data\lumora.db`, DPAPI 키는 `%LOCALAPPDATA%\P.P. AI - LUMORA\keys`에 저장됩니다. `--initialize-keys`는 DB와 암호화 데이터가 없거나 DB가 비어 있는 경우에만 실행할 수 있습니다. 키 파일은 Git에 포함하지 마십시오.

개발 환경 OpenAPI 문서는 `http://127.0.0.1:5080/openapi/v1.json`, API 생존 상태는 `http://127.0.0.1:5080/api/health`, DB 연결 및 Migration 준비 상태는 `http://127.0.0.1:5080/api/ready`에서 확인합니다. 운영 시작 시 Migration을 자동 실행하지 않습니다.

### 프런트엔드 실행

```powershell
cd frontend
npm install
Copy-Item .env.example .env.local
npm run dev -- --host 127.0.0.1
```

프런트엔드 빌드와 정적 검사는 `npm run build`, `npm run lint`로 실행합니다. 자세한 절차와 구조는 [개발환경 안내](docs/13_DEVELOPMENT_SETUP.md)를 참고하십시오.

DB Provider, 논리 데이터 모델, Memory 수명 주기, 암호화 및 성능 설계는 [14. 데이터베이스 아키텍처](docs/14_DATABASE_ARCHITECTURE.md), [15. 데이터 모델](docs/15_DATA_MODEL.md), [16. Memory 설계](docs/16_MEMORY_DESIGN.md), [17. 데이터 보안 및 암호화](docs/17_DATA_SECURITY_AND_ENCRYPTION.md), [18. 데이터베이스 성능](docs/18_DATABASE_PERFORMANCE.md)에서 확인합니다.

SQLite 구현 및 운영 절차는 [19. SQLite 구현](docs/19_SQLITE_IMPLEMENTATION.md), [20. EF Core 가이드](docs/20_EF_CORE_GUIDE.md), [21. 암호화 구현](docs/21_ENCRYPTION_IMPLEMENTATION.md), [22. Migration 가이드](docs/22_DATABASE_MIGRATION_GUIDE.md), [23. DB 테스트 가이드](docs/23_DATABASE_TEST_GUIDE.md)를 참고하십시오.

SQLite 암호화 백업, DPAPI 키 복구 및 데이터 보존 절차는 [24. 백업 전략](docs/24_BACKUP_STRATEGY.md), [25. 백업·복원 안내](docs/25_BACKUP_RESTORE_GUIDE.md), [26. 키 복구 안내](docs/26_KEY_RECOVERY_GUIDE.md), [27. 데이터 보존 정책](docs/27_DATA_RETENTION_POLICY.md)를 참고하십시오. 백업/복구 설계 결정은 [ADR-0005](docs/decisions/ADR-0005-backup-and-key-recovery.md)에 기록했습니다.

Memory 및 StructuredFact UseCase, 검색 정책, HTTP 계약과 현재 개발 전용 scope 제한은 [28. Memory UseCase](docs/28_MEMORY_USECASE_DESIGN.md), [29. StructuredFact](docs/29_STRUCTURED_FACT_DESIGN.md), [30. API 계약](docs/30_API_CONTRACT.md), [31. 검색 설계](docs/31_SEARCH_DESIGN.md)와 [HTTP 요청 예제](src/Lumora.Api/Examples/memory-api.http)를 참고하십시오.

Ollama Provider 추상화, loopback 전용 Adapter의 실제 범위와 보안 제한, 개발 전용 상태·모델 및 채팅 API는 [32. Provider 아키텍처](docs/32_LLM_PROVIDER_ARCHITECTURE.md), [33. Ollama 연동](docs/33_OLLAMA_INTEGRATION.md), [34. 로컬 AI 보안](docs/34_LOCAL_AI_SECURITY.md), [35. Provider API](docs/35_LLM_PROVIDER_API.md), [42. 자연어 대화 MVP](docs/42_CHAT_MVP.md), [대화 API 요청 예제](src/Lumora.Api/Examples/chat-api.http) 및 [Ollama API 요청 예제](src/Lumora.Api/Examples/ollama-api.http)를 참고하십시오.

로컬 모델 평가 및 18개 공통 scenario 벤치마크 결과는 [36. 모델 평가 계획](docs/36_LOCAL_MODEL_EVALUATION.md), [37. 벤치마크 결과](docs/37_MODEL_BENCHMARK_RESULTS.md), [38. 선정 결정](docs/38_MODEL_SELECTION_DECISION.md), [39. Qwen3 CLI/API 진단](docs/39_OLLAMA_CHAT_API_DIAGNOSTIC.md), [40. 모델 비교 프로토콜](docs/40_MODEL_COMPARISON_PROTOCOL.md), [41. Ollama 업데이트 및 Gemma 준비](docs/41_OLLAMA_UPDATE_AND_GEMMA_PREPARATION.md)를 참고하십시오. `qwen3.5:4b-q4_K_M`는 사용자 승인으로 설치해 기본 smoke test를 완료했으며 전체 scenario benchmark나 제품 모델 선정은 아직 진행하지 않았습니다. 반복별 결과는 [Qwen3.5 4B smoke 결과](scripts/model-evaluation/results/2026-11-11-qwen35-4b-smoke.json)에 있습니다. 이전 공통 평가 요청·응답인 [Gemma 격리 결과](scripts/model-evaluation/results/2026-11-09-gemma3-4b-isolated.json)와 [Qwen 격리 결과](scripts/model-evaluation/results/2026-11-09-qwen3-8b-isolated.json)는 원본 그대로 보존했습니다. 다음 모델(`qwen3.5:9b-q4_K_M`) 설치는 별도 사용자 승인 전까지 하지 않습니다. 실행 도구는 [benchmark harness](scripts/model-evaluation/benchmark.py), 시나리오는 [합성 평가 시나리오](scripts/model-evaluation/scenarios.json)입니다.

## 저장소 기반 설정

저장소에는 `.gitignore`, `.editorconfig`, `.gitattributes`, `Directory.Build.props`, `Directory.Packages.props` 및 NuGet 공식 패키지 소스 설정이 포함됩니다. SDK 버전은 `global.json`으로 고정하지 않아 설치된 호환 .NET 10 SDK를 사용할 수 있습니다.

## 폴더 구조

```text
P.P.AI-LUMORA/
├── AGENTS.md
├── README.md
├── .editorconfig
├── .gitattributes
├── .gitignore
├── Directory.Build.props
├── Directory.Packages.props
├── Lumora.sln
├── NuGet.Config
├── docs/
│   ├── 00_PROJECT_OVERVIEW.md
│   ├── 01_REQUIREMENTS.md
│   ├── 02_ROADMAP.md
│   ├── 03_WBS.md
│   ├── 04_ARCHITECTURE.md
│   ├── 05_DATABASE_DESIGN.md
│   ├── 06_LLM_PROVIDER_DESIGN.md
│   ├── 07_MEMORY_DESIGN.md
│   ├── 08_SECURITY_DESIGN.md
│   ├── 09_PERFORMANCE_GUIDE.md
│   ├── 10_CODING_CONVENTION.md
│   ├── 11_SOLUTION_STRUCTURE.md
│   ├── 12_PROJECT_DEPENDENCIES.md
│   ├── 13_DEVELOPMENT_SETUP.md
│   ├── 14_DATABASE_ARCHITECTURE.md
│   ├── 15_DATA_MODEL.md
│   ├── 16_MEMORY_DESIGN.md
│   ├── 17_DATA_SECURITY_AND_ENCRYPTION.md
│   ├── 18_DATABASE_PERFORMANCE.md
│   ├── 19_SQLITE_IMPLEMENTATION.md
│   ├── 20_EF_CORE_GUIDE.md
│   ├── 21_ENCRYPTION_IMPLEMENTATION.md
│   ├── 22_DATABASE_MIGRATION_GUIDE.md
│   ├── 23_DATABASE_TEST_GUIDE.md
│   ├── 24_BACKUP_STRATEGY.md
│   ├── 25_BACKUP_RESTORE_GUIDE.md
│   ├── 26_KEY_RECOVERY_GUIDE.md
│   ├── 27_DATA_RETENTION_POLICY.md
│   ├── 28_MEMORY_USECASE_DESIGN.md
│   ├── 29_STRUCTURED_FACT_DESIGN.md
│   ├── 30_API_CONTRACT.md
│   ├── 31_SEARCH_DESIGN.md
│   ├── 32_LLM_PROVIDER_ARCHITECTURE.md
│   ├── 33_OLLAMA_INTEGRATION.md
│   ├── 34_LOCAL_AI_SECURITY.md
│   ├── 35_LLM_PROVIDER_API.md
│   ├── 36_LOCAL_MODEL_EVALUATION.md
│   ├── 37_MODEL_BENCHMARK_RESULTS.md
│   ├── 38_MODEL_SELECTION_DECISION.md
│   ├── 39_OLLAMA_CHAT_API_DIAGNOSTIC.md
│   ├── 39_OLLAMA_CHAT_API_DIAGNOSTIC_RAW.json
│   ├── 40_MODEL_COMPARISON_PROTOCOL.md
│   └── decisions/
│       ├── ADR-0001-initial-technology.md
│       ├── ADR-0002-sensitive-data-encryption.md
│       ├── ADR-0003-orm-selection.md
│       └── ADR-0004-database-provider-strategy.md
├── src/
│   ├── Lumora.Api/
│   │   └── Examples/
│   │       ├── memory-api.http
│   │       └── ollama-api.http
│   ├── Lumora.Application/
│   ├── Lumora.Contracts/
│   ├── Lumora.Domain/
│   └── Lumora.Infrastructure/
├── tests/
│   ├── Lumora.Api.IntegrationTests/
│   ├── Lumora.Application.UnitTests/
│   ├── Lumora.Domain.UnitTests/
│   └── Lumora.Infrastructure.IntegrationTests/
├── scripts/
│   └── model-evaluation/
│       ├── benchmark.py
│       └── scenarios.json
├── frontend/
└── prompts/
    └── 01_ENVIRONMENT_AND_REPOSITORY_SETUP.md
```

## 변경 원칙

기술 버전·패키지 버전은 구현 시점의 공식 지원 상태를 확인하고 `Directory.Packages.props` 등에서 중앙 관리합니다. 이 문서는 제품의 초기 기준이며, 구현 결과와 차이가 생기면 실제 코드를 기준으로 문서를 갱신합니다.
