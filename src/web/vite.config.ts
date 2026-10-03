import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  // Where the API runs, from the API_URL environment variable or a .env / .env.local file in this
  // folder (loadEnv reads both):
  //   - http://localhost:5080 (default) when you start it outside Docker (`dotnet run`, IDE; see
  //     SrNotification.Api launchSettings),
  //   - http://localhost:8080 when it runs in Docker (`docker compose up`; see docker-compose.yml).
  // Everything goes through this dev server, so the browser sees one origin and the sign-in cookie
  // just works. The Host header is passed through unchanged, so the API builds its sign-in redirect
  // URL as http://localhost:5173/signin-oidc — register that URL in Entra for local development.
  const env = loadEnv(mode, '.', '');
  const api = env.API_URL || 'http://localhost:5080';

  return {
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
  };
});
