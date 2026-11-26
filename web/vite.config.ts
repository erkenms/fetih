import react from '@vitejs/plugin-react'
import type { ProxyOptions } from 'vite'
import { defineConfig } from 'vitest/config'

// Comet.Web's address (Web:Urls in Comet.Web.config), or a remote web:
//   FETIH_BACKEND=https://test.<domain> npm run dev
// The remote web sets Secure cookies (Web:SecureCookies), which a plain
// http://localhost page may not keep (Safari refuses them), so the proxy drops
// Domain and Secure from Set-Cookie. The server's own cookie settings stay as
// they are; SameSite=Strict is fine because the page and /api share an origin.
const backend = process.env.FETIH_BACKEND ?? process.env.COMET_WEB_URL ?? 'http://127.0.0.1:5820'

const proxied: ProxyOptions = {
  target: backend,
  changeOrigin: true,
  secure: true,
  cookieDomainRewrite: '',
  configure: (proxy) => {
    proxy.on('proxyRes', (res) => {
      const cookies = res.headers['set-cookie']
      if (cookies) {
        res.headers['set-cookie'] = cookies.map((c) => c.replace(/;\s*secure/gi, ''))
      }
    })
  },
}

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': proxied,
      '/assets': proxied,
      '/hubs': { ...proxied, ws: true },
    },
  },
  build: {
    // /assets is the client art Comet.Web serves, so the bundle goes elsewhere
    assetsDir: 'static',
    chunkSizeWarningLimit: 1000,
  },
  test: {
    include: ['src/**/*.test.ts'],
  },
})
