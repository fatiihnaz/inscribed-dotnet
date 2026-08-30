# Authentication and identity

How Inscribed identifies humans and machines, why it is built this way, and how to verify it end to end. The consumer-facing summary lives in the [README](../README.md#identity-tokens-and-capabilities); this document is the deep dive for people changing the auth module.

## Overview

Inscribed wears two hats. Toward Google it is an **OAuth client**: Google's only job is to answer "this person really is fatih@gmail.com". Toward everything else it is an **authorization server**: it mints its own RS256 access tokens carrying its own capabilities and tenant key, and publishes the public keys as standard JWKS at `/.well-known/jwks.json`. The alternative (validating Google's tokens directly) was rejected because capabilities and tenancy could not live in Google's claim model; owning the token means adding another login provider later changes nothing on the CMS side.

## Choosing an issuer

The bundled identity provider ships in the box but is not mandatory. One setting decides:

```jsonc
"Auth": { "Mode": "BuiltIn" }   // BuiltIn | External
```

**BuiltIn** is everything this document describes: Google login, refresh rotation, service keys, the
`auth_*` tables, `/auth/*` and the user and membership routes under `/admin/*`.

**External** switches all of it off. `Inscribed.Auth.Issuer` is never registered, so its schema is
never migrated and its routes return 404. Inscribed becomes a pure resource server that validates
tokens minted elsewhere, discovered through `Auth:Authority`:

```jsonc
"Auth": {
  "Mode": "External",
  "Authority": "https://keycloak.example.com/realms/inscribed",
  "Audience": "inscribed-cms",
  "TenantClaim": "tenant",
  "RolesClaim": "roles",
  "RoleMap": { "cms-editor": "content:write" }
}
```

`ValidIssuer` is deliberately left unset in External mode: the handler reads `iss` and the signing
keys from the discovery document, so a provider sitting behind a proxy whose `iss` differs from its
base URL still validates. Discovery is lazy and retried, so the CMS starts even when the provider is
briefly down; only configuration errors (External with no `Authority`) stop the process.

`TenantClaim` exists because `azp` carries the provider's client id, and one tenant often needs more
than one client (a panel, a mobile app, a CI service account). A hardcoded claim per client lets them
all resolve to the same Inscribed client key. In BuiltIn mode it must stay `azp`, which the bundled
issuer mints itself; the app refuses to start otherwise.

`RolesClaim` accepts a dotted path (`realm_access.roles`) for providers that nest roles inside a JSON
claim. `RoleMap` translates provider role names onto the capability vocabulary; keys must not contain
a colon, because environment variables split nested keys on it.

**Switching modes** leaves `auth_*` data untouched, so going back finds it in place. But service keys
minted by the bundled issuer stop working the moment you switch, and have to be replaced with the
provider's own machine credentials. That is the one hard cut in the migration.

**What the provider has to emit.** On Keycloak these are three protocol mappers on the client, and two
of them fail silently when missing: without the audience mapper every request is a bare 401, without
the roles mapper every request is a bare 403, and neither leaves anything in the log naming the cause.
Check them first when a freshly wired provider refuses everything.

| Mapper | Type | Produces |
|---|---|---|
| Audience | `oidc-audience-mapper` | `aud` containing `Auth:Audience` |
| Realm roles | `oidc-usermodel-realm-role-mapper`, multivalued, claim name `roles` | a flat `roles` array rather than nested `realm_access.roles` |
| Tenant | `oidc-hardcoded-claim-mapper`, claim name matching `Auth:TenantClaim` | the Inscribed client key this provider client belongs to |

The roles mapper is optional if you set `RolesClaim` to `realm_access.roles` instead and let Inscribed
read the nested claim. The other two have no alternative.

`GET /auth/whoami` answers "what does this token actually carry" in one call: tenant, resolved
capabilities, the role claim type in force, and every raw claim. It exists in both modes and is the
first thing to reach for when a provider is newly wired up.

## The claim contract

Everything the CMS reads from an authenticated request fits in five claims. As long as a token carries these, the CMS does not care who issued it; this contract, not an interface, is the seam that makes the auth module replaceable.

| Claim | Meaning | Used for |
|---|---|---|
| `sub` | user id, or `service:{id}` for service keys | the stored writer of a row, draft ownership |
| `azp` | tenant key (`Client.Key`) | all data isolation |
| `roles` | granted capabilities | `ContentRead` / `ContentWrite` / `SchemaSync` / `ClientAdmin` / `ServiceAdmin` policies |
| `name` | display name (falls back to e-mail); service keys carry the key name | panel display, `Identity.Name` |
| `email` | user e-mail; absent on service-key principals | panel display; proves a human principal to both admin policies |

There is deliberately no `preferred_username`: Inscribed has no username concept (no local accounts, no passwords), so the OIDC-standard `name` + `email` pair is emitted instead.

