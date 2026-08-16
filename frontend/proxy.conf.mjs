/**
 * Dev-server proxy for `ng serve`.
 *
 * The Express proxy in `src/server.ts` only runs when the built SSR server calls
 * `app.listen()`. Under `ng serve` Angular imports the exported request handler instead, so
 * that file's `server.on('upgrade')` hook never attaches and a WebSocket handshake to
 * `/api/hubs/...` is left hanging — which strands the realtime hub in "connecting".
 *
 * `ws: true` gives the dev server its own upgrade handling, so the SignalR hub works the same
 * way in development as it does behind the built server.
 */
const target = process.env['API_PROXY_TARGET'] || 'http://localhost:8090';

export default {
  '/api': {
    target,
    secure: false,
    changeOrigin: true,
    ws: true,
  },
};
