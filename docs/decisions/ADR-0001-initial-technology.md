# ADR-0001: 초기 기술 구성

- 상태: 승인된 초기 기준
- 날짜: 2026-11-08

## 맥락

개인 PC에서 시작해 개인 서버·기업 내부 설치로 확장 가능한 로컬 우선 개인 AI 플랫폼이 필요합니다. 초기 비용을 낮추고 기존 개발 경험을 활용해야 합니다.

## 결정

- Backend: C# / ASP.NET Core 10
- Frontend: Vue 3 / TypeScript
- LLM Runtime: Ollama 초기 Provider
- 개인 DB: SQLite
- 기업 DB: MSSQL 후속 Provider
- 개발 IDE: Visual Studio Code
- 아키텍처: 모듈형 모놀리스

## 이유

- 사용자의 기존 C#/.NET 및 Vue 경험 활용
- 초기 개발 복잡도와 비용 절감
- Frontend/API, LLM Provider, DB Provider 교체 경계 확보
- 개인용과 기업용의 서로 다른 배포 요구 수용

## 결과 및 주의

- Vue에서 React로 변경하려면 Frontend 재작성은 필요하지만 API 계약은 유지할 수 있도록 함.
- SQLite와 MSSQL은 SQL/타입/마이그레이션 차이가 있으므로 별도 Provider 검증이 필요함.
- Ollama API 형식은 Infrastructure에 격리함.
- 기술 버전은 구현 시점의 공식 지원 및 호환성을 재확인함.
