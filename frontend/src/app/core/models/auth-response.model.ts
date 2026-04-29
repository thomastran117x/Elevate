export interface AuthResponse {
  Username: string;
  Token: string;
  AccessToken?: string;
  RefreshToken?: string;
  SessionBindingToken?: string;
  Avatar: string;
  Usertype: string;
  Id: number;
}
