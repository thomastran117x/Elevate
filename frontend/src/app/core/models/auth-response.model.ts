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
