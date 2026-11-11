# Copilot Agent 첫 번째 작업 프롬프트

아래 내용을 VS Code의 Copilot Agent Mode에 그대로 전달합니다.

---

현재 열려 있는 폴더는 `P.P. AI - LUMORA` 프로젝트의 루트입니다.

먼저 `AGENTS.md`, `README.md`, `docs/00_PROJECT_OVERVIEW.md`, `docs/01_REQUIREMENTS.md`, `docs/04_ARCHITECTURE.md`, `docs/05_DATABASE_DESIGN.md`, `docs/06_LLM_PROVIDER_DESIGN.md`, `docs/08_SECURITY_DESIGN.md`, `docs/09_PERFORMANCE_GUIDE.md`를 읽으십시오.

이번 작업은 **개발 환경 검증과 빈 저장소의 기반 구성까지만** 수행합니다. 아직 채팅 기능, DB 스키마, LLM 호출 기능을 구현하지 마십시오.

## 사용자 환경

- Windows 64-bit
- .NET SDK 10.0.403 설치 완료
- Git 2.56.0.4 설치 완료
- Node.js 26.13.0 설치 완료
- Ollama for Windows 설치 완료
- NVIDIA GeForce RTX 3060 12GB
- Visual Studio Code 사용
- Visual Studio IDE는 필수 도구로 사용하지 않음

## 작업 1. 환경 확인

PowerShell에서 다음 명령의 실행을 안내하거나 실행 가능한 터미널에서 확인하십시오.

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

명령 실행 권한이 없거나 결과를 확인할 수 없다면 실행했다고 주장하지 말고 사용자에게 결과를 요청하십시오.

## 작업 2. VS Code 확장 확인

다음 확장이 필요한지 확인하고 설치 여부를 보고하십시오.

- C# Dev Kit (Microsoft)
- Vue - Official (Vue Language Features)
- ESLint
- Prettier - Code formatter

확장을 임의로 대량 설치하지 말고 위 목록만 확인하십시오. Copilot Agent 사용 가능 여부도 확인하십시오.

## 작업 3. Git 및 기본 파일

현재 폴더에 기존 파일이 있는지 먼저 확인하십시오. 기존 파일을 삭제하거나 덮어쓰지 마십시오.

필요한 경우 다음 파일을 생성하십시오.

- `.gitignore`
- `.editorconfig`
- `.gitattributes`
- `global.json` (설치된 SDK 10.0.403을 기준으로 고정할 필요와 부작용을 먼저 설명한 후 생성)
- `Directory.Build.props` (nullable, implicit usings 등 최소 공통 설정을 검토)
- `Directory.Packages.props`는 패키지를 실제 추가할 때 생성

`.gitignore`에는 최소한 다음을 포함하십시오.

- `bin/`, `obj/`
- `node_modules/`, `dist/`
- `.vs/`, `.vscode/*.local.json`
- 사용자별 설정 파일
- SQLite DB 파일 및 WAL/SHM 파일
- 로그, 임시 파일, 백업 파일, 키 파일, 비밀정보 파일

`.vscode/extensions.json`을 생성한다면 권장 확장만 포함하고 필수 설치로 강제하지 마십시오.

## 작업 4. 솔루션 구조 제안

아직 모든 프로젝트를 생성하지 말고, 문서에 정의된 구조가 실제 개발에 적절한지 검토하십시오.

```text
src/
  Lumora.Api/
  Lumora.Application/
  Lumora.Domain/
  Lumora.Infrastructure/
  Lumora.Contracts/
frontend/
tests/
docs/
```

불필요한 프로젝트 분리를 피하고, 각 프로젝트의 책임과 참조 방향을 간단한 표로 보고하십시오. 사용자 승인 전에는 프로젝트를 무더기로 생성하지 마십시오.

## 작업 5. Ollama 상태

- Ollama 서비스가 실행 중인지 확인합니다.
- `ollama list`로 설치된 모델 목록만 확인합니다.
- 모델이 없다면 임의 다운로드하지 않습니다.
- RTX 3060 12GB에 맞는 후보를 제안하되, 모델 라이선스·양자화·컨텍스트·메모리 요구사항을 검증하기 전에는 설치하지 않습니다.
- Ollama가 외부 네트워크에 노출되어 있는지 확인할 수 있는 안전한 점검 방법을 제안합니다. 방화벽을 임의 변경하지 않습니다.

## 보안 절대 조건

- 외부 LLM API를 추가하지 마십시오.
- 실제 개인정보를 만들거나 입력하지 마십시오.
- 비밀정보를 파일에 저장하지 마십시오.
- 네트워크로 사용자 데이터를 전송하는 코드를 만들지 마십시오.
- 모델을 자동 다운로드하지 마십시오.

## 완료 보고 형식

1. 확인한 환경 버전과 실제 명령 결과
2. 확인한 VS Code 확장
3. 생성·수정한 파일 목록
4. Git 상태
5. Ollama 실행 상태와 모델 목록
6. 제안하는 솔루션 구조
7. 발견한 문제와 미확인 사항
8. 다음 작업 제안

실행하지 않은 명령이나 테스트를 성공했다고 보고하지 마십시오. 이번 작업이 끝나면 멈추고 사용자 승인을 기다리십시오.
