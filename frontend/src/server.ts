import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { request as httpRequest } from 'node:http';
import { join } from 'node:path';
import type { Duplex } from 'node:stream';
import { Readable } from 'node:stream';

const browserDistFolder = join(import.meta.dirname, '../browser');
const apiProxyTarget = process.env['API_PROXY_TARGET'] || 'http://localhost:8090';
const hopByHopHeaders = new Set([
  'connection',
  'content-length',
  'keep-alive',
  'proxy-authenticate',
  'proxy-authorization',
  'te',
  'trailer',
  'transfer-encoding',
  'upgrade',
]);

const app = express();
const angularApp = new AngularNodeAppEngine();

async function readRequestBody(req: express.Request): Promise<Uint8Array | undefined> {
  if (req.method === 'GET' || req.method === 'HEAD') {
    return undefined;
  }

  const chunks: Buffer[] = [];

  for await (const chunk of req) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }

  if (chunks.length === 0) {
    return undefined;
  }

  return new Uint8Array(Buffer.concat(chunks));
}

app.use('/api', async (req, res) => {
  const targetUrl = new URL(req.originalUrl, apiProxyTarget);
  const headers = new Headers();
  const controller = new AbortController();
  const abortUpstream = () => controller.abort();
  res.once('close', abortUpstream);
  res.once('finish', () => res.removeListener('close', abortUpstream));

  for (const [key, value] of Object.entries(req.headers)) {
    if (!value || hopByHopHeaders.has(key.toLowerCase()) || key.toLowerCase() === 'host') {
      continue;
    }

    headers.set(key, Array.isArray(value) ? value.join(', ') : value);
  }

  try {
    const body = await readRequestBody(req);
    const upstream = await fetch(targetUrl, {
      method: req.method,
      headers,
      body: body as unknown as BodyInit | undefined,
      redirect: 'manual',
      signal: controller.signal,
    });

    const setCookies = upstream.headers.getSetCookie();
    if (setCookies.length > 0) {
      res.setHeader('set-cookie', setCookies);
    }

    upstream.headers.forEach((value, key) => {
      if (key.toLowerCase() === 'set-cookie' || hopByHopHeaders.has(key.toLowerCase())) {
        return;
      }

      res.setHeader(key, value);
    });

    res.status(upstream.status);

    if (!upstream.body) {
      res.end();
      return;
    }

    const upstreamBody = Readable.fromWeb(upstream.body as any);
    upstreamBody.once('error', (error) => {
      if (!controller.signal.aborted && !res.destroyed) {
        res.destroy(error);
      }
    });
    res.once('close', () => upstreamBody.destroy());
    upstreamBody.pipe(res);
  } catch (error) {
    if (controller.signal.aborted || res.destroyed) {
      return;
    }
    const message = error instanceof Error ? error.message : 'Unknown proxy error';
    res.status(502).json({
      success: false,
      message: `API proxy request failed: ${message}`,
    });
  }
});

app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then((response) => (response ? writeResponseToNodeResponse(response, res) : next()))
    .catch(next);
});

/**
 * Tunnels a WebSocket upgrade through to the API.
 *
 * The `/api` handler above cannot carry this: it proxies with `fetch`, and `upgrade` and
 * `connection` are stripped as hop-by-hop headers. Without this, SignalR would fall back to
 * long polling whenever the browser reaches the backend through this server (Docker and
 * Kubernetes both do).
 */
function proxyWebSocketUpgrade(req: express.Request, socket: Duplex, head: Buffer): void {
  const target = new URL(req.url ?? '/', apiProxyTarget);

  const upstream = httpRequest({
    protocol: target.protocol,
    hostname: target.hostname,
    port: target.port,
    path: target.pathname + target.search,
    method: 'GET',
    headers: { ...req.headers, host: target.host },
  });

  const destroyBoth = (): void => {
    socket.destroy();
    upstream.destroy();
  };

  upstream.on('upgrade', (upstreamRes, upstreamSocket, upstreamHead) => {
    const headers = Object.entries(upstreamRes.headers)
      .map(([key, value]) => `${key}: ${Array.isArray(value) ? value.join(', ') : value}`)
      .join('\r\n');

    socket.write(
      `HTTP/1.1 101 ${upstreamRes.statusMessage ?? 'Switching Protocols'}\r\n${headers}\r\n\r\n`,
    );

    if (upstreamHead?.length) upstreamSocket.unshift(upstreamHead);

    upstreamSocket.on('error', destroyBoth);
    socket.on('error', destroyBoth);
    upstreamSocket.pipe(socket).pipe(upstreamSocket);
  });

  // The backend answered with a normal response, i.e. it refused the upgrade.
  upstream.on('response', destroyBoth);
  upstream.on('error', destroyBoth);
  socket.on('error', destroyBoth);

  // Bytes already read past the request headers belong to the client stream; put them
  // back so the pipe set up on upgrade forwards them.
  if (head?.length) socket.unshift(head);
  upstream.end();
}

if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 3090;
  const server = app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
    console.log(`Proxying /api requests to ${apiProxyTarget}`);
  });

  server.on('upgrade', (req, socket, head) => {
    if (!req.url?.startsWith('/api/')) {
      socket.destroy();
      return;
    }

    proxyWebSocketUpgrade(req as express.Request, socket, head);
  });
}

export const reqHandler = createNodeRequestHandler(app);
