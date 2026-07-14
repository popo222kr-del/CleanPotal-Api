import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 개발 시 /api 요청을 ASP.NET Core API(localhost:5001)로 프록시 → CORS 회피
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    host: true,           // 0.0.0.0 바인딩 → 같은 Wi-Fi의 핸드폰에서 PC IP로 접속 가능
    proxy: {
      '/api': {
        target: 'http://localhost:5001',
        changeOrigin: true,
      },
    },
  },
})