The vocabulary itself belongs to this module, not to the CMS. `content:read`, `content:write`, `schema:sync`, `client:admin`, `service:admin` and the `roles` claim that carries them are declared once, in [CapabilityCatalog](../src/Inscribed.Auth/Authorization/CapabilityCatalog.cs), and nothing in `Inscribed.Application` names any of them. Replace the module and you bring your own names; there is no constant in the core to edit and no second list to drift.

That holds because the two places capabilities are enforced are both outside the core. Endpoint authorization is wired in the composition root, where `RequireRole` reads the catalog. The one authorization question the CMS core has to ask for itself is whether a principal may act on a claim-derived item that is not their own, and it asks it through an interface:

```csharp
public interface IAdministratorPolicy
{
    bool IsAdministrator(ClaimsPrincipal user);
}
```

`CapabilityAdministratorPolicy` answers it with either admin capability, since the caller is already tenant-scoped by the time it is asked. A replacement module registers its own implementation and answers with whatever its own tokens carry.

## Credentials

| Credential | Form | Lifetime | Revocable | Carried in |
|---|---|---|---|---|
| Access token | RS256 JWT | `Auth:AccessTokenMinutes` (15) | no, by design | `Authorization: Bearer` |
| Refresh token | opaque 256-bit random, SHA-256 hash in DB | `Auth:RefreshTokenDays` (30) | instantly | httpOnly cookie, `Path=/auth` |
| Service key | opaque `ink_live_…`, SHA-256 hash in DB | optional expiry | instantly | `Authorization: Bearer` |

Storage rules: raw secrets exist exactly once, in the response of the call that created them. SHA-256 without a work factor is sufficient because both values are 256-bit random; bcrypt-style stretching only matters for low-entropy secrets. Refresh tokens are opaque rather than JWTs because their only consumer is this server and they must be revocable, which requires a DB row anyway.

A **policy scheme** routes each request per its shape: both credential kinds arrive in `Authorization: Bearer`, and the `ink_live_` prefix decides which handler runs (a JWT starts with `eyJ`, so the two can never be confused). A dedicated `X-Service-Key` header was dropped on purpose: one channel keeps `Vary: Authorization` a complete cache key, and log pipelines redact `Authorization` by default while a custom header would leak the raw key into access logs. Endpoints never inspect headers themselves; `/cms/sync` accepts both credential kinds without containing a line of auth logic.

## Flows

### Bootstrap

On startup, in order: migrate both DbContexts, touch the signing key store (generates an RS256 key if none exists, so misconfiguration fails at boot rather than on the first request), seed the `admin` client. Seeding solves the cold-start deadlock: with zero clients nobody can log in, so nobody could become admin. E-mails in `Auth:Admin:BootstrapAdmins` receive `service:admin` on login without a membership. The seeder is idempotent.

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
4. **Capabilities are recomputed here**, from memberships plus the bootstrap-admin list. This is the practical answer to unrevocable access tokens: a grant change takes effect within one access-token lifetime.
5. The old row is revoked, a new refresh + access pair is issued. Two concurrent refreshes race on the row's `Version`; the loser gets a silent 401 (double-spend protection).

`POST /auth/logout` revokes the refresh token and deletes the cookie. An access token already in the wild stays technically valid for up to its remaining lifetime; checking a blacklist on every request would forfeit the point of JWTs, and a 15-minute window is the accepted industry trade-off.

### Service keys (M2M)

The handler looks the key up by its first 16 characters (`KeyPrefix`, indexed), compares the full SHA-256 with `CryptographicOperations.FixedTimeEquals` (timing-attack defense), and checks revocation, expiry and the owning client's active flag. `LastUsedAt` is written at most once a minute via `ExecuteUpdate`, bypassing `Version`, so telemetry cannot cause concurrency conflicts under parallel requests. The resulting principal carries `azp` = the key's client, `roles` = the key's capabilities, `sub` = `service:{id}`, `name` = the key's name.

### Signing-key rotation

`POST /admin/signing-keys/rotate`: a new key is generated and signs from that moment; the old key stays valid for verification for a **1-hour grace** (in-flight access tokens live at most 15 minutes plus clock skew), then drops out of JWKS. Validation keys are cached for 5 minutes, and an unknown `kid` triggers an immediate reload with a 30-second floor (so forged kids cannot hammer the DB). Rotation therefore propagates in seconds, restart-free, across multiple instances.

## Capability model

Authorization is a set, not a rank. A principal holds any combination of four capabilities, and memberships and service keys draw from the same vocabulary:

| Capability | Grants | Typical holder |
|---|---|---|
| `content:read` | published pages and collections (`ContentRead` accepts this or `content:write`) | render keys, SSR frontends |
| `content:write` | page content and drafts, collection items and drafts | editors |
| `schema:sync` | `POST /cms/sync` only | deploy pipelines |
| `client:admin` | one client's memberships, service keys and tenant settings | tenant owners |
| `service:admin` | every `/admin/*` route on every client | operators |

