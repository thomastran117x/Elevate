import { createFeatureSelector, createSelector } from '@ngrx/store';
import { SessionState } from './session.reducer';

export const selectSessionState = createFeatureSelector<SessionState>('session');

export const selectSession = createSelector(selectSessionState, (state) => state.session);
export const selectAccessToken = createSelector(
  selectSession,
  (session) => session?.AccessToken ?? null,
);
