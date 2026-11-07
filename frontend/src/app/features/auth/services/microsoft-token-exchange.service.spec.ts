import { TestBed } from '@angular/core/testing';

import { environment } from '@environments/environment';

import {
  MICROSOFT_SIGN_IN_SCOPE,
  MICROSOFT_TOKEN_EXCHANGE_FALLBACK_MESSAGE,
  MicrosoftTokenExchangeService,
} from './microsoft-token-exchange.service';

describe('MicrosoftTokenExchangeService', () => {
  let service: MicrosoftTokenExchangeService;
  let fetchSpy: jasmine.Spy<typeof fetch>;

  const respond = (status: number, body: string) =>
    fetchSpy.and.resolveTo(new Response(body, { status }));

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(MicrosoftTokenExchangeService);
    fetchSpy = spyOn(window, 'fetch');
  });

  it('redeems the code with the verifier and the sign-in callback', async () => {
    respond(200, JSON.stringify({ id_token: 'id-token' }));

    const token = await service.exchange('code', 'verifier');

    expect(token).toBe('id-token');
    const [url, init] = fetchSpy.calls.mostRecent().args;
    expect(url).toBe('https://login.microsoftonline.com/common/oauth2/v2.0/token');
    const body = new URLSearchParams(init!.body as string);
    expect(body.get('code')).toBe('code');
    expect(body.get('code_verifier')).toBe('verifier');
    expect(body.get('grant_type')).toBe('authorization_code');
    expect(body.get('redirect_uri')).toBe(`${environment.frontendUrl}/auth/microsoft`);
    expect(body.get('scope')).toBe(MICROSOFT_SIGN_IN_SCOPE);
  });

  it('asks for the scope it is given', async () => {
    respond(200, JSON.stringify({ id_token: 'id-token' }));

    await service.exchange('code', 'verifier', 'openid profile email');

    const body = new URLSearchParams(fetchSpy.calls.mostRecent().args[1]!.body as string);
    expect(body.get('scope')).toBe('openid profile email');
  });

  it('reports a refused code with the fallback message', async () => {
    respond(400, JSON.stringify({ error: 'invalid_grant' }));

    await expectAsync(service.exchange('code', 'verifier')).toBeRejectedWithError(
      MICROSOFT_TOKEN_EXCHANGE_FALLBACK_MESSAGE,
    );
  });

  it('reports an unavailable provider as a server error', async () => {
    respond(503, '');

    await expectAsync(service.exchange('code', 'verifier')).toBeRejected();
  });

  it('refuses a response without an ID token, or one that is not JSON', async () => {
    respond(200, JSON.stringify({ access_token: 'x' }));
    await expectAsync(service.exchange('code', 'verifier')).toBeRejectedWithError(
      MICROSOFT_TOKEN_EXCHANGE_FALLBACK_MESSAGE,
    );

    respond(200, 'not json');
    await expectAsync(service.exchange('code', 'verifier')).toBeRejectedWithError(
      MICROSOFT_TOKEN_EXCHANGE_FALLBACK_MESSAGE,
    );
  });

  it('reports a network failure', async () => {
    fetchSpy.and.rejectWith(new TypeError('offline'));

    await expectAsync(service.exchange('code', 'verifier')).toBeRejected();
  });
});
