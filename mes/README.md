# ProductionManagement MES

CleanPotal의 `/mes` 화면에 포함되는 Blazor Server MES입니다.

## 데이터 위치

- DB: `src/ProductionManagement.Web/App_Data/Production.db`
- 첨부문서: `src/ProductionManagement.Web/App_Data/Documents/`

`App_Data`는 운영 데이터이므로 Git에서 제외됩니다. 바탕화면의 기존 운영 DB와 성적서 파일을 이 위치로 복사해 사용합니다.
운영 DB를 사용하므로 개발용 샘플 데이터 시드는 비활성화되어 있습니다.

## 실행

저장소 루트의 `start-dev.bat`을 실행하면 CleanPotal API, React 화면, MES, 터널이 함께 실행됩니다.
MES만 실행하려면 이 폴더의 `start-mes.bat`을 실행합니다.

MES는 운영 데이터가 사내망에 직접 노출되지 않도록 **`localhost:5206`에만 바인딩**합니다.
브라우저는 언제나 포털과 같은 주소인 `/mes-runtime`으로만 MES에 닿습니다.

## 브라우저 → MES 경로

```
브라우저 ──▶ 포털(같은 origin)/mes-runtime ──▶ localhost:5206 (MES)
                     │
                     ├─ 개발: Vite dev 서버가 전달 (client/vite.config.ts)
                     └─ 운영: CleanPotal.Api 의 리버스 프록시가 전달 (Mes:RuntimeUrl)
```

**운영에도 프록시가 반드시 있어야 합니다.** 없으면 `/mes-runtime` 요청이 SPA fallback(`index.html`)으로
떨어져 iframe 안에 포털이 다시 열리고, 세션 교환도 "HTML 200"이라 성공처럼 보입니다.

프록시 설정에서 주의할 점 두 가지:

- **원래 Host를 MES까지 전달해야 합니다.** Vite는 `changeOrigin: false` + `xfwd: true`,
  운영 프록시는 `X-Forwarded-Host`/`X-Forwarded-Proto`를 붙입니다. MES는 `UseForwardedHeaders`로
  이 값을 읽어 절대 URL을 만듭니다. 이게 빠지면 로그인 리다이렉트 `Location`이
  `http://localhost:5206/...`으로 나가 iframe이 포털 origin을 벗어나고, 방금 심은 세션 쿠키가 끊깁니다.
- **WebSocket을 통과시켜야 합니다.** Blazor Server는 화면 조작을 전부 WebSocket으로 주고받습니다.

## 인증

MES는 별도 아이디·비밀번호를 받지 않습니다. 포털 React 화면이 현재 JWT를 CleanPotal API(`/api/auth/me`)로
검증하고 MES 쿠키 세션(`.CleanPotal.MES`, 경로 `/mes-runtime`, 12시간)으로 교환합니다.
MES 감사 로그의 사용자명에는 CleanPotal 아이디가 기록됩니다.

인증이 없는 요청은 두 갈래로 나뉩니다.

| 요청 종류 | 응답 |
|---|---|
| 화면(iframe·주소창) | `/mes-runtime/login` 안내 페이지 — "CleanPotal에서 접속해 주세요" |
| fetch·Blazor 등 비화면 | `401` — 포털이 이를 보고 SSO를 다시 태웁니다 |

## 데스크톱 앱과 다른 점

성적서 **자동 채우기**는 Excel COM(Windows 전용)이라 서버에서 돌릴 수 없습니다.
웹의 `성적서 조회` 화면은 **등록·조회·다운로드만** 합니다(`NoOpCertificateExcelFiller`).
자동 채우기가 필요하면 ClosedXML 서버 구현이나 별도 Windows 워커로 대체해야 합니다.
