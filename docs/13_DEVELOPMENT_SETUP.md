# 13. 개발환경 구성 및 실행

## 1. 사전 요구사항

- Windows 64-bit
- .NET 10 SDK
- Node.js 24.12 이상 또는 22.18 이상 (Vue 생성기 요구 조건)
- npm
- Git
- VS Code (선택)

현재 구성은 Vue 생성 시점의 공식 `create-vue` Node 요구 조건을 따릅니다. Vite 8은 Node.js 20.19 이상 또는 22.12 이상을 요구하므로, 프로젝트의 `engines` 조건을 충족하는 Node.js를 사용합니다.

## 2. 백엔드 복원·빌드·테스트

저장소 루트의 `NuGet.Config`는 공개 패키지 복원을 위해 공식 nuget.org v3 피드를 등록합니다. 조직 전용 피드가 필요한 경우 개발자 또는 조직 설정을 별도로 구성하십시오.

```powershell
dotnet restore
dotnet build Lumora.sln
dotnet test Lumora.sln
```

## 3. 데이터베이스 준비 및 백엔드 실행

EF Core 도구를 설치하고 SQLite Migration을 명시적으로 적용합니다. 운영 서버 시작 시 Migration은 자동 실행되지 않습니다.

```powershell
dotnet tool install --global dotnet-ef --version 10.0.14
$env:LUMORA_DATA_DIRECTORY = Join-Path $env:LOCALAPPDATA 'P.P. AI - LUMORA\data'
dotnet ef database update --project src\Lumora.Infrastructure\Lumora.Infrastructure.csproj --startup-project src\Lumora.Api\Lumora.Api.csproj --context Lumora.Infrastructure.Persistence.Context.LumoraDbContext
```

`LUMORA_DATA_DIRECTORY`를 지정하지 않으면 DB는 `%LOCALAPPDATA%\P.P. AI - LUMORA\data\lumora.db`에 생성됩니다. `Database:Sqlite:DataDirectory` 설정 또는 `ConnectionStrings:Sqlite`로 경로를 명시할 수 있습니다. 연결 문자열을 설정하면 해당 경로가 우선합니다.

새 개발 환경에서 키를 준비합니다. DPAPI 키는 DB와 분리된 `%LOCALAPPDATA%\P.P. AI - LUMORA\keys`에 저장되며, 별도 경로는 `LUMORA_KEY_DIRECTORY` 또는 `Security:KeyDirectory`로 지정할 수 있습니다.

```powershell
dotnet run --project src\Lumora.Api\Lumora.Api.csproj -- --initialize-keys
```

이 명령은 키를 자동 교체하지 않습니다. 기존 데이터베이스가 암호화된 데이터 또는 검색 토큰을 포함하면 새 키 생성이 거부됩니다. DPAPI 키를 잃어버린 경우 이 명령을 다시 실행하지 말고 보호된 키 백업을 복구하십시오.

그 후 루프백 주소에만 바인딩하여 API를 실행합니다.

```powershell
dotnet run --project src\Lumora.Api\Lumora.Api.csproj -- --urls http://127.0.0.1:5080
```

- Health Check: `http://127.0.0.1:5080/api/health`
- Readiness Check: `http://127.0.0.1:5080/api/ready`
- 개발 환경 OpenAPI JSON: `http://127.0.0.1:5080/openapi/v1.json`

Health Check는 API 프로세스만 확인하며 DB·LLM 준비 상태를 판정하지 않습니다. Readiness Check는 SQLite 연결과 미적용 Migration 여부를 확인합니다. OpenAPI 문서는 Development 환경에서만 노출됩니다.

## 4. 프런트엔드 설치 및 실행

```powershell
Set-Location frontend
npm install
Copy-Item .env.example .env.local
npm run dev -- --host 127.0.0.1
```

`.env.local`에서 `VITE_API_BASE_URL`을 설정합니다. 예제 기본값은 `http://127.0.0.1:5080/api`이며 `.env.local`은 Git에서 제외됩니다. 실제 비밀정보나 API 키를 Vite의 `VITE_*` 변수에 넣지 마십시오. 해당 값은 브라우저 번들에 포함될 수 있습니다.

