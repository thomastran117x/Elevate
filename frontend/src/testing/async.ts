/**
 * Drains the microtask queue.
 *
 * Services that `await` something before issuing their HTTP call (bootstrapping
 * a CSRF token, for instance) have not reached the request yet when the spec
 * resumes. Await this before `httpMock.expectOne`.
 */
export function flushPromises(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
