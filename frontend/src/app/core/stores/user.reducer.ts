import { createReducer, on } from '@ngrx/store';
import { setUser, clearUser, updateAccessToken } from './user.actions';
import { User } from './user.model';

export interface UserState {
  user: User | null;
}

export const initialState: UserState = {
  user: null,
};

export const userReducer = createReducer(
  initialState,
  on(setUser, (state, { user }) => ({ ...state, user })),
  on(clearUser, () => ({ user: null })),
  on(updateAccessToken, (state, { accessToken }) => ({
    ...state,
    user: state.user ? { ...state.user, Token: accessToken } : null,
  })),
);
