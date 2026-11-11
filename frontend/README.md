# 프런트엔드

Vue 3, TypeScript, Vite, Vue Router, Pinia 및 Vue I18n을 사용하는 별도 SPA입니다. 백엔드 프로젝트를 직접 참조하지 않고 REST API 서비스 계층을 통해 통신합니다.

```powershell
npm install
Copy-Item .env.example .env.local
npm run dev -- --host 127.0.0.1
```

`.env.local`에 API 기본 주소를 설정하며 실제 비밀정보는 저장하지 않습니다. 한국어와 영어 리소스는 `src/i18n/locales/`에 있습니다.

```powershell
npm run build
npm run lint
```