The axis that matters is *who the principal is*, not how much power it has. Editing content values is always a human acting through a console; reconciling the block manifest is always a machine acting at deploy time; rendering needs neither. Collapsing the first two into one grant (as the former `cms:access` did) forced render and deploy credentials to carry each other's power, so a compromised SSR host could prune content through `/cms/sync`.

The vocabulary lives in [`CapabilityCatalog`](../src/Inscribed.Auth/Authorization/CapabilityCatalog.cs) inside the auth module, because that is where grants are minted and validated; a replacement identity provider must emit the same strings. The **policies** that consume it stay in [Program.cs](../src/Inscribed.Api/Program.cs): what counts as "may edit content" is a CMS concern and must survive replacing the identity provider.

The claim is still named `roles` and the columns are still `Roles`. That is transport and storage naming, fixed by the JWT convention that `RequireRole` reads through `RoleClaimType`; `capabilities` is the vocabulary name used by the admin API, the CLI and these docs. Renaming the columns would buy nothing and cost a schema migration.

Admin endpoints ignore `azp`: an admin manages all clients regardless of which client they logged in through, while content editing still requires a real membership on the target client. Public sites need no role at all once the client's `AllowAnonymousContentRead` flag is on; that flag is tenant policy, changed by an admin (`PUT /admin/clients/{key}`), never by sync.

That flag, and the tenant's locale list, live on the **CMS** half of a client (`clients`), not on `auth_clients`. This module owns only identity: key, name, allowed redirect origins and whether the tenant may obtain tokens. Swapping it for another provider therefore costs nothing on the content side — see [Tenancy](../README.md#tenancy-clients). `POST /admin/clients` reaches this module through `IClientIdentityStore`, which is the seam an alternative provider implements.

**Tenant administration is refused to machines.** Because `/admin/*` can mint service keys, a machine principal holding the admin role could issue itself replacements and survive revocation, which would make rotation meaningless. Two independent checks close that: both admin policies also require the `email` claim, which service-key principals never carry, and `ServiceKeyService.ValidateAsync` strips every `CapabilityCatalog.HumanOnly` entry from a key's claims even when the stored row still carries it, logging a warning naming the key. Stripping lives in `ValidateAsync` rather than the authentication handler because it is the single producer of that capability array, so any future consumer inherits the guarantee. For the same reason the bootstrap-admin allowlist only grants `service:admin` in tokens minted for `Auth:AdminClientKey`: a bootstrap admin logging in through a tenant's own client gets that tenant's membership capabilities and nothing more.

## Storage

Auth tables are prefixed `auth_*`, live in their own `AuthDbContext` with a separate migration history table (`__ef_migrations_history_auth`), and share the entity house style (private constructor, static factory, `Version` bump on mutation). Removing the module removes its schema cleanly.

## Configuration

```jsonc
"Auth": {
  "Mode": "BuiltIn",                     // BuiltIn | External
  "Audience": "inscribed-cms",
  "TenantClaim": "azp",                  // must stay azp in BuiltIn mode
  "RolesClaim": "roles",                 // dotted paths allowed, e.g. realm_access.roles
  "RoleMap": {},                         // provider role name -> capability
  "Authority": "",                       // External only; required there
  "RequireHttpsMetadata": true,          // External only; false for a plain-http trial

  "Issuer": "https://cms.example.com",   // BuiltIn: iss claim + public base URL; Google redirect URI derives from it
  "AccessTokenMinutes": 15,
  "RefreshTokenDays": 30,
  "ReuseLeewaySeconds": 30,              // 0 = strict reuse detection
  "AdminClientKey": "admin",
  "Cookie": { "Name": "inscribed_rt", "SameSite": "Lax", "Secure": true },
  "Google": { "ClientId": "", "ClientSecret": "", "CallbackPath": "/auth/google/callback" },
  "Admin": {
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
6. A `schema:sync` key gets 200 from `POST /cms/sync`; a `content:read` key gets 200 from `GET /cms/content` but **403** from `POST /cms/sync`.
7. With the client flag off, `GET /cms/public/{clientKey}/content` is 404; after `PUT /admin/clients/{key}` enables it, 200 with `Cache-Control: public`.
8. Revoking the service key turns its next request into 401 on `/cms/content`. On the three collection read routes it is 401 only for a collection without `AllowAnonymousRead`: those routes are `AllowAnonymous` and evaluate access in the handler, so an invalid key silently degrades to an anonymous caller and still gets 200 on a public collection.
9. A service key carrying an admin capability gets **403** from `GET /admin/users` and logs a warning, while the same call with a bootstrap admin's access token returns 200. Logging in through a non-admin client and refreshing yields a token without the admin role.

## Known limits

Deliberately deferred, to be revisited before production hardening: the signing private key is stored as plain PEM (the DB is the trust boundary; at-rest encryption via KMS/DataProtection is a hardening step), there is no rate limiting on `/auth/*`, expired refresh tokens are never garbage-collected, and the module has no unit tests (`RefreshTokenService`, `JwtIssuer`, `ServiceKeyService` are the hungriest).
