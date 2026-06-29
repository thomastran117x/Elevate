import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';
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

    Readable.fromWeb(upstream.body as any).pipe(res);
  } catch (error) {
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

if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 3090;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
    console.log(`Proxying /api requests to ${apiProxyTarget}`);
  });
}

export const reqHandler = createNodeRequestHandler(app);
