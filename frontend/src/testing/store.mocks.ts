import { Action } from '@ngrx/store';
import { MockStore, provideMockStore } from '@ngrx/store/testing';

import { TestProvider } from './http-testing';

import { User } from '../app/core/stores/user.model';
import { Session } from '../app/core/stores/session.model';
import { selectUser } from '../app/core/stores/user.selectors';
import { selectAccessToken, selectSession } from '../app/core/stores/session.selectors';

export interface TestStoreState {
  user?: User | null;
  session?: Session | null;
}

/**
 * A `MockStore` with the three selectors the app actually reads pre-overridden.
 * `dispatch` is a no-op you can spy on, so specs assert intent rather than
 * reducer output.
 */
export function provideTestStore(state: TestStoreState = {}): TestProvider[] {
  const user = state.user ?? null;
  const session = state.session ?? null;

  return [
    provideMockStore({
      selectors: [
        { selector: selectUser, value: user },
        { selector: selectSession, value: session },
        { selector: selectAccessToken, value: session?.AccessToken ?? null },
      ],
    }),
  ];
}

/** Spies on `store.dispatch` and returns the recorded actions. */
export function dispatchSpy(store: MockStore): jasmine.Spy<(action: Action) => void> {
  return spyOn(store, 'dispatch');
}

export { MockStore };
