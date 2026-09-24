import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 개발 시 /api 요청을 ASP.NET Core API(localhost:5001)로 프록시 → CORS 회피
export default defineConfig({
  plugins: [react()],
  build: {
    // 백엔드(ASP.NET Core)가 프론트 정적 파일을 직접 서빙하는 단일 배포 구조.
    // npm run build 결과가 바로 API 프로젝트의 wwwroot로 나가서 dotnet publish 시 같이 담긴다.
    outDir: '../src/CleanPotal.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    host: true,           // 0.0.0.0 바인딩 → 같은 Wi-Fi의 핸드폰에서 PC IP로 접속 가능
    allowedHosts: true,   // cloudflared/ngrok 등 터널 도메인 접속 허용 (개발 테스트용)
    proxy: {
      '/api': {
        target: 'http://localhost:5001',
        changeOrigin: true,
      },
      // MES는 localhost에만 두고 CleanPotal 개발 서버를 통해 전달한다.
      // 브라우저에서는 같은 origin이므로 LAN IP 접속에서도 iframe·쿠키·WebSocket이 정상 동작한다.
      //
      // changeOrigin 은 반드시 false 다. true 로 두면 Host 헤더가 localhost:5206 으로 바뀌어
      // MES(ASP.NET Core)가 만드는 절대 URL(로그인 리다이렉트 Location 등)이
      // http://localhost:5206/... 으로 나간다. 그러면 iframe 이 포털 origin 을 벗어나
      // 방금 심은 MES 세션 쿠키를 못 보내고, 내부 주소까지 브라우저에 드러난다.
      // xfwd 로 X-Forwarded-* 를 붙여 MES 가 원래 요청 주소를 알 수 있게 한다.
      '/mes-runtime': {
        target: 'http://localhost:5206',
        changeOrigin: false,
        xfwd: true,
        ws: true,
        // MES 는 이 헤더를 포털 프록시가 넣은 신뢰 값으로 쓴다. 브라우저가 보낸 같은 이름은 지운다.
        configure: proxy => {
          proxy.on('proxyReq', proxyReq => proxyReq.removeHeader('x-cleanpotal-portal-endpoint'));
        },
      },
    },
  },
})
