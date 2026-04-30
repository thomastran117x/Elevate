import { createReducer, on } from '@ngrx/store';
import { clearSession, setSession, updateSession } from './session.actions';
import { Session } from './session.model';

export interface SessionState {
  session: Session | null;
}

export const initialState: SessionState = {
  session: null,
};

export const sessionReducer = createReducer(
  initialState,
  on(setSession, (state, { session }) => ({ ...state, session })),
  on(clearSession, () => ({ session: null })),
  on(updateSession, (state, { accessToken, expiresAtUtc }) => ({
    ...state,
    session: state.session
      ? { ...state.session, AccessToken: accessToken, ExpiresAtUtc: expiresAtUtc }
      : { AccessToken: accessToken, ExpiresAtUtc: expiresAtUtc },
  })),
);