## 5. 프런트엔드 검사

```powershell
npm run build
npm run lint
```

`npm run build`는 Vue TypeScript 검사와 Vite 프로덕션 빌드를 수행합니다. ESLint/Oxlint 검사는 자동 수정 없이 실행합니다.

### npm 취약점 고정

2026-11-08 [npm advisory GHSA-g3hv-75wg-m249](https://github.com/advisories/GHSA-g3hv-75wg-m249)의 `minimatch` 취약점(패턴 파트 증폭을 통한 잠재적 서비스 거부)이 확인됐습니다. 영향 버전은 `>=7.0.0 <11.0.1`; 당시 lockfile 버전 `10.2.6`이 해당 범위였습니다. `minimatch` 자체는 직접 의존성이 아니라 `eslint@10.14.0 → @eslint/config-array@0.23.5` 및 `@vue/eslint-config-typescript@14.9.0 → @typescript-eslint/utils@8.72.1 → @typescript-eslint/typescript-estree@8.72.1`에서 들어왔습니다. `npm audit`은 같은 취약 경로를 따라 11개 moderate 항목을 보고했고, 취약 패키지가 연결된 직접 lint 개발 도구는 ESLint, Vue TypeScript ESLint 설정, eslint-plugin-vue였습니다. 영향 범위는 ESLint/Oxlint 개발 검사 도구 체인이며 프로덕션 dependencies에서는 발견되지 않았습니다.

현재 `package.json`의 npm override는 취약점 수정 범위에 포함되지 않는 `minimatch` 11.0.5 계열로 올립니다. 부모들의 `^10.2.2`~`^10.2.6` 범위를 넘어서는 메이저 override입니다. audit의 자동 수정 제안은 호환성 확인 없이 ESLint 9.39.5로 내리는 강제 변경이어서 따르지 않았습니다. 대신 현재 Vue ESLint 설정의 ESLint 9/10 peer 범위와 minimatch 11을 설치한 lockfile을 확인하고, npm install·audit·build·lint를 검증했습니다. 향후 ESLint/TypeScript lint 도구를 올릴 때 peer 범위 및 tree, audit, build, lint를 다시 확인하십시오. 경고를 숨기거나 감사 수준을 낮추기 위한 설정은 사용하지 않습니다.

## 6. 테스트 구성

- Domain 및 Application은 단위 테스트 프로젝트를 사용합니다.
- Infrastructure 통합 테스트는 임시 파일 기반의 실제 SQLite Provider와 SQLite Migration을 사용합니다.
- API 통합 테스트는 테스트 서버에서 Health Check, OpenAPI 및 DB readiness를 검증합니다.
- DPAPI 통합 테스트는 Windows 사용자 컨텍스트에서 실행합니다.

## 7. 문제 해결

- `dotnet restore`가 실패하면 .NET SDK 버전과 저장소 `NuGet.Config`의 패키지 피드 접근 상태를 확인합니다. 프록시 환경에서는 조직의 NuGet 프록시 설정이 필요할 수 있습니다.
- 프로젝트가 지원 Node 버전보다 낮다고 안내하면 Vue 생성기가 요구하는 Node.js 버전 이상으로 전환한 뒤 `npm install`을 다시 실행합니다.
- 프런트엔드 API 요청 주소가 맞지 않으면 `frontend/.env.local`의 `VITE_API_BASE_URL`을 확인하고 Vite 개발 서버를 재시작합니다.
- 포트가 이미 사용 중이면 API와 프런트엔드의 주소를 각각 변경하고 프런트엔드 환경 변수도 맞춥니다.
- 개발용 API는 루프백 주소에서만 실행합니다. 방화벽을 변경하거나 서비스를 외부 네트워크에 공개하지 않습니다.

## 8. 현재 범위

SQLite/EF Core 스키마, Repository 계약, 로컬 DPAPI 키 보호 및 암호화 기반만 구현되어 있습니다. 사용자 인증·업무 API·Memory 유스케이스·SQL Server Provider·Ollama 호출·외부 AI API·프런트엔드 업무 기능은 포함되지 않습니다. SQLite 키 회전 및 자동 키 백업도 아직 구현되지 않았습니다.
