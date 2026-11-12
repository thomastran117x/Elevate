# Provider-link conflicts

| Alert | Severity | Owner |
| --- | --- | --- |
| [`AuthProviderLinkConflicts`](#authproviderlinkconflicts) | ticket | Security lead |

Owners and escalation are defined in [ownership](../ownership.md).

## AuthProviderLinkConflicts

**Meaning:** more than 5 Google or Microsoft links per hour, and more than 3x the hourly p99, were refused. Every one was already refused, so nothing was linked. The pattern matters because it is how account takeover through identity providers is attempted:
- an email address that was reassigned at its provider;
- a Microsoft tenant that does not verify addresses;
- an attacker's own identity offered for someone else's email.

### Triage

1. In the **Provider linking** row, split the conflicts by `method` (`google`, `microsoft`) and `reason`:

   | Reason | Usually means |
   | --- | --- |
   | `email_bound_to_other_provider` | The address belongs to an account linked to a different identity of that provider: a reassigned address, or someone using another person's email. |
   | `email_unverified` | Microsoft did not verify the address. A tenant is asserting an email it does not own. |
   | `provider_bound_to_other_account` | The identity is already linked elsewhere. Someone is trying to attach it to a second account. |
   | `provider_already_connected` | The account already links a different identity of that provider. Usually a user with two accounts. |

2. A burst of `email_unverified` from `microsoft` points at one tenant. Correlate by `correlation=` with ingress logs to see the tenant issuer if your ingress records it.

   ```sh
   docker compose logs --since 2h backend \
     | grep 'Auth security event auth.provider_link.conflict' \
     | grep -oE 'reason=[^ ]* method=[^ ]* .* target=[^ ]* source=[^ ]*' | sort | uniq -c | sort -rn | head
   ```

3. Check whether any **successful** links (`auth.provider.linked`) followed for the same `target` hashes. A success after a run of conflicts is the case to escalate as a possible takeover.

### Contain

- For a suspected takeover through a confirmed link, ask the owner to unlink the provider from **Account > Security**. That revokes every session (reason `provider_unlinked`) and is recorded in their security history. If the owner cannot be reached, disable the account (`PATCH /api/admin/users/{id}/status`).
- If one Microsoft tenant is responsible, block its issuer at the ingress or report it to Microsoft. Unverified Microsoft addresses are never linked automatically.

### Verify and close

Record the reason and method breakdown, the target hashes involved, any successful link that followed and how it was handled, and any tenant or network blocked.

## Rehearse

- **Alert logic:** the promtool cases `provider link conflicts ...` in `docker/monitoring/prometheus/tests/auth-alerts.test.yml`.
- **End to end:** provider sign-in needs a real Google or Microsoft tenant, so the drill does not cover it. The integration suite reproduces each conflict reason against the running API with a fake provider. Run `dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-integration-tests --filter "FullyQualifiedName~ProviderLinkEndpointsTests"`, which needs Docker on x64 Linux; see [testing](../../TESTING.md). In a non-production deployment with test tenants, sign in with a Microsoft account whose email matches an existing local account linked to Google, and watch the **Provider linking** row.
