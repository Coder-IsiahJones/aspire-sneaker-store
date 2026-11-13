import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// apphost.cs calls WithReference(api) on this app too, so Aspire sets API_HTTP here
// the same way it does for web/.
const apiUrl = process.env.API_HTTP

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: apiUrl ? { '/api': { target: apiUrl, changeOrigin: true } } : undefined,
  },
})
