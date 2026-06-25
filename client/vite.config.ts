import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 개발 시 /api 요청을 ASP.NET Core API(localhost:5001)로 프록시 → CORS 회피
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5001',
        changeOrigin: true,
      },
    },
  },
})
