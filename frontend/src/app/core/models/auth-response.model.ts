export interface AuthenticatedSessionResponse {
  AccessToken: string;
  ExpiresAtUtc: string;
  RefreshToken?: string;
  SessionBindingToken?: string;
  ReturnPath?: string | null;
}

export interface CurrentUserResponse {
  Id: number;
  Email: string;
  Username: string;
  Name?: string | null;
  Avatar?: string | null;
  Usertype: string;
}

type UnknownRecord = Record<string, unknown>;

function asRecord(value: unknown): UnknownRecord | null {
  return typeof value === 'object' && value !== null ? (value as UnknownRecord) : null;
}

function readString(source: UnknownRecord, ...keys: string[]): string | undefined {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'string') {
      return value;
    }
  }

  return undefined;
}

function readNullableString(source: UnknownRecord, ...keys: string[]): string | null | undefined {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'string' || value === null) {
      return value;
    }
  }

  return undefined;
}

function readNumber(source: UnknownRecord, ...keys: string[]): number | undefined {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }

    if (typeof value === 'string') {
      const parsed = Number(value);
      if (Number.isFinite(parsed)) {
        return parsed;
      }
    }
  }

  return undefined;
}

export function normalizeAuthenticatedSessionResponse(
  value: unknown,
): AuthenticatedSessionResponse | null {
  const source = asRecord(value);
  if (!source) {
    return null;
  }

  const accessToken = readString(source, 'AccessToken', 'accessToken');
  const expiresAtUtc = readString(source, 'ExpiresAtUtc', 'expiresAtUtc');

  if (!accessToken || !expiresAtUtc) {
    return null;
  }

  const refreshToken = readString(source, 'RefreshToken', 'refreshToken');
  const sessionBindingToken = readString(source, 'SessionBindingToken', 'sessionBindingToken');
  const returnPath = readNullableString(source, 'ReturnPath', 'returnPath');

  return {
    AccessToken: accessToken,
    ExpiresAtUtc: expiresAtUtc,
    RefreshToken: refreshToken,
    SessionBindingToken: sessionBindingToken,
    ReturnPath: returnPath ?? undefined,
  };
}

export function normalizeCurrentUserResponse(value: unknown): CurrentUserResponse | null {
  const source = asRecord(value);
  if (!source) {
    return null;
  }

  const id = readNumber(source, 'Id', 'id');
  const email = readString(source, 'Email', 'email');
  const username = readString(source, 'Username', 'username');
  const usertype = readString(source, 'Usertype', 'usertype');

  if (id === undefined || !email || !username || !usertype) {
    return null;
  }

  const name = readNullableString(source, 'Name', 'name');
  const avatar = readNullableString(source, 'Avatar', 'avatar');

  return {
    Id: id,
    Email: email,
    Username: username,
    Name: name ?? undefined,
    Avatar: avatar ?? undefined,
    Usertype: usertype,
  };
}
