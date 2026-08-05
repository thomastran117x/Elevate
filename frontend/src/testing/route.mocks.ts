import { ActivatedRoute, convertToParamMap, ParamMap, Params } from '@angular/router';
import { BehaviorSubject } from 'rxjs';

export interface FakeRouteConfig {
  params?: Params;
  queryParams?: Params;
}

export interface FakeActivatedRoute {
  route: ActivatedRoute;
  /** Pushes a new query-param map through both the observable and the snapshot. */
  setQueryParams(queryParams: Params): void;
  setParams(params: Params): void;
}

/**
 * An `ActivatedRoute` double whose `paramMap`/`queryParamMap` observables and
 * `snapshot` stay in sync, so components that read both see the same values.
 */
export function fakeActivatedRoute(config: FakeRouteConfig = {}): FakeActivatedRoute {
  const paramMap$ = new BehaviorSubject<ParamMap>(convertToParamMap(config.params ?? {}));
  const queryParamMap$ = new BehaviorSubject<ParamMap>(convertToParamMap(config.queryParams ?? {}));
  const params$ = new BehaviorSubject<Params>(config.params ?? {});
  const queryParams$ = new BehaviorSubject<Params>(config.queryParams ?? {});

  const snapshot = {
    paramMap: paramMap$.value,
    queryParamMap: queryParamMap$.value,
    params: params$.value,
    queryParams: queryParams$.value,
  };

  const route = {
    paramMap: paramMap$.asObservable(),
    queryParamMap: queryParamMap$.asObservable(),
    params: params$.asObservable(),
    queryParams: queryParams$.asObservable(),
    snapshot,
  } as unknown as ActivatedRoute;

  return {
    route,
    setQueryParams(queryParams: Params) {
      const map = convertToParamMap(queryParams);
      snapshot.queryParamMap = map;
      snapshot.queryParams = queryParams;
      queryParamMap$.next(map);
      queryParams$.next(queryParams);
    },
    setParams(params: Params) {
      const map = convertToParamMap(params);
      snapshot.paramMap = map;
      snapshot.params = params;
      paramMap$.next(map);
      params$.next(params);
    },
  };
}
