# Authentication and identity

How Inscribed identifies humans and machines, why it is built this way, and how to verify it end to end. The consumer-facing summary lives in the [README](../README.md#identity-tokens-and-roles); this document is the deep dive for people changing the auth module.

## Overview

Inscribed wears two hats. Toward Google it is an **OAuth client**: Google's only job is to answer "this person really is fatih@gmail.com". Toward everything else it is an **authorization server**: it mints its own RS256 access tokens carrying its own roles and tenant key, and publishes the public keys as standard JWKS at `/.well-known/jwks.json`. The alternative (validating Google's tokens directly) was rejected because roles and tenancy could not live in Google's claim model; owning the token means adding another login provider later changes nothing on the CMS side.

## The claim contract

Everything the CMS reads from an authenticated request fits in five claims. As long as a token carries these, the CMS does not care who issued it; this contract, not an interface, is the seam that makes the auth module replaceable.

| Claim | Meaning | Used for |
|---|---|---|
| `sub` | user id, or `service:{id}` for service keys | `updatedBy`, draft ownership |
| `azp` | tenant key (`Client.Key`) | all data isolation |
| `roles` | authorization roles | `CmsAccess` / `CmsRead` / `AdminAccess` policies |
| `name` | display name (falls back to e-mail); service keys carry the key name | panel display, `Identity.Name` |
| `email` | user e-mail; absent on service-key principals | panel display |

There is deliberately no `preferred_username`: Inscribed has no username concept (no local accounts, no passwords), so the OIDC-standard `name` + `email` pair is emitted instead.

## Credentials

| Credential | Form | Lifetime | Revocable | Carried in |
|---|---|---|---|---|
| Access token | RS256 JWT | `Auth:AccessTokenMinutes` (15) | no, by design | `Authorization: Bearer` |
| Refresh token | opaque 256-bit random, SHA-256 hash in DB | `Auth:RefreshTokenDays` (30) | instantly | httpOnly cookie, `Path=/auth` |
| Service key | opaque `ink_live_…`, SHA-256 hash in DB | optional expiry | instantly | `X-Service-Key` or `Authorization: Bearer` |

Storage rules: raw secrets exist exactly once, in the response of the call that created them. SHA-256 without a work factor is sufficient because both values are 256-bit random; bcrypt-style stretching only matters for low-entropy secrets. Refresh tokens are opaque rather than JWTs because their only consumer is this server and they must be revocable, which requires a DB row anyway.

A **policy scheme** routes each request per its shape: requests presenting an `ink_live_` key go to the `ServiceToken` handler, everything else to `Bearer`. Endpoints never inspect headers themselves; `/cms/sync` accepts both credential kinds without containing a line of auth logic.

## Flows

### Bootstrap

On startup, in order: migrate both DbContexts, touch the signing key store (generates an RS256 key if none exists, so misconfiguration fails at boot rather than on the first request), seed the `admin` client. Seeding solves the cold-start deadlock: with zero clients nobody can log in, so nobody could become admin. E-mails in `Auth:Admin:BootstrapAdmins` receive `cms:admin` on login without a membership. The seeder is idempotent.

### Google login

`GET /auth/login?clientKey=…&redirectUri=…`

1. The client is loaded; the request fails with 400 unless the client is active and the redirect URI's **origin** is in `AllowedRedirectOrigins` (open-redirect defense: this login cannot be used to bounce users to an attacker's site).
2. A random `state` and a PKCE verifier are generated and stored in Redis with a 10-minute TTL, then the browser is redirected to Google. Redis, not memory, because abandoned logins must expire and any API instance must be able to serve the callback.

`GET /auth/google/callback?code=…&state=…`

3. The state is read from Redis and **deleted immediately** (single use, replay defense); unknown state is 400.
4. The code, PKCE verifier and client secret are exchanged server-to-server for an `id_token`. Its signature is not re-verified (it arrived directly from Google over TLS, which OIDC permits), but `iss`, `aud` and `exp` are checked, and **`email_verified` must be true**: an unverified-e-mail Google account must not be able to impersonate a CMS user.
5. The user is found by Google subject, else by e-mail (in which case the Google account is linked). A user whose e-mail matches but whose already-linked Google subject differs is **rejected** as a takeover attempt.
6. A refresh token is issued as an httpOnly cookie and the browser is redirected to the redirect URI **stored in Redis**, never the one from the query. Access tokens are never placed in URLs.

### Refresh and rotation

`POST /auth/refresh` (cookie travels automatically):

1. The raw cookie value is hashed and looked up; unknown hash is 401.
2. **Reuse detection:** if the found row is already revoked, someone is replaying an old token. Since the victim cannot be told apart from the attacker, the whole `FamilyId` lineage is revoked in one update and the response is 401; the real user logs in again, the attacker is locked out.
3. **Reuse leeway** (`Auth:ReuseLeewaySeconds`, default 30): a replay within the window is treated as a network race instead, but only if the row was revoked *by rotation* (never by logout) and its successor is still live. The typical trigger is a refresh response lost in transit followed by a client retry, which is byte-identical to an attack and cannot be disambiguated client-side. The undelivered successor is revoked and a fresh rotation is issued from the same family.
4. **Roles are recomputed here**, from memberships plus the bootstrap-admin list. This is the practical answer to unrevocable access tokens: a role change takes effect within one access-token lifetime.
5. The old row is revoked, a new refresh + access pair is issued. Two concurrent refreshes race on the row's `Version`; the loser gets a silent 401 (double-spend protection).

