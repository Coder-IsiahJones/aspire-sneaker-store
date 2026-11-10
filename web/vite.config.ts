import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

// apphost.cs calls WithReference(api) on this app, so Aspire sets API_HTTP
// to wherever the API is listening on this run.
const apiUrl = process.env.API_HTTP

export default defineConfig({
  plugins: [vue()],
  server: {
    proxy: apiUrl ? { '/api': { target: apiUrl, changeOrigin: true } } : undefined,
  },
})
