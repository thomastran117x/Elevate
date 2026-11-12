# Authorization probing and mass authorization denial

| Alert | Severity | Owner |
| --- | --- | --- |
| [`AuthMassAuthorizationDenial`](#authmassauthorizationdenial) | page | Auth on-call |
| [`AuthAuthorizationProbing`](#authauthorizationprobing) | ticket | Security lead |

Owners and escalation are defined in [ownership](../ownership.md). Open an [incident record](../incident-record.md) when you acknowledge a page.

Every 403 is recorded as `authz.denied` with a `reason`:
- `policy` is a role policy such as `AdminOnly`;
- `mfa_required` is a `[RequireMfa]` route without a fresh step-up proof;
- `resource_access` is a service refusing ownership, staff role, or account state.

The event's `route=` is the method and route template, never the path. `user=` is the caller. 401s are not recorded.

## AuthMassAuthorizationDenial

**Meaning:** `policy` plus `mfa_required` denials jumped above 1/s and 3x their p99 for 5 minutes. This usually has one of two causes:
1. **A regression:** a release changed a policy, a role mapping, or the step-up store, and legitimate users are being refused.
2. **Probing at scale:** someone is calling admin or step-up-protected routes with ordinary accounts.

### Triage

1. Did a deployment happen just before the rise? If so, treat it as a regression first.
2. Group the denials by route and reason:

   ```sh
   docker compose logs --since 30m backend \
     | grep 'Auth security event authz.denied' \
     | grep -oE 'reason=[^ ]* .* route=[^ ]* [^ ]*' | sed -E 's/ (method|flow|dimension|factor|change|user|actor|session|target|source|client|device)=[^ ]*//g' \
     | sort | uniq -c | sort -rn | head -20
   ```

   - **Many users denied on a route they normally use:** a regression. Compare with the reviewed public surface and the role/ownership matrix; see [API authorization](../../API.md).
   - **Mostly `mfa_required` across users:** check Redis, because step-up proofs live there. When Redis cannot return a proof, `[RequireMfa]` treats it as missing.
   - **One or a few `user=` ids across many admin routes:** probing.

### Contain

- **Regression:** roll back the release. The Auth Contract Gate in CI should have caught a classification change, so add a regression test for whatever it missed.
- **Step-up store outage:** restore Redis. Users can step up again once it is back.
- **Probing:** disable the probing accounts (`PATCH /api/admin/users/{id}/status`) and block their sources at the ingress.

### Verify and close

Record the cause (regression, outage, or probing), the affected routes, the user ids involved for probing, and the fix or rollback.

## AuthAuthorizationProbing

**Meaning:** `resource_access` denials exceed 0.2/s and 3x their p99 for 10 minutes. A caller is likely walking resource ids, such as clubs, events, or registrations, they do not own. Every request was refused, so this is a ticket.

### Triage

```sh
docker compose logs --since 1h backend \
  | grep 'Auth security event authz.denied' | grep 'reason=resource_access' \
  | grep -oE 'user=[0-9]* .* route=[^ ]* [^ ]*' | awk '{print $1, $(NF-1), $NF}' | sort | uniq -c | sort -rn | head
```

One `user=` on one route template with a high count is enumeration. Many users on one route after a release is a regression; handle it as in [`AuthMassAuthorizationDenial`](#authmassauthorizationdenial).

### Contain

Disable the enumerating account and block its source at the ingress. If the enumeration reveals that a route leaks existence (403 versus 404) for private resources, open a security issue to make it answer 404.

### Verify and close

Record the user ids, the routes, the duration, and any follow-up issue.

## Rehearse

- **Alert logic:** the promtool cases `resource access denials ...` and `policy and MFA-required denials ...` in `docker/monitoring/prometheus/tests/auth-alerts.test.yml`.
- **End to end:** `auth-drill --scenario telemetry` calls a `[RequireMfa]` route without a step-up proof, which produces an `mfa_required` denial, and checks that `authz.denied` was counted. See [drills](../drills.md). The integration suite's `AuthorizationMatrixEndpointsTests` exercises policy and resource denials for every global and club role.