`POST /auth/logout` revokes the refresh token and deletes the cookie. An access token already in the wild stays technically valid for up to its remaining lifetime; checking a blacklist on every request would forfeit the point of JWTs, and a 15-minute window is the accepted industry trade-off.

### Service keys (M2M)

The handler looks the key up by its first 16 characters (`KeyPrefix`, indexed), compares the full SHA-256 with `CryptographicOperations.FixedTimeEquals` (timing-attack defense), and checks revocation, expiry and the owning client's active flag. `LastUsedAt` is written at most once a minute via `ExecuteUpdate`, bypassing `Version`, so telemetry cannot cause concurrency conflicts under parallel requests. The resulting principal carries `azp` = the key's client, `roles` = the key's roles, `sub` = `service:{id}`, `name` = the key's name.

### Signing-key rotation

`POST /admin/signing-keys/rotate`: a new key is generated and signs from that moment; the old key stays valid for verification for a **1-hour grace** (in-flight access tokens live at most 15 minutes plus clock skew), then drops out of JWKS. Validation keys are cached for 5 minutes, and an unknown `kid` triggers an immediate reload with a 30-second floor (so forged kids cannot hammer the DB). Rotation therefore propagates in seconds, restart-free, across multiple instances.

## Role model

| Role | Grants | Typical holder |
|---|---|---|
| `cms:access` | read + write CMS content | editors, deploy-pipeline keys |
| `cms:read` | read-only (`CmsRead` accepts either role) | render keys of private sites |
| `cms:admin` | `/admin/*` | operators |

Admin endpoints ignore `azp`: an admin manages all clients regardless of which client they logged in through, while content editing still requires a real membership on the target client. Public sites need no role at all once the client's `AllowAnonymousContentRead` flag is on; that flag is tenant policy, changed by an admin (`PUT /admin/clients/{key}`), never by sync.

Authorization policies (`CmsAccess`, `CmsRead`, `AdminAccess`) are defined in [Program.cs](../src/Inscribed.Api/Program.cs), not inside the auth module: what counts as "may edit content" is a CMS concern and must survive replacing the identity provider.

## Storage

Auth tables are prefixed `auth_*`, live in their own `AuthDbContext` with a separate migration history table (`__ef_migrations_history_auth`), and share the entity house style (private constructor, static factory, `Version` bump on mutation). Removing the module removes its schema cleanly.

## Configuration

```jsonc
"Auth": {
  "Issuer": "https://cms.example.com",   // iss claim + public base URL; Google redirect URI derives from it
  "Audience": "inscribed-cms",
  "AccessTokenMinutes": 15,
  "RefreshTokenDays": 30,
  "ReuseLeewaySeconds": 30,              // 0 = strict reuse detection
  "AdminClientKey": "admin",
  "Cookie": { "Name": "inscribed_rt", "SameSite": "Lax", "Secure": true },
  "Google": { "ClientId": "", "ClientSecret": "", "CallbackPath": "/auth/google/callback" },
  "Admin": {
    "Role": "cms:admin",
    "BootstrapAdmins": ["you@example.com"],
    "ConsoleOrigins": ["https://admin.example.com"]
  }
}
```

Environment variables use `__` for nesting: `Auth__Google__ClientSecret=…`, `Auth__Admin__BootstrapAdmins__0=…`. Secrets belong in env/secret stores only. In `Production` the app refuses to start with a `localhost` issuer (`ValidateOnStart`): failing loudly beats silently minting wrong tokens.

Cookie deployment rules: the SPA and API must share a registrable domain (e.g. `app.example.com` + `api.example.com`) with `SameSite=Lax`; a fully third-party API domain is unsupported because Safari and Firefox block third-party cookies regardless of `SameSite=None`. Local development across ports (`localhost:3001` → `localhost:5000`) counts as same-site; use `Secure=false` there.

## Smoke-test chain

The reference end-to-end verification after auth changes:

1. `docker compose up -d --build`; startup logs show both migrations and the seed.
2. `GET /.well-known/jwks.json` returns at least one key.
3. `/auth/login?clientKey=admin&redirectUri=…` with a bootstrap-admin Google account completes and sets the cookie.
4. `POST /auth/refresh` returns an access token whose decoded payload carries `sub`, `azp`, `roles`, `name`, `email`.
5. `POST /admin/clients/{key}/service-keys` returns the raw key once.
6. A `cms:access` key gets 200 from `POST /cms/sync`; a `cms:read` key gets 200 from `GET /cms/data` but **403** from `POST /cms/sync`.
7. With the client flag off, `GET /cms/public/{clientKey}/data` is 404; after `PUT /admin/clients/{key}` enables it, 200 with `Cache-Control: public`.
8. Revoking the service key turns its next request into 401.

## Known limits

Deliberately deferred, to be revisited before production hardening: the signing private key is stored as plain PEM (the DB is the trust boundary; at-rest encryption via KMS/DataProtection is a hardening step), there is no rate limiting on `/auth/*`, expired refresh tokens are never garbage-collected, and the module has no unit tests (`RefreshTokenService`, `JwtIssuer`, `ServiceKeyService` are the hungriest).
