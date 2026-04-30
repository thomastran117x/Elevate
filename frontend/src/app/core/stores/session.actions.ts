import { createAction, props } from '@ngrx/store';
import { Session } from './session.model';

export const setSession = createAction('[Session] Set Session', props<{ session: Session }>());
export const clearSession = createAction('[Session] Clear Session');
export const updateSession = createAction(
  '[Session] Update Session',
  props<{ accessToken: string; expiresAtUtc: string }>(),
);
