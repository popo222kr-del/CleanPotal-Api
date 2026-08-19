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
    },
  },
})
