# Contributing to Inscribed

Thanks for wanting to work on Inscribed. This document explains how the codebase is organized, how to get a development environment running, and the conventions changes are expected to follow. Read [Philosophy](#philosophy) first; it answers most "where does this go?" questions before they come up.

## Table of contents

- [Philosophy](#philosophy)
- [Prerequisites](#prerequisites)
- [Getting started](#getting-started)
- [Project layout](#project-layout)
- [Build and run](#build-and-run)
- [Database migrations](#database-migrations)
- [Testing](#testing)
- [Code style and conventions](#code-style-and-conventions)
- [Common tasks](#common-tasks)
- [Commit conventions](#commit-conventions)
- [Pull requests](#pull-requests)
- [License of contributions](#license-of-contributions)

## Philosophy

1. **Content code never depends on auth.** `Inscribed.Application` and `Inscribed.Infrastructure` have no compile-time reference to `Inscribed.Auth`; the only bridge is the claim contract (`sub`, `azp`, `roles`, `name`, `email`) read in the API layer. The tell you are violating it: adding a `using Inscribed.Auth…` anywhere outside `Inscribed.Api`.

2. **`Program.cs` is the only place that wires things together.** Authorization policies, CORS, DI composition and endpoint mapping live in the composition root. The tell: an authorization policy or role name being defined inside a module instead of `Program.cs`.

3. **Entities protect their own invariants.** Domain and auth entities use a private constructor, a static factory (`Create`, `Issue`) that validates arguments, and mutation methods that bump `Version` for optimistic concurrency. The tell: a public setter, or code outside the entity mutating its state field by field.

4. **Secrets are stored as hashes, shown once.** Refresh tokens and service keys persist only as SHA-256; the raw value appears exactly once, in the response of the call that created it. The tell: any column or log line holding a raw token.

5. **The backend is the source of truth for structure; sync is whole-state.** `POST /cms/sync` reconciles, it does not patch, and it never hard-deletes (archive/restore instead). The tell: a change that makes sync outcomes depend on request order, or a `Remove()` call on published content.

6. **Failure is explicit and typed.** Business failures throw `ValidationException`, `NotFoundException`, `ConcurrencyConflictException`, or `UnauthorizedAccessException`; the [GlobalExceptionHandler](src/Inscribed.Api/Middleware/GlobalExceptionHandler.cs) maps them to problem-details responses. The tell: an endpoint building its own error JSON, or returning 200 with an error field.

## Prerequisites

- .NET SDK 9.0
- Docker (for PostgreSQL 17 and Redis 7; installing them natively also works)
- A Google OAuth client if you need to exercise login flows (many changes don't; a service key covers most API testing)

## Getting started

```sh
git clone <repo-url> inscribed-dotnet
cd inscribed-dotnet

# infrastructure only; the API itself runs from source
docker compose up -d db redis

# the CMS connection string is not in appsettings.json; supply it via env
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=inscribed_cms;Username=postgres;Password=<DB_PASSWORD from .env>"
# PowerShell: $env:ConnectionStrings__Default = "Host=localhost;..."

dotnet run --project src/Inscribed.Api
# → http://localhost:5129 (launchSettings), ASPNETCORE_ENVIRONMENT=Development
```

On startup the app migrates both database schemas, creates a signing key if none exists, and seeds the `admin` client, so a fresh clone reaches a working state with no manual SQL. Verify with:

```sh
curl http://localhost:5129/.well-known/jwks.json
```

To iterate against a real consumer (an editor panel or site), add its origin to `Cors__AllowedOrigins` and, for login, to the client's `AllowedRedirectOrigins`. For local login flows set `Auth__Cookie__Secure=false`; different `localhost` ports count as same-site, so the default `SameSite=Lax` works (see [docs/auth.md](docs/auth.md#configuration) for the deployment cookie rules).

## Project layout

```
src/
  Inscribed.Domain/            # entity base + CMS entities, enums, typed exceptions; no dependencies
    Entities/                  #   ContentBlock, CollectionItem, Entity (Id/CreatedAt/UpdatedAt/Version)
    Enums/                     #   BlockType
    Exceptions/                #   ValidationException, NotFoundException, ConcurrencyConflictException
  Inscribed.Application/       # CMS business logic; knows nothing about auth or storage engines
    Contracts/                 #   requests/responses, repository + draft-service interfaces
    Contracts/Policies/        #   ICollectionPolicy: the collection extension point
    Contracts/Schemas/         #   CollectionSchema, FieldDefinition, FieldType, SlugSource
    Services/                  #   ContentService (sync/publish/drafts), CollectionService
    Services/Helpers/          #   schema validation, filter parsing, slug normalization/generation
    Services/Policies/         #   policy resolver + file-based collection loader and policy
  Inscribed.Infrastructure/    # storage implementations for the Application contracts
    Storage/                   #   CmsDbContext, EF configurations, repositories
    Cache/                     #   Redis draft services
    Migrations/                #   CmsDbContext migrations
  Inscribed.Auth/              # self-contained identity provider module
    Entities/                  #   User, Client, Membership, RefreshToken, ServiceKey, SigningKey
    Services/                  #   JwtIssuer, SigningKeyStore, GoogleLoginService, RefreshTokenService, ServiceKeyService
    Authentication/            #   JwtBearer config, ServiceToken handler, scheme selection
    Endpoints/                 #   /auth/*, /admin/*, /.well-known/jwks.json
    Storage/                   #   AuthDbContext (auth_* tables), repositories, own migrations
  Inscribed.Api/               # composition root: Program.cs, policies, CMS endpoints, error handler
docs/                          # in-depth guides (auth.md: identity internals and rationale)
```

## Build and run

```sh
dotnet build Inscribed.sln
dotnet run --project src/Inscribed.Api          # from source
docker compose up -d --build                    # full stack as deployed
```

Caveats (important):

- **Migrations apply automatically at startup** for both contexts. You never run `dotnet ef database update` in normal development; starting the API is the update. This also means starting the API against a shared database applies your half-finished migration to it.
- **Startup is fail-fast on config.** `AuthOptions` are validated at boot (`ValidateOnStart`); in `Production` a `localhost` issuer aborts startup by design. If the app won't start, read the first exception, it is usually configuration.
- **Redis is not optional.** Drafts and the OAuth `state`/PKCE handshake live there; without Redis, login and draft endpoints fail even though the app starts.
- **JSON columns require dynamic JSON.** `NpgsqlDataSourceBuilder.EnableDynamicJson()` in [Infrastructure/DependencyInjection.cs](src/Inscribed.Infrastructure/DependencyInjection.cs) is what lets `JsonNode` properties map to `jsonb`; new data sources must do the same or writes fail at runtime, not compile time.

## Database migrations

Two contexts, two migration streams, one database. Always pass the right `--project`/`--context` pair; the startup project is always the API:

```sh
# CMS schema (content blocks, collection items)
dotnet ef migrations add <Name> \
  --project src/Inscribed.Infrastructure --startup-project src/Inscribed.Api \
  --context CmsDbContext

# Auth schema (auth_* tables, history table __ef_migrations_history_auth)
dotnet ef migrations add <Name> \
  --project src/Inscribed.Auth --startup-project src/Inscribed.Api \
  --context AuthDbContext --output-dir Storage/Migrations
```

Rules of thumb: never edit an already-committed migration (add a new one); keep a migration in the same commit as the entity/configuration change that requires it; auth tables keep their `auth_` prefix.

## Testing

There is **no test project yet**; adding one (xUnit under `tests/`) is welcome and overdue. The components most in need of coverage, in order: `ContentService.SyncAsync` (the reconcile matrix: create/archive/restore/prune), `CollectionSchemaValidator`, `RefreshTokenService` (rotation, reuse detection, leeway), `JwtIssuer`/`SigningKeyStore` (rotation grace), and `ServiceKeyService`. Until then, changes are verified by exercising the flow end to end; the [smoke-test chain in docs/auth.md](docs/auth.md#smoke-test-chain) is the reference sequence.

## Code style and conventions

- **Match the surrounding code.** When in doubt, imitate the file you are editing.
- **No code comments.** The codebase is deliberately comment-free; names, types and structure carry the meaning. Put the "why" in the PR description or in `docs/`, not in the source.
- **Contracts are `sealed record`s** in `Application/Contracts`; entities are `sealed class`es with private constructors and factories.
- **Endpoints stay thin.** They read claims, validate presence of required inputs, call one service method, and translate the result; business rules and error decisions belong in `Application` services or entities.
- **Endpoints reference policy names, never role names.** `RequireAuthorization("CmsAccess")`, not `RequireRole("cms:access")`; the mapping lives in `Program.cs`.
- **Async all the way down**, with `CancellationToken` accepted (defaulted) on every service and repository method.
- **Time is passed in, not sampled.** Entity methods take `DateTime utcNow` as a parameter; call sites sample `DateTime.UtcNow` once per operation.
- **Every mutation bumps `Version`.** New entity methods that change state must increment it, or optimistic concurrency silently stops protecting that path.
- **Nullable reference types are on everywhere**; no `!` to silence warnings without a reason the reviewer will accept.

## Common tasks

### Add a new collection

Drop a JSON definition file into the collections directory (`Collections:Path`, default `collections/`; use [collections/news.json](collections/news.json) as the template) and restart the API. Definitions are validated strictly at startup by [FileCollectionPolicyLoader](src/Inscribed.Application/Services/Policies/FileCollectionPolicyLoader.cs); a broken file aborts boot with an error naming the file.

No migration is needed: items of all collections share the `CollectionItem` table, and endpoints/validation pick the new collection up from the loaded policy. `ICollectionPolicy` remains the internal seam behind the loader; behavior that a JSON file cannot express (custom permissions, enrichment) belongs there.

### Add a field type

1. Add the value to [FieldType](src/Inscribed.Application/Contracts/Schemas/FieldType.cs).
2. Teach [CollectionSchemaValidator](src/Inscribed.Application/Services/Helpers/CollectionSchemaValidator.cs) how to validate it.
3. If it should be filterable, add its query-string parsing to [CollectionFilterParser](src/Inscribed.Application/Services/Helpers/CollectionFilterParser.cs).
4. Update consuming panels (the schema endpoint exposes the new type string as-is).

### Add an endpoint

1. Pick the group: content in [CmsEndpoints](src/Inscribed.Api/Endpoints/CmsEndpoints.cs), collections in [CollectionEndpoints](src/Inscribed.Api/Endpoints/CollectionEndpoints.cs), auth/admin in `Inscribed.Auth/Endpoints/`.
2. Guard with an existing policy (`CmsRead` for reads, `CmsAccess` for writes, `AdminAccess` for administration); a new kind of permission means a new policy in `Program.cs`, not a role check in the endpoint.
3. Read identity only via [UserPrincipalExtensions](src/Inscribed.Api/Authentication/UserPrincipalExtensions.cs) (`GetClientId`, `GetUserSub`) and pass values into the service; services never touch `HttpContext`.
4. Set cache headers explicitly for anonymous-readable endpoints (`Vary: Authorization`, `public` vs `private, no-store`).

### Add an auth entity or column

1. Change the entity in `src/Inscribed.Auth/Entities/` following the private-ctor/factory/`Version` pattern.
2. Update its `IEntityTypeConfiguration` in `Storage/Configurations/`.
3. Add an `AuthDbContext` migration (see [Database migrations](#database-migrations)).
4. If it affects tokens or claims, update [docs/auth.md](docs/auth.md) in the same PR.

## Commit conventions

Conventional Commits, imperative present mood, one focused commit per feature:

| Prefix | Use for |
|---|---|
| `feat:` | new user-visible capability |
| `fix:` | bug fixes |
| `refactor:` | behaviour-preserving restructuring |
| `chore:` | tooling, deps, config, infra |
| `docs:` | documentation-only changes |
| `test:` | tests only |
| `style:` | formatting only |

- Subject is the header only, ≤ ~70 chars: `feat: add read-only cms:read role for service keys`.
- Body is a `- ` bullet list of what changed, imperative present ("Add", "Move", "Stop"), not past tense.
- **Breaking changes** (the project is pre-1.0, they are allowed but must be flagged): append `!` to the type **and** add a `BREAKING CHANGE:` footer paragraph describing the migration path.
- Cleanup that precedes a feature is its own commit (`refactor: …` then `feat: …`); a cohesive feature touching many files is still one commit.
- Docs change in the same commit as the API they describe; `docs:` is only for documentation-only commits.

## Pull requests

1. Branch from `main`; keep the PR to one concern.
2. `dotnet build Inscribed.sln` passes; exercise the affected flow against a running stack (see [Testing](#testing)).
3. New config keys appear in three places: `AuthOptions` (or the relevant options type), `.env.example`, and the README [configuration table](README.md#configuration-reference).
4. API-visible changes update the README endpoint table and, where relevant, `docs/`.
5. Call out breaking changes in the PR description, not just the commit footer.
6. Explain non-obvious decisions in the description; remember the source itself stays comment-free.

## License of contributions

Inscribed is licensed under the [GNU LGPL v3.0](LICENSE). By submitting a contribution you agree that it is provided under the same license (inbound = outbound); no separate CLA is required.