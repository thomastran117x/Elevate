# Authorization

This guide is the permission matrix for the API: who may do what, how each endpoint is checked, and how a new endpoint declares and tests its permissions. Authentication, sessions and step-up are described in [API](API.md#authorization) and [architecture](ARCHITECTURE.md).

## Layers

A request passes four layers, in order:

1. **Authentication by default.** The fallback policy requires an authenticated bearer user; public endpoints opt out with action-level `[AllowAnonymous]` and are listed in `ReviewedPublicSurface.cs`.
2. **Global role policies.** `[Authorize(Policy = AuthorizationPolicies.AdminOnly)]` guards the administration endpoints.
3. **Resource permissions at the edge.** An action or hub method declares `[RequirePermission(PermissionNames.X, "parameter")]`. `ResourcePermissionActionFilter` (MVC) and `ResourcePermissionHubFilter` (SignalR) evaluate it through `IAuthorizationService` and the `ResourcePermissionHandler` before the action runs.
4. **Service re-checks.** Services evaluate the same permission through `IResourceAuthorizer`, so direct calls, background work and transactional paths are held to the same rule.

## Permissions, relationships and resources

The catalog lives in `backend/src/main/application/security/authorization/Permissions.cs`. Each `Permission` names:

- the **resource kind** it is checked against (a club, event, series, payment, media asset, user, or content the service has already loaded);
- the **relationships** that pass, such as Member, Volunteer, Manager, Owner, Admin, Self, Author, Uploader, Registrant, Invitee or Payer;
- the relationships that also pass while the club is **public**;
- its **refusal**.

Evaluators decide each resource kind from the database: ownership from `Club.UserId`, staff roles from `ClubStaff`, membership from `FollowClub`, and event visibility from `EventAccessChecker`. They do not read the club or membership caches. A removed manager, a cancelled membership, or a club that became private takes effect on the caller's next request.

### Admin

Admin is a relationship like any other, so every override is written down:

- **Allowed:** `club.staff`, `club.manage` and `club.own`, and through them event management and media, series, and private-event visibility. Also `user.self-or-admin`.
- **Not allowed:**
  - `payment.owner`;
  - `content.author`;
  - `media.view` for an asset that has no club.

`club.own` (delete a club, manage its staff and staff invitations, transfer ownership) is held by the owner and admins, but not by managers.

## Refusals

| Situation | Answer |
| --- | --- |
| The resource does not exist | The action's usual `404 RESOURCE_NOT_FOUND`, with its usual message. The edge leaves missing resources to the action. |
| A hidden resource (`event.view`, `media.view`) | `404 RESOURCE_NOT_FOUND`, identical to a missing one. |
| An anonymous caller of a private club (`club.read`, `club.participate`) | `401 UNAUTHORIZED` with the endpoint's message. |
| Any other refusal | `403 FORBIDDEN` with the endpoint's message, such as `Not allowed`. |

Refusal messages are the ones the services already used. Declarations pass them with `DenialMessage` and `AnonymousDenialMessage`, usually referring to a constant on the service. The edge runs after `[ApiController]` model validation, so malformed requests are still answered `400` first.

The edge refuses before any child resource is looked up. A non-member of a private club gets the same answer whether or not a given post exists, and a non-manager cannot learn an event's state from invitation management.

## Known deviations

The matrix records the current product rules, including these known gaps. Each is a product decision for its own change:

- **Private clubs and their events (#387, #386).** These are open to everyone:
  - private club details, member lists, reviews, and users' membership lists;
  - events in a private club;
  - reviewing any club.
- **Realtime access (#392).** Access is checked when a connection joins a group, not afterwards.
- **Moderation (#402).** Any club staff member, volunteers included, may edit, pin and delete any post. Comments, replies, discussions and reviews are author-only, with no staff or admin moderation. Discussion edits do not re-check membership.
- **Payments (#401, #397).** There is no admin read or refund of another user's payment. `POST /api/payments/{eventId}` checks visibility in the service, after the idempotency replay, which is not scoped to the caller (#401).
- **Invitations (#403).** Accepting an invitation or redeeming a link does not re-check that its issuer still holds the role that let them send it.
- **Membership integrity (#404).** Membership rows have no unique constraint, and invite-link limits and `MaxMemberCount` are not enforced atomically.
- **Version rollback (#405).** Rolling a club back restores its privacy setting along with its content.
- **Self-service after losing access.** Leaving a waitlist and removing a favourite are allowed after the event is no longer visible; unregistering is not.

## Declaring permissions for a new endpoint

1. Choose the narrowest catalog permission that fits. Add a new permission only for a genuinely new rule, and put its allowed relationships, Admin included, in the catalog.
2. Declare it on the action or hub method: `[RequirePermission(PermissionNames.ClubManage, "clubId")]`. The parameter must be one of the method's parameters or a route value holding the resource id. Put feature flags on hub methods with `[FeatureGate]`; the hub filter checks them first.
3. Re-check it in the service with `_authorizer.EnsureAsync(ResourceActor.User(userId, role), Permissions.X, resource, messages)`. Check before loading child resources, and inside the transaction for writes.
4. Add the endpoint to `ApprovedPermissionMatrix.cs` with its class, edge checks, service checks, and a note when the checks alone do not explain it.
5. Regenerate the matrix below and commit it with the code.

An endpoint whose resource comes from the request body (batch operations, presigned uploads) has no edge check. Its matrix entry lists the permissions the service checks.

## Testing permissions

- **`PermissionExpectations.cs`** is the approved answer for every permission and every caller on representative resources. `ResourceAuthorizerMatrixTests` evaluates each row against the real evaluators over SQLite, including cross-club isolation, stale roles, a club becoming private, and missing resources. The table is written by hand: changing who a permission allows fails here until the table changes with it.
- **`PermissionMatrixGateTests`** (part of the Auth Contract Gate in CI) fails when any of these is true:
  - an endpoint is missing from `ApprovedPermissionMatrix`;
  - an entry is stale;
  - a declared `[RequirePermission]` differs from the approved edge checks;
  - a class contradicts the endpoint's metadata.
- **`ResourcePermissionFilterTests`** cover the MVC and hub filters: envelopes, messages, deferral of missing resources, parameter binding, and feature gates.
- **`AuthorizationMatrixEndpointsTests`** (the `authorization` integration shard) exercise representative endpoints end to end:
  - owner powers held by admins but not managers;
  - cross-club isolation;
  - a removed manager refused on the same token;
  - private-club content refused before child lookups;
  - event state hidden from outsiders;
  - participants' own records.

## Regenerating the matrix

The section below is rendered from the code. After changing the catalog, the expectations or the endpoint matrix, run:

```powershell
$env:UPDATE_AUTHORIZATION_DOC = "1"
dotnet test backend.tests.Unit/backend.tests.Unit.csproj --filter "FullyQualifiedName~AuthorizationDocumentTests"
Remove-Item Env:UPDATE_AUTHORIZATION_DOC
```

Without the variable, the same test fails in CI whenever this document is out of date.

## Matrix

<!-- BEGIN GENERATED: permission matrix -->

This section is generated by `AuthorizationMatrixDocument` in
`backend.tests.Unit/Application/Security/Authorization/`. Do not edit it by hand; see
[regenerating](#regenerating-the-matrix).

### Permission catalog

| Permission | Resource | Allowed | Also allowed while the club is public | Refusal | Guards |
| --- | --- | --- | --- | --- | --- |
| `club.read` | Club | Member, Volunteer, Manager, Owner, Admin | Anyone | 401 if anonymous, else 403 | Read a club's posts, discussions, comments and realtime feed. Public clubs are open to everyone. |
| `club.participate` | Club | Member, Volunteer, Manager, Owner, Admin | Authenticated | 401 if anonymous, else 403 | Reply, comment and react. Public clubs are open to any signed-in user. |
| `club.contribute` | Club | Member, Volunteer, Manager, Owner, Admin |  | 403 | Start a discussion. Members and staff only, even in public clubs. |
| `club.staff` | Club | Volunteer, Manager, Owner, Admin |  | 403 | Staff work: create, edit, pin and delete club posts. |
| `club.manage` | Club | Manager, Owner, Admin |  | 403 | Edit the club, its versions and rollbacks, member invitations and links, analytics, events and series. |
| `club.own` | Club | Owner, Admin |  | 403 | Delete the club, list, add and remove staff, staff invitations, and transfer ownership. |
| `event.view` | Event | Volunteer, Manager, Owner, Admin, Registrant, Invitee, Payer | Anyone | 404 (hidden) | Read an event and act on it as a participant. Only published, paused and cancelled events are visible; private ones only to the listed relationships. |
| `event.manage` | Event | Manager, Owner, Admin |  | 403 | Manage an event through its club: edit, lifecycle, versions, analytics, waitlist, invitations. |
| `event.media` | Event | Volunteer, Manager, Owner, Admin |  | 403 | Manage an event's images through its club. |
| `series.manage` | EventSeries | Manager, Owner, Admin |  | 403 | Manage a recurring series through its club. |
| `payment.owner` | Payment | Self |  | 403 | Read or refund one's own payment. |
| `media.view` | MediaAsset | Manager, Owner, Admin, Uploader |  | 404 (hidden) | Read a media asset's processing status. |
| `user.self-or-admin` | User | Admin, Self |  | 403 | Read another user's private lists. |
| `content.author` | AuthoredContent | Author |  | 403 | Edit or delete one's own discussion, reply, comment or review. |

### Approved answers by caller

`yes` is allowed; `401`, `403` and `404` are the refusal; `missing` is the resource's usual not-found answer.

| Permission | Resource | Anonymous | Unrelated user | Member | Volunteer | Manager | Owner | Admin | Other club's manager | Registrant | Invitee | Payer |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `club.read` | public club | yes | yes | yes | yes | yes | yes | yes | yes | yes | yes | yes |
| `club.read` | private club | 401 | 403 | yes | yes | yes | yes | yes | 403 | 403 | 403 | 403 |
| `club.read` | missing club | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `club.participate` | public club | 401 | yes | yes | yes | yes | yes | yes | yes | yes | yes | yes |
| `club.participate` | private club | 401 | 403 | yes | yes | yes | yes | yes | 403 | 403 | 403 | 403 |
| `club.participate` | missing club | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `club.contribute` | public club | 403 | 403 | yes | yes | yes | yes | yes | 403 | 403 | 403 | 403 |
| `club.contribute` | private club | 403 | 403 | yes | yes | yes | yes | yes | 403 | 403 | 403 | 403 |
| `club.contribute` | missing club | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `club.staff` | public club | 403 | 403 | 403 | yes | yes | yes | yes | 403 | 403 | 403 | 403 |
| `club.staff` | private club | 403 | 403 | 403 | yes | yes | yes | yes | 403 | 403 | 403 | 403 |
| `club.staff` | missing club | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `club.manage` | public club | 403 | 403 | 403 | 403 | yes | yes | yes | 403 | 403 | 403 | 403 |
| `club.manage` | private club | 403 | 403 | 403 | 403 | yes | yes | yes | 403 | 403 | 403 | 403 |
| `club.manage` | missing club | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `club.own` | public club | 403 | 403 | 403 | 403 | 403 | yes | yes | 403 | 403 | 403 | 403 |
| `club.own` | private club | 403 | 403 | 403 | 403 | 403 | yes | yes | 403 | 403 | 403 | 403 |
| `club.own` | missing club | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `event.view` | public event | yes | yes | yes | yes | yes | yes | yes | yes | yes | yes | yes |
| `event.view` | private event | 404 | 404 | 404 | yes | yes | yes | yes | 404 | yes | yes | yes |
| `event.view` | draft event | 404 | 404 | 404 | 404 | 404 | 404 | 404 | 404 | 404 | 404 | 404 |
| `event.view` | event in private club | yes | yes | yes | yes | yes | yes | yes | yes | yes | yes | yes |
| `event.view` | missing event | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `event.manage` | public event | 403 | 403 | 403 | 403 | yes | yes | yes | 403 | 403 | 403 | 403 |
| `event.manage` | private event | 403 | 403 | 403 | 403 | yes | yes | yes | 403 | 403 | 403 | 403 |
| `event.manage` | missing event | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `event.media` | public event | 403 | 403 | 403 | yes | yes | yes | yes | 403 | 403 | 403 | 403 |
| `event.media` | missing event | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `series.manage` | series | 403 | 403 | 403 | 403 | yes | yes | yes | 403 | 403 | 403 | 403 |
| `series.manage` | missing series | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `payment.owner` | payer's payment | 403 | 403 | 403 | 403 | 403 | 403 | 403 | 403 | 403 | 403 | yes |
| `payment.owner` | missing payment | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `media.view` | volunteer's club upload | 404 | 404 | 404 | yes | yes | yes | yes | 404 | 404 | 404 | 404 |
| `media.view` | member's clubless upload | 404 | 404 | yes | 404 | 404 | 404 | 404 | 404 | 404 | 404 | 404 |
| `media.view` | missing asset | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing | missing |
| `user.self-or-admin` | member's account | 403 | 403 | yes | 403 | 403 | 403 | yes | 403 | 403 | 403 | 403 |
| `content.author` | member's content | 403 | 403 | yes | 403 | 403 | 403 | 403 | 403 | 403 | 403 | 403 |

### Endpoints

Edge checks run in the MVC action filter or the hub filter before the action; service checks run
inside the service. Every endpoint the backend declares appears here exactly once.

| Endpoint | Class | Edge checks | Service checks | Note |
| --- | --- | --- | --- | --- |
| `GET /api/admin/clubs/posts` | AdminOnly policy |  |  |  |
| `POST /api/admin/clubs/posts/reindex` | AdminOnly policy |  |  |  |
| `POST /api/admin/clubs/reindex` | AdminOnly policy |  |  |  |
| `POST /api/admin/events/reindex` | AdminOnly policy |  |  |  |
| `PATCH /api/admin/users/{id}/status` | AdminOnly policy |  |  |  |
| `POST /api/auth/api/logout` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/api/refresh` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/change-password` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `GET /api/auth/csrf` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `GET /api/auth/device/verify` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/device/verify` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `DELETE /api/auth/devices` | Self |  |  | The caller's own account, identified by the access token. |
| `GET /api/auth/devices` | Self |  |  | The caller's own account, identified by the access token. |
| `DELETE /api/auth/devices/{deviceId:int}` | Self |  |  | The caller's own account, identified by the access token. |
| `GET /api/auth/email/availability` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/email/availability` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/forgot-password` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/google` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/google/code` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/login` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/logout` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `GET /api/auth/me` | Self |  |  | The caller's own account, identified by the access token. |
| `GET /api/auth/mfa` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/disable` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/enable/start` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/enroll/start` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/enroll/verify` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/recovery-codes/regenerate` | Self |  |  | The caller's own account, identified by the access token. |
| `GET /api/auth/mfa/recovery-codes/status` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/remove` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/sms/disable` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/sms/enable/start` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/sms/enroll/start` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/sms/enroll/verify` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/sms/remove` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/start` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `GET /api/auth/mfa/step-up/options` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/step-up/start` | Self |  |  | The caller's own account, identified by the access token. |
| `GET /api/auth/mfa/step-up/status` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/step-up/verify` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/totp/disable` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/totp/enable` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/totp/enroll/start` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/totp/enroll/verify` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/totp/remove` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/mfa/verify` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/mfa/verify/recovery-code` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/mfa/verify/totp` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/microsoft` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/oauth/complete` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `GET /api/auth/passkeys` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/passkeys/registration` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/passkeys/registration/options` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/passkeys/sign-in` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/passkeys/sign-in/options` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/passkeys/step-up` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/passkeys/step-up/options` | Self |  |  | The caller's own account, identified by the access token. |
| `DELETE /api/auth/passkeys/{passkeyId:int}` | Self |  |  | The caller's own account, identified by the access token. |
| `PATCH /api/auth/passkeys/{passkeyId:int}` | Self |  |  | The caller's own account, identified by the access token. |
| `GET /api/auth/providers` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/providers/link/confirm` | Self |  |  | The caller's own account, identified by the access token. |
| `DELETE /api/auth/providers/{provider}` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/providers/{provider}/link/complete` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/providers/{provider}/link/start` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/recovery/password` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/recovery/username` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/refresh` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/reset-password` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `GET /api/auth/security-events` | Self |  |  | The caller's own account, identified by the access token. |
| `DELETE /api/auth/sessions` | Self |  |  | The caller's own account, identified by the access token. |
| `GET /api/auth/sessions` | Self |  |  | The caller's own account, identified by the access token. |
| `DELETE /api/auth/sessions/others` | Self |  |  | The caller's own account, identified by the access token. |
| `DELETE /api/auth/sessions/{sessionId}` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/auth/signup` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `GET /api/auth/username/availability` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `GET /api/auth/username/suggestions` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `GET /api/auth/verify` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/verify` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `GET /api/auth/verify/email-change` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/verify/email-change` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `POST /api/auth/verify/otp` | Public |  |  | Sign-in, sign-up, verification and recovery. |
| `GET /api/clubs` | Public |  |  | Public club search; private clubs are filtered out. |
| `POST /api/clubs` | Authenticated |  |  | Any signed-in user may create a club. |
| `POST /api/clubs/invitations/accept` | Authenticated |  |  | Only the invitation's recipient (checked in the service). |
| `POST /api/clubs/invitations/decline` | Authenticated |  |  | Only the invitation's recipient (checked in the service). |
| `POST /api/clubs/invitations/resolve` | Public |  |  | Possession of the invitation token is the authority. |
| `GET /api/clubs/managed` | Self |  |  | Clubs the caller owns or staffs. |
| `POST /api/clubs/members/invitation-links/redeem` | Authenticated |  |  | Any signed-in holder of a live link. |
| `POST /api/clubs/members/invitations/accept` | Authenticated |  |  | Only the invitation's recipient (checked in the service). |
| `POST /api/clubs/members/invitations/decline` | Authenticated |  |  | Only the invitation's recipient (checked in the service). |
| `POST /api/clubs/members/invitations/resolve` | Public |  |  | Possession of the invitation token is the authority. |
| `POST /api/clubs/search` | Public |  |  | Public club search; private clubs are filtered out. |
| `GET /api/clubs/{clubId}/discussions` | Resource | `club.read` on `clubId` | `club.read` |  |
| `POST /api/clubs/{clubId}/discussions` | Resource | `club.contribute` on `clubId` | `club.contribute` |  |
| `DELETE /api/clubs/{clubId}/discussions/{discussionId}` | Resource |  | `content.author` | Known deviation: author only, without a membership re-check. |
| `PUT /api/clubs/{clubId}/discussions/{discussionId}` | Resource |  | `content.author` | Known deviation: author only, without a membership re-check. |
| `GET /api/clubs/{clubId}/discussions/{discussionId}/replies` | Resource | `club.read` on `clubId` | `club.read` |  |
| `POST /api/clubs/{clubId}/discussions/{discussionId}/replies` | Resource | `club.participate` on `clubId` | `club.participate` |  |
| `DELETE /api/clubs/{clubId}/discussions/{discussionId}/replies/{replyId}` | Resource | `club.participate` on `clubId` | `club.participate`, `content.author` |  |
| `PUT /api/clubs/{clubId}/discussions/{discussionId}/replies/{replyId}` | Resource | `club.participate` on `clubId` | `club.participate`, `content.author` |  |
| `DELETE /api/clubs/{clubId}/discussions/{discussionId}/replies/{replyId}/reaction` | Resource | `club.participate` on `clubId` | `club.participate` |  |
| `PUT /api/clubs/{clubId}/discussions/{discussionId}/replies/{replyId}/reaction` | Resource | `club.participate` on `clubId` | `club.participate` |  |
| `DELETE /api/clubs/{clubId}/join` | Self |  |  | Leave a club the caller belongs to. |
| `POST /api/clubs/{clubId}/join` | Authenticated |  |  | Public clubs only; a private club is invite-only (checked in the service). |
| `GET /api/clubs/{clubId}/members` | Public |  |  | Known deviation (#386): any caller can list any club's members. |
| `GET /api/clubs/{clubId}/members/invitation-links` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `POST /api/clubs/{clubId}/members/invitation-links` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `POST /api/clubs/{clubId}/members/invitation-links/{linkId}/revoke` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `GET /api/clubs/{clubId}/members/invitations` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `POST /api/clubs/{clubId}/members/invitations` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `POST /api/clubs/{clubId}/members/invitations/{recipientUserId}/revoke` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `GET /api/clubs/{clubId}/members/me` | Self |  |  | The caller's own membership. |
| `GET /api/clubs/{clubId}/posts` | Resource | `club.read` on `clubId` | `club.read` |  |
| `POST /api/clubs/{clubId}/posts` | Resource | `club.staff` on `clubId` | `club.staff` |  |
| `DELETE /api/clubs/{clubId}/posts/{id}` | Resource | `club.staff` on `clubId` | `club.staff` | Known deviation: any staff member may delete any post. |
| `GET /api/clubs/{clubId}/posts/{id}` | Resource | `club.read` on `clubId` | `club.read` |  |
| `PUT /api/clubs/{clubId}/posts/{id}` | Resource | `club.staff` on `clubId` | `club.staff` | Known deviation: any staff member may edit or pin any post. |
| `GET /api/clubs/{clubId}/posts/{postId}/comments` | Resource | `club.read` on `clubId` | `club.read` |  |
| `POST /api/clubs/{clubId}/posts/{postId}/comments` | Resource | `club.participate` on `clubId` | `club.participate` |  |
| `DELETE /api/clubs/{clubId}/posts/{postId}/comments/{commentId}` | Resource | `club.participate` on `clubId` | `club.participate`, `content.author` |  |
| `PUT /api/clubs/{clubId}/posts/{postId}/comments/{commentId}` | Resource | `club.participate` on `clubId` | `club.participate`, `content.author` |  |
| `DELETE /api/clubs/{clubId}/posts/{postId}/comments/{commentId}/reaction` | Resource | `club.participate` on `clubId` | `club.participate` |  |
| `PUT /api/clubs/{clubId}/posts/{postId}/comments/{commentId}/reaction` | Resource | `club.participate` on `clubId` | `club.participate` |  |
| `GET /api/clubs/{clubId}/reviews` | Public |  |  | Known deviation (#387): reviews of private clubs are public. |
| `POST /api/clubs/{clubId}/reviews` | Authenticated |  |  | Known deviation (#387): any signed-in user may review any club. |
| `DELETE /api/clubs/{clubId}/reviews/{reviewId}` | Resource |  | `content.author` |  |
| `PUT /api/clubs/{clubId}/reviews/{reviewId}` | Resource |  | `content.author` |  |
| `GET /api/clubs/{clubId}/staff/invitations` | Resource | `club.own` on `clubId` | `club.own` |  |
| `POST /api/clubs/{clubId}/staff/invitations` | Resource | `club.own` on `clubId` | `club.own` |  |
| `POST /api/clubs/{clubId}/staff/invitations/{recipientUserId}/revoke` | Resource | `club.own` on `clubId` | `club.own` |  |
| `DELETE /api/clubs/{id}` | Resource | `club.own` on `id` | `club.own` |  |
| `GET /api/clubs/{id}` | Public |  |  | Known deviation (#387): private club details are returned to anyone. |
| `PUT /api/clubs/{id}` | Resource | `club.manage` on `id` | `club.manage` |  |
| `GET /api/clubs/{id}/staff` | Resource | `club.own` on `id` | `club.own` |  |
| `POST /api/clubs/{id}/staff/managers` | Resource | `club.own` on `id` | `club.own` |  |
| `POST /api/clubs/{id}/staff/volunteers` | Resource | `club.own` on `id` | `club.own` |  |
| `DELETE /api/clubs/{id}/staff/{userId}` | Resource | `club.own` on `id` | `club.own` |  |
| `POST /api/clubs/{id}/transfer-ownership` | Resource | `club.own` on `id` | `club.own` |  |
| `GET /api/clubs/{id}/versions` | Resource | `club.manage` on `id` | `club.manage` |  |
| `GET /api/clubs/{id}/versions/{versionNumber}` | Resource | `club.manage` on `id` | `club.manage` |  |
| `POST /api/clubs/{id}/versions/{versionNumber}/rollback` | Resource | `club.manage` on `id` | `club.manage` |  |
| `GET /api/events` | Public |  |  | Public, published events only. |
| `DELETE /api/events/batch` | Resource |  | `club.manage` | The clubs come from the request body; every one is checked in the service. |
| `GET /api/events/batch` | Resource |  | `event.view` | Events the caller cannot view are dropped. |
| `PUT /api/events/batch` | Resource |  | `club.manage` | The clubs come from the request body; every one is checked in the service. |
| `DELETE /api/events/batch/register` | Self |  |  | Each event is checked for visibility in the service. |
| `POST /api/events/batch/register` | Self |  |  | Each event is checked for visibility in the service. |
| `POST /api/events/batch/{clubId}` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `GET /api/events/clubs/{clubId}` | Public |  |  | Known deviation (#387): ignores the club's privacy. |
| `GET /api/events/clubs/{clubId}/analytics` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `POST /api/events/clubs/{clubId}/drafts` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `GET /api/events/clubs/{clubId}/manage` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `GET /api/events/clubs/{clubId}/series` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `POST /api/events/clubs/{clubId}/series/preview` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `POST /api/events/images/presigned-url` | Resource |  | `club.manage`, `event.media` | Club and event come from the body. Without a club (a new club's images) any signed-in user may upload. |
| `POST /api/events/invitations/accept` | Authenticated |  |  | Only the invitation's recipient (checked in the service). |
| `POST /api/events/invitations/decline` | Authenticated |  |  | Only the invitation's recipient (checked in the service). |
| `POST /api/events/invitations/resolve` | Public |  |  | Possession of the invitation token is the authority. |
| `POST /api/events/invitations/{invitationId}/accept` | Authenticated |  |  | Only the invitation's recipient (checked in the service). |
| `POST /api/events/invitations/{invitationId}/decline` | Authenticated |  |  | Only the invitation's recipient (checked in the service). |
| `GET /api/events/me/favourites/ids` | Self |  |  | The caller's own favourites. |
| `GET /api/events/me/invited` | Self |  |  | The caller's own invitations. |
| `GET /api/events/me/pinned` | Self |  |  | Events the caller can no longer view are dropped. |
| `DELETE /api/events/me/recently-viewed` | Self |  |  | The caller's own viewing history. |
| `GET /api/events/me/recently-viewed` | Self |  |  | The caller's own viewing history. |
| `DELETE /api/events/me/recently-viewed/batch` | Self |  |  | The caller's own viewing history. |
| `POST /api/events/me/recently-viewed/merge` | Self |  |  | The caller's own viewing history. |
| `GET /api/events/me/recently-viewed/settings` | Self |  |  | The caller's own viewing history. |
| `PUT /api/events/me/recently-viewed/settings` | Self |  |  | The caller's own viewing history. |
| `DELETE /api/events/me/recently-viewed/{eventId}` | Self |  |  | The caller's own viewing history. |
| `GET /api/events/me/waitlisted` | Self |  |  | Events the caller can no longer view are redacted. |
| `POST /api/events/search` | Public |  |  | Public, published events only. |
| `DELETE /api/events/series/{seriesId}` | Resource | `series.manage` on `seriesId` | `series.manage` |  |
| `GET /api/events/series/{seriesId}` | Resource | `series.manage` on `seriesId` | `series.manage` |  |
| `POST /api/events/series/{seriesId}/cancel` | Resource | `series.manage` on `seriesId` | `series.manage` |  |
| `POST /api/events/series/{seriesId}/extend` | Resource | `series.manage` on `seriesId` | `series.manage` |  |
| `PATCH /api/events/series/{seriesId}/occurrences` | Resource | `series.manage` on `seriesId` | `series.manage` |  |
| `POST /api/events/series/{seriesId}/publish` | Resource | `series.manage` on `seriesId` | `series.manage` |  |
| `POST /api/events/{clubId}` | Resource | `club.manage` on `clubId` | `club.manage` |  |
| `DELETE /api/events/{eventId}` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `GET /api/events/{eventId}` | Resource | `event.view` on `eventId` | `event.view` |  |
| `PUT /api/events/{eventId}` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `GET /api/events/{eventId}/analytics` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/archive` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/cancel` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `PATCH /api/events/{eventId}/draft` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `DELETE /api/events/{eventId}/favourite` | Self |  |  | Removing a favourite is allowed after losing visibility. |
| `POST /api/events/{eventId}/favourite` | Resource | `event.view` on `eventId` | `event.view` |  |
| `GET /api/events/{eventId}/favourite/me` | Self |  |  | The caller's own favourite. |
| `GET /api/events/{eventId}/images` | Resource | `event.media` on `eventId` | `event.media` |  |
| `POST /api/events/{eventId}/images` | Resource | `event.media` on `eventId` | `event.media` |  |
| `PUT /api/events/{eventId}/images/order` | Resource | `event.media` on `eventId` | `event.media` |  |
| `DELETE /api/events/{eventId}/images/{imageId}` | Resource | `event.media` on `eventId` | `event.media` |  |
| `PATCH /api/events/{eventId}/images/{imageId}` | Resource | `event.media` on `eventId` | `event.media` |  |
| `PUT /api/events/{eventId}/images/{imageId}` | Resource | `event.media` on `eventId` | `event.media` |  |
| `PUT /api/events/{eventId}/images/{imageId}/cover` | Resource | `event.media` on `eventId` | `event.media` |  |
| `GET /api/events/{eventId}/invitation-links` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/invitation-links` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/invitation-links/{linkId}/revoke` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `GET /api/events/{eventId}/invitations` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/invitations` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/invitations/{invitationId}/revoke` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/lifecycle/revert` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `GET /api/events/{eventId}/manage` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/pause` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/publish` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `DELETE /api/events/{eventId}/register` | Resource | `event.view` on `eventId` | `event.view` |  |
| `PATCH /api/events/{eventId}/register` | Resource | `event.view` on `eventId` | `event.view` |  |
| `POST /api/events/{eventId}/register` | Resource | `event.view` on `eventId` | `event.view` |  |
| `GET /api/events/{eventId}/registrations` | Resource | `event.view` on `eventId` | `event.view`, `event.manage` | Contact and dietary fields only for event managers. Known deviation: registrant ids are public. |
| `GET /api/events/{eventId}/registrations/me` | Resource | `event.view` on `eventId` | `event.view` |  |
| `POST /api/events/{eventId}/reinstate` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/resume` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/series` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/series/detach` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/unarchive` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `GET /api/events/{eventId}/versions` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `GET /api/events/{eventId}/versions/{versionNumber}` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/versions/{versionNumber}/rollback` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/view` | Resource | `event.view` on `eventId` | `event.view` |  |
| `DELETE /api/events/{eventId}/waitlist` | Self |  |  | Leaving is allowed even after losing visibility. |
| `GET /api/events/{eventId}/waitlist` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `POST /api/events/{eventId}/waitlist` | Resource | `event.view` on `eventId` | `event.view` |  |
| `GET /api/events/{eventId}/waitlist/me` | Resource | `event.view` on `eventId` | `event.view` |  |
| `POST /api/events/{eventId}/waitlist/promote` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `DELETE /api/events/{eventId}/waitlist/{entryId}` | Resource | `event.manage` on `eventId` | `event.manage` |  |
| `GET /api/media/{publicId:guid}` | Resource | `media.view` on `publicId` | `media.view` |  |
| `GET /api/payments/me` | Self |  |  | The caller's own payments. |
| `POST /api/payments/webhook` | Public |  |  | Authenticated by the Stripe signature. |
| `POST /api/payments/{eventId:int}` | Resource |  | `event.view` | Visibility is checked in the service. The idempotency replay is tracked as a separate security issue. |
| `GET /api/payments/{paymentId:int}` | Resource | `payment.owner` on `paymentId` | `payment.owner` | Re-checked in the controller after the payment is loaded. Known deviation: no admin read of another user's payment. |
| `POST /api/payments/{paymentId:int}/refund` | Resource | `payment.owner` on `paymentId` | `payment.owner` | Known deviation: no admin refund. |
| `DELETE /api/profile` | Self |  |  | The caller's own account, identified by the access token. |
| `GET /api/profile` | Self |  |  | The caller's own account, identified by the access token. |
| `PATCH /api/profile` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/profile/avatar` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/profile/change-password` | Self |  |  | The caller's own account, identified by the access token. |
| `POST /api/profile/email` | Self |  |  | The caller's own account, identified by the access token. |
| `DELETE /api/profile/email/pending` | Self |  |  | The caller's own account, identified by the access token. |
| `GET /api/profile/email/pending` | Self |  |  | The caller's own account, identified by the access token. |
| `PATCH /api/profile/username` | Self |  |  | The caller's own account, identified by the access token. |
| `GET /api/profile/{username}` | Public |  |  | Public profile card. |
| `GET /api/users/{userId}/clubs/following` | Authenticated |  |  | Known deviation (#386): any signed-in user can list another user's memberships. |
| `GET /api/users/{userId}/events/registered` | Resource | `user.self-or-admin` on `userId` | `user.self-or-admin` | Re-checked in the controller. |
| `GET /api/users/{userId}/reviews` | AdminOnly policy |  |  |  |
| `HUB ClubRealtimeHub.JoinClub` | Resource | `club.read` on `clubId` | `club.read` |  |
| `HUB ClubRealtimeHub.JoinDiscussion` | Resource | `club.read` on `clubId` | `club.read` | The service also proves the discussion belongs to the club. |
| `HUB ClubRealtimeHub.JoinPost` | Resource | `club.read` on `clubId` | `club.read` | The service also proves the post belongs to the club. |
| `HUB ClubRealtimeHub.LeaveClub` | Public |  |  | Grants nothing. |
| `HUB ClubRealtimeHub.LeaveDiscussion` | Public |  |  | Grants nothing. |
| `HUB ClubRealtimeHub.LeavePost` | Public |  |  | Grants nothing. |
| `HUB ClubRealtimeHub.RequestPresence` | Resource | `club.read` on `clubId` | `club.read` |  |
| `HUB ClubRealtimeHub.Typing` | Public |  |  | Requires a signed-in caller who already joined the thread (checked in the method). |

<!-- END GENERATED: permission matrix -->
