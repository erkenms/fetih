import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// Comet.Web's address (Web:Urls in Comet.Web.config)
const backend = process.env.COMET_WEB_URL ?? 'http://127.0.0.1:5820'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': backend,
      '/assets': backend,
      '/hubs': { target: backend, ws: true },
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
