import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the API runs on http://localhost:5080 (see SrNotification.Api launchSettings).
// Everything goes through this dev server, so the browser sees one origin and the sign-in cookie
// just works. The Host header is passed through unchanged, so the API builds its sign-in redirect
// URL as http://localhost:5173/signin-oidc — register that URL in Entra for local development.
const api = 'http://localhost:8080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': api,
      '/auth': api,
      '/signin-oidc': api,
      '/signout-callback-oidc': api,
    },
  },
  build: {
    outDir: 'dist',
  },
});
