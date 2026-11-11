# 10. 코딩 규칙

## C# / .NET

- Namespace, Type, Method, Public Property: PascalCase
- private field: `_camelCase`
- local variable, parameter: camelCase
- Interface: `I` 접두사
- 비동기 메서드: `Async` 접미사
- nullable reference types 활성화
- 공개 API DTO와 Domain Entity 분리
- CancellationToken을 비동기 경계까지 전달
- 의존성 주입 사용
- 정적 전역 상태와 Service Locator 지양
- 예외는 의미 있는 경계에서 처리하고 로그에 민감정보를 남기지 않음

## Vue / TypeScript

- Vue 3 Composition API 우선
- `<script setup lang="ts">` 사용
- 컴포넌트 파일은 PascalCase
- 변수·함수는 camelCase
- API 호출은 별도 API 모듈로 분리
- 화면 컴포넌트에 DB/LLM 구현 세부사항을 넣지 않음
- `any` 남용 금지
- API 응답 타입은 계약과 일치시킴

## DB

- DB 테이블·컬럼은 snake_case를 사용하고 Entity/Property 식별자는 PascalCase를 사용하며 양 Provider 매핑을 문서화
- PK/FK/인덱스 명명 규칙을 문서화
- 시간은 UTC 저장 원칙
- 모든 SQL은 매개변수화
- Migration과 설계 문서를 함께 변경

## 문서와 주석

- 문서·설명·주석은 한국어
- 식별자는 영어
- 주석은 코드가 무엇을 하는지 반복하기보다 왜 필요한지 설명
- 공개 인터페이스와 보안·데이터 경계에 설명 추가
- 미확정 설계는 확정된 것처럼 쓰지 않고 `검토 중`으로 표시

## Git

- 작은 단위로 커밋
- 비밀정보·DB 파일·실제 데이터·빌드 산출물 커밋 금지
- `.gitignore`를 초기 생성
- 커밋 메시지는 `feat:`, `fix:`, `docs:`, `test:`, `refactor:` 등 일관된 접두사 사용
