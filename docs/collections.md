# Collections

How collections are defined, validated, and enriched with external data, and why the subsystem is built this way. The consumer-facing summary lives in the [README](../README.md#collections); this document is the reference for people writing definition files or changing the collections module.

## Overview

A collection holds structured items that are not blocks on a page: news, projects, listings. Each collection is one **JSON definition document**, stored in the `collection_definitions` table as `jsonb` exactly as it was written. Items of all collections share one table, so a new collection needs **no code and no migration**.

### Where definitions live

The database is the source of truth. Files are how definitions get there the first time:

- On boot, if `collection_definitions` is **empty** and the collections directory has `*.json` files, each one is validated and imported. A broken file at this point aborts startup, because nothing has been stored yet and the operator is standing right there.
- On every later boot the directory is **ignored**, and a warning names how many files were skipped. Editing `collections/news.json` after the first boot changes nothing until it is imported.
- `Collections:Path` defaults to `collections/` relative to the working directory; the compose file mounts `./collections` to `/app/collections`. A configured path that does not exist aborts startup only while seeding is still possible; the **default** path missing is never an error.

Getting a definition in or out afterwards is a CLI job, and `collection export` is what puts one back into a file so it can live in git:

```
collection list
collection show      --key <key>
collection validate  --file <path>
collection import    --file <path> | --dir <path> [--force] [--assign-locale <code>]
collection export    --key <key> [--out <path>]
collection delete    --key <key> [--force]
```

`import` reports what the change does to stored data before it does it: fields added, dropped or retyped, a changed `slug.source`, and how many items are stored under the key. A change that drops or retypes a field is refused while items exist unless `--force` says otherwise, because those items keep values the new schema no longer accepts. `delete` removes the definition only; the items stay in the table and become unreachable, which is why it too asks for `--force`.

Two locale changes get their own warning because they are quiet rather than loud. Adding `locales` to a collection that had none leaves every stored item with no locale, so locale-filtered reads stop returning them; `--assign-locale <code>` backfills them in the same command. Removing `locales` merges what were separate languages into one list.

### Staying current

**There is nothing to reload.** A change made with `collection import` or `collection delete` is live on the very next request, in every process, with no restart, no signal and no command to remember.

The mechanism is a cache the caller validates rather than one somebody has to invalidate. Every definition row carries a `Version`. Resolving a collection reads that one number (`SELECT "Version" WHERE "Key" = …`, a single indexed row), and reuses the parsed policy it already holds when the number still matches. Only a changed number costs a document fetch and a reparse, and only for the collection that changed. Parsing is the expensive part, not reading; that is what the cache is for and what the version check protects.

The cost is one small query per request, and it buys the absence of a whole category of problem. Nothing goes stale, so nothing has to be told, which means there is no channel to keep open, no listener to reconnect, and no window in which two instances disagree about the schema. A panel can read the schema from one instance and write to another in the same second.

Within one request the resolution is memoised, so a route that consults a collection twice still pays for one query. A rebuild recreates the enricher objects, but enrichment responses survive it: the cache they read is a process-wide `IMemoryCache` keyed by credential and URL, so nothing is refetched that was not already due.

Nothing is loaded at boot. A definition is read the first time somebody asks for it and not before, so startup does no work that a request would not do anyway.

**A stored definition that fails to parse does not take the process down.** Every valid definition keeps being served, the invalid one is logged with its full error list, and it is remembered as broken rather than forgotten: reading it answers **500** naming what is wrong, instead of the **404** that would make an operator think it had been deleted. The failure is cached against the same version, so a broken document is parsed once rather than on every request, and fixing it clears the failure on the next read.

This is the trade that moving definitions into the database forces. When they lived in files, a bad definition failed the deploy, which is where someone was watching; a bad row would instead take the app down at whatever unrelated restart came next, so it must not be fatal. The defence moved to the write side: `import` parses before it stores and refuses to write anything that does not parse, and it is the only path that writes.

## Definition file reference

```jsonc
// collections/projects.json
{
  "key": "projects",
  "allowAnonymousRead": true,
  "slug": { "source": "AutoGenerated", "from": "title" },
  "displayField": "title",
  "fields": [
    { "name": "title", "type": "ShortText", "label": "Title", "required": true },
    { "name": "repo",  "type": "ShortText", "help": "owner/name on GitHub" },
    { "name": "tags",  "type": "StringArray", "filterable": true },
    { "name": "state", "type": "Select", "source": { "kind": "static", "values": ["draft", "live"] } },
    { "name": "owner", "type": "Select", "source": { "kind": "collection", "collection": "team-members" } },
    { "name": "cover", "type": "Image" },
    { "name": "links", "type": "ObjectArray", "itemFields": [
      { "name": "url",   "type": "Url", "required": true },
      { "name": "label", "type": "ShortText" }
    ]}
  ],
  "enrich": [
    {
      "url": "https://api.github.com/repos/{repo}",
      "auth": "github",
      "map": { "stars": "stargazers_count" }
    }
  ]
}
```

| Property | Required | Meaning |
|---|---|---|
| `key` | yes | route segment and storage key; lowercase alphanumerics separated by single hyphens (`team-members`) |
| `fields` | yes | the schema; at least one field |
| `slug` | no | slug strategy; omitting it means `UserDefined` |
| `displayField` | no | the `ShortText` field that names an item for humans; omitting it names items by their slug (see [References between collections](#references-between-collections)) |
| `allowAnonymousRead` | no | opt into public, CDN-cacheable reads (default `false`) |
| `locales` | no | languages this collection is written in; omitting it means the collection is not localized (see [Locales](#locales)) |
| `enrich` | no | read-time enrichment entries (see [Read-time enrichment](#read-time-enrichment)) |

JSON comments and trailing commas are allowed (the files are written by hand). Everything else is strict: an unknown property or an unknown field type anywhere in the file is a parse error, so a typo like `"requird"` cannot silently become "not required".

### Fields

| Property | Required | Meaning |
|---|---|---|
| `name` | yes | JSON property name of the value; starts with a letter or underscore, then letters, digits, underscores; unique per level, case-insensitive |
| `type` | yes | one of the field types below |
| `label` | no | editor-panel label; defaults to `name` |
| `required` | no | value must be present on publish (drafts skip this check) |
| `help` | no | hint text for the editor panel |
| `readOnly` | no | rendered as non-editable in the panel |
| `filterable` | no | enables equality filtering via query string on the list endpoint |
| `sortable` | no | enables `?sort=` on this field; only `ShortText`, `Number`, `Date` and `Select` may be sortable |
| `source` | `Select` (required), `StringArray` (optional) | where the choices come from; forbidden on every other type |
| `allowCustom` | `Select` / `StringArray` only | let an editor store a value the source does not offer (default `false`) |
| `from` | no | read this value off a referenced item instead of storing it; makes the field `readOnly` and `computed` (see [Mirrored fields](#mirrored-fields)) |
| `itemFields` | `ObjectArray` only | nested field list describing each array element |

| Type | Intent |
|---|---|
| `ShortText`, `LongText` | plain strings of varying editorial size |
| `RichText` | HTML/rich content |
| `Bool`, `Number`, `Date`, `Url` | typed scalars |
| `Select` | one value from a `source` |
| `StringArray` | many values; with a `source` they are checked, without one it is free-form tagging |
| `ObjectArray` | list of structured objects, shaped by `itemFields` |
| `Image` | fixed-shape object with `src` (`Url`) and `alt` (`ShortText`), both required whenever the field has a value |
| `Link` | fixed-shape object with `href` (`Url`, required whenever the field has a value) and an optional `label` |

Block types and field types are one vocabulary: [content blocks](../README.md#pages-and-content-blocks) carry exactly this list, so a panel widget written for a field type renders the block of the same name.

### Choices

A `Select` stores one value and `StringArray` stores many, and both take their choices from a `source`:

```jsonc
// a fixed dictionary: the stored value is the entry itself
{ "name": "state", "type": "Select", "source": { "kind": "static", "values": ["draft", "live"] } }

// a relationship: the stored value is the target item's slug
{ "name": "owner", "type": "Select", "source": { "kind": "collection", "collection": "team-members" } }
```

`source` is required on `Select`, because a list with nothing in it cannot be chosen from, and optional on `StringArray`, where its absence is free-form tagging. A `static` source rejects a write outside its `values` with **400**; `allowCustom: true` accepts anything the editor types, which is how a tag field suggests without dictating.

A `collection` source is **not** checked on write. Verifying it would mean a cross-collection read on every save, and the reference is allowed to dangle anyway: the target can be archived later (below), and a client is expected to render a slug it cannot resolve as "not found" rather than treat it as an error.

### Slug

| `slug.source` | Creation | Behavior |
|---|---|---|
| `UserDefined` (default) | `PUT /cms/collections/{key}/{slug}` | the caller supplies the slug; `PUT` upserts |
| `AutoGenerated` | `POST /cms/collections/{key}/` | the slug is derived from the field named in `slug.from`; collisions get `-2`, `-3`, … suffixes |
| `ClaimDerived` | `PUT /cms/collections/{key}/{slug}` | the slugs come from the caller's own claims; each one shows up as a **virtual item** until somebody writes it (see below) |

`slug.from` must reference a `ShortText` field: slugs derive from short human-readable titles, and deriving one from rich text, arrays, or numbers has no sensible normalization. `slug.from` is only valid with `AutoGenerated`.

### Renaming a slug

A slug is fixed once the item exists unless the definition opts in:

```jsonc
{
  "slug": { "source": "AutoGenerated", "from": "title", "editable": true }
}
```

`slug.editable` is invalid with `ClaimDerived`: those slugs are derived from the caller's claims, so a renamed item would answer to nobody. With `AutoGenerated` it means the slug can be typed by hand; it does **not** re-derive the slug when the title changes, because that would move a published URL on every typo fix.

`PUT /cms/collections/{key}/{slug}/slug` with `{ "slug": "new-slug", "version": 4 }` renames and returns the item. `version` is required, exactly as it is for any other write to an existing item. The old slug is kept as an alias pointing at the item, and the caller's autosaved draft moves with it.

Drafts belong to one editor each, so a rename moves only the renamer's own: the draft store is addressed by collection, slug and editor, and offers no way to enumerate the others holding one at that slug. Another editor's unsaved work stays at the old slug and expires there. They are not left guessing, though, because their next autosave writes to an address that is now an alias, which answers 409 naming the new slug, so a panel can repoint them without losing what they typed.

| Situation | Result |
|---|---|
| The collection is not `editable` | 400 |
| The caller cannot edit the item | 403 |
| `version` is missing or stale | 409 |
| The slug **in the path** is itself an old address | 409, `reason: "moved"` |
| A live or archived item already holds the new slug | 409, `reason: "taken"` |
| An alias of **another** item holds the new slug | 409, `reason: "alias"`; retry with `?replaceAlias=true` to take it over |
| An alias of **this** item holds the new slug (renaming back) | the alias is dropped and the rename proceeds |

Every slug conflict carries `reason` and `conflictingSlug` so a panel branches on the extension rather than on the sentence in `detail`, exactly as it already does for the archived 409. `conflictingSlug` is always **the current address of the item standing in the way**: for `taken` that is the slug you asked for, for `alias` it is the other item, and for `moved` it is the item you were trying to rename.

Aliases are per collection and never chain: each one points straight at an item, so `a → b → c` leaves two aliases resolving directly to the item, not a chain to walk. A live item always wins, because a slug can never be a live slug and an alias at the same time: `POST` skips aliased slugs when it suffixes (`-2`, `-3`, …), and `PUT` onto an aliased slug answers 409 unless you pass `?replaceAlias=true`.

**No write lands on an old address.** Every write whose **path** names a slug that is an alias answers **409** with `reason: "moved"` and `conflictingSlug` naming where the item lives now. That covers upsert, draft save, archive, restore and rename alike, so a stale tab or a hand-typed slug cannot create a second record at the old address, overwrite the wrong row, or autosave into a slot nobody will read.

The two reasons say different things and a client acts on them differently:

| `reason` | What is stale | What to do |
|---|---|---|
| `moved` | the slug in the **path**; `conflictingSlug` is where that item lives now | repoint to it and re-read, then let the user resubmit |
| `alias` | the slug in the **rename body**, which is another item's old address; `conflictingSlug` is that other item | ask the user, then retry with `?replaceAlias=true` |

Keeping them apart matters because acting on the wrong one is destructive: `?replaceAlias=true` sent to a path that is itself stale takes an address away from an item the caller never meant to touch.

Refusing rather than following is deliberate. Following an alias on a write means writing to a record the caller did not name, and it leaves the caller holding a stale address forever because nothing ever tells it the address changed. The 409 carries the canonical slug precisely so a client can repoint itself.

`DELETE /cms/collections/{key}/{slug}/draft` is the one write that still succeeds on an alias, because it deletes the caller's own draft at that address and that is exactly the cleanup a repointing client wants.

`DELETE /cms/collections/{key}/{slug}/alias` drops an alias deliberately and frees the slug for reuse.

### Reading a renamed item

`GET /cms/collections/{key}/{old-slug}` returns **200 with the item**, not a redirect. The item carries its own canonical `slug`, and the response repeats it in a `Content-Location` header.

This is deliberate. A 308 would be followed silently by every default HTTP client, so the frontend would receive the data and never learn that the address changed, which is the one fact it needs. A permanent redirect is also cached hard by browsers and CDNs, which would outlive the alias itself and defeat `DELETE …/alias`.

**What the frontend must do:** compare the slug it asked for with the `slug` on the item it got back. If they differ, the content has moved and the site should answer its own visitor with a 301 to its own new URL. Nothing in the CMS can do this for you: `/cms/collections/...` is not the address your readers use.

An alias only resolves to a live item. Once the target is archived, the old slug answers 404 to public readers rather than pointing at hidden content; editors still reach it, exactly as they still reach the archived item by its current slug.

### Claim-derived slugs

Some collections are not written by whoever happens to hold `content:write`; they are written by whoever *is* the thing being described. A club's team pages, a per-tenant settings record, a per-department page: the row belongs to someone, and who that is already sits in the token.

```jsonc
{
  "key": "teams",
  "slug": { "source": "ClaimDerived", "claim": "roles", "endsWith": "_LEADER" },
  "fields": [ /* … */ ]
}
```

An editor whose `roles` claim contains `WEB_LEADER` and `MOBIL_LEADER` gets two slugs, `web` and `mobil`. They may write those two and nothing else; nobody may invent a third.

| Property | Meaning |
|---|---|
| `claim` | required; which claim to read (`roles`, `groups`, `azp`, …) |
| `endsWith` / `startsWith` / `pattern` | optional and mutually exclusive; **at most one** |

The affix forms strip what they matched (`WEB_LEADER` → `web`) and match case-insensitively. `pattern` must carry exactly one capture group, and group 1 becomes the slug; it is compiled with `RegexOptions.NonBacktracking` and validated at startup, so a definition file cannot introduce a catastrophic-backtracking stall. With no matcher at all, every value of the claim produces a slug. Results run through the same slugifier as `AutoGenerated`, so casing and Turkish characters land the same way, and a value that slugifies to nothing is skipped.

Because the rule only names a claim, it survives replacing the identity provider: any issuer that emits the same claim keeps the collection working.

**Who may write.** `CanEdit` is the derived set plus `tenant:admin`; `CanCreate` is admin-only. An admin override exists because leaders leave and their page still needs fixing; without it a departed claim would freeze a row nobody could touch.

**Locales.** A claim value has no language, so on a localized collection the locale is appended: `web-tr`, `web-en`. Slug uniqueness is `(collection, slug)` with no locale in it, so this is what lets a claim-derived record be translated at all. Editing rights follow the base value: whoever owns `web` owns both `web-tr` and `web-en`.

The suffix is not merely cosmetic: it is the only place a claim-derived collection records which rows are translations of each other, so it is read back as one. Materialising `web-en` when `web-tr` already exists **joins that row's translation group automatically** — the caller does not pass `?translationGroup=`, because the slug already said it. Everywhere else the two axes stay as they were: an explicit `?translationGroup=` still wins, `?locale=` still decides which language is written, and a collection that declares no `locales` derives bare slugs and links nothing. This is the one place where the slug carries locale meaning, and it exists because claim-derived slugs are generated rather than authored: there is no editor to tell us that `web-en` translates `web-tr`.

**Enrichment** runs on virtual items too, against an empty object. A `{slug}` placeholder therefore still resolves, which is how a not-yet-written team page can already show its member count from an upstream API.

### Locales

A collection declares the languages it is written in. Content blocks take their locale list from the client (pushed by `POST /cms/sync?locales=…`), but collections are global rather than tenant-scoped, so each collection is its own authority:

```jsonc
{
  "key": "news",
  "locales": ["tr", "en"],   // ordered; the first is the default
  // ...
}
```

**Omitting `locales` means the collection is not localized**, and then `locale` is a no-op on every route: items are stored with no locale, listings never filter, and a `?locale=` that arrives anyway is accepted and ignored. This is what keeps a single-language collection behaving exactly as it did before locales existed.

With `locales` present:

| Route | Locale |
|---|---|
| `GET /cms/collections/{key}/` | scoped; `total` counts that locale only |
| `POST /cms/collections/{key}/` | stores the item in that locale |
| `POST`/`DELETE /cms/collections/{key}/drafts` | one new-item draft slot per locale |
| `GET`/`PUT /cms/collections/{key}/{slug}` | none needed; a slug already names one row in one language |
| `PUT /cms/collections/{key}/{slug}/draft`, `DELETE …/draft` | none needed, same reason |

Reads and writes treat an unknown `?locale=` differently. A read falls back to the default locale and reports what it actually served in the item's `locale` field, because the listing endpoint is anonymous and CDN-cacheable and a 400 there breaks the page for visitors. A write rejects it with **400**: falling back would store the item in the wrong language.

Slug uniqueness deliberately stays `(collection, slug)` with no locale in it. A record and its translation get different slugs (`yeni-urun` / `new-product`), which is correct for SEO and is what lets the per-slug routes above identify one row without being told a locale. `AutoGenerated` slugs therefore disambiguate across *all* languages: two records whose titles slugify identically get `-2` regardless of which locale they are in.

Field `label` and `help` stay in one language; translating the editor panel is out of scope.

### Who may reach a collection

Two optional keys narrow reach. Both are additive restrictions on top of the route policies, never grants:

```jsonc
{
  "clients": ["site-a", "site-b"],
  "access": {
    "read":   { "claim": "roles", "anyOf": ["content:read", "content:write"] },
    "create": { "claim": "roles", "anyOf": ["content:write"] },
    "write":  { "any": [
                 { "claim": "roles",  "anyOf": ["content:write"] },
                 { "claim": "groups", "equals": "news-editors" }
               ] }
  }
}
```

`clients` lists the tenants a collection belongs to, matched against the caller's tenant claim. Omit it and every tenant sees the collection. Items themselves are not tenant-scoped — collections are global and the table has no client column — so this is a visibility and permission rule, not data isolation.

**`access` can only refuse, never allow.** The route still requires `content:write` to write and `content:read` or `content:write` to read; a predicate runs afterwards and can turn a yes into a no. This is deliberate: a mistake in a definition then over-restricts, and no mistake can open a collection up. A branch that is absent means no extra restriction for that action, which is why leaving `access` out reproduces the old behaviour exactly.

A branch is a **claim test**, or a group of them:

| Form | Meaning |
|---|---|
| `"anyOf": ["a", "b"]` | at least one of the claim values is in the list |
| `"allOf": ["a", "b"]` | every listed value is among the claim values |
| `"equals": "a"` | a claim value is exactly this |
| `"present": true` | the claim exists at all; its value is not read |

A test names one `claim` and exactly one of those four. Groups are `{ "all": [...] }` and `{ "any": [...] }`, and their entries must be plain tests: groups do not nest, so `A and (B or C)` cannot be written. The flat form covers what these rules need in practice, because the capability half of such a formula is already enforced by the route.

Enforcement, in full:

| Caller | Collection | Effect |
|---|---|---|
| anonymous | `allowAnonymousRead` | reads normally; neither key applies |
| authenticated | `allowAnonymousRead` | reads normally; `clients` still hides it from `/cms/collections/me` and refuses writes |
| authenticated | not public, reading | out of `clients` is **404**, refused by `access.read` is **403** |
| any | not public, writing | out of `clients` is **404**, refused by `access.write` / `access.create` is **403** |

Out-of-scope answers **404** rather than 403, with the same message an unknown key gets, so a tenant cannot discover which collections exist for others. Refusal by a predicate is **403**, because the collection is legitimately visible to that tenant.

Public data stays public: a collection with `allowAnonymousRead` serves its published items to anyone, and `clients` narrows only the editing surface. For the same reason `allowAnonymousRead` together with an explicit `access.read` is a **startup error** rather than a silently dead rule.

### Translation groups

Different slugs per language mean the rows cannot recognise each other by slug, so every item carries a `translationGroupId`. A record and its translations are the rows sharing one group.

The id is assigned at creation and needs nothing from the caller:

```http
POST /cms/collections/news?locale=tr
{ "data": { "title": "Yeni Ürün" } }

201 → { "slug": "yeni-urun", "locale": "tr", "translationGroupId": "8f3f…" }
```

Every record is therefore born as the sole member of its own group. Writing a translation means joining an existing one, and the only thing the caller supplies is a value it already read off the record it is translating:

```http
POST /cms/collections/news?locale=en&translationGroup=8f3f…
{ "data": { "title": "New Product" } }

201 → { "slug": "new-product", "locale": "en", "translationGroupId": "8f3f…" }
```

`?translationGroup=` is accepted on `POST /cms/collections/{key}/`, on `PUT /cms/collections/{key}/{slug}` (create branch only), and on `POST /cms/collections/{key}/drafts`. The draft stores it so a half-written translation survives a page reload: the pending new-item draft comes back on the listing endpoint with its `translationGroupId` intact.

Reading a single item returns its siblings, which is exactly what a language switcher needs and costs one extra query:

```http
GET /cms/collections/news/new-product

200 → { "slug": "new-product", "locale": "en",
        "translationGroupId": "8f3f…",
        "translations": [ { "locale": "tr", "slug": "yeni-urun" } ] }
```

`translations` is absent entirely on collections that declare no `locales` (no query is issued), and `[]` on a localized record that has none yet. Listings never include it: that would be one query per row.

Two guards, both worth the error:

- a `translationGroup` that does not exist in this collection is a **400**, so a mistyped id cannot silently create an orphan group;
- joining a group that already holds an item in the target locale is a **409**, so one record cannot end up with two English versions and no way to tell which is right. A unique index on `(translationGroupId, locale)` enforces the same thing at the database level.

## Item lifecycle

### Listing order

Listings are ordered by `slug` unless `?sort=` says otherwise. Sortable keys are the three columns `slug`, `createdAt`, `updatedAt`, plus every schema field marked `sortable`, each taking an optional `:asc` / `:desc` (`?sort=publishedAt:desc`).

Schema fields matter here because row age is not editorial order: a news feed is ordered by the `publishedAt` an editor typed, and a back-dated import or a scheduled piece puts `createdAt` in exactly the wrong order. Only `ShortText`, `Number`, `Date` and `Select` may be declared sortable; the rest have no ordering a reader would recognise.

The values live inside the `jsonb` payload, so a schema-field sort orders on `jsonb_extract_path("Data", field)`, mapped as a database function and composed into the same EF query that filters and pages. The field name travels as a bind parameter rather than being spliced into a statement, so there is no SQL string to whitelist a name into and no second query path to keep in step with the first. Ordering on the `jsonb` value instead of its text form is what keeps types honest: `Number` compares numerically (`9` before `10`, not after), and ISO dates compare chronologically as strings.

Missing values sort last in both directions, which costs one extra `IS NULL` ordering term ahead of the value. A schema-field sort therefore scans and sorts the collection; that is the right trade at the scale collections are for, and an expression index only becomes worth discussing at tens of thousands of rows in one collection.

Ties always break on `slug`, so paging stays stable when rows share a value. An unknown key, an unsortable field, or an unknown direction is a **400** listing what is available, rather than a silent fallback.

### What an item response carries

Every item carries `createdAt` and `updatedAt`. Editor reads add `isArchived` / `archivedAt` where they apply; anonymous reads never see them.

Who wrote last is **recorded but not published**. The `sub` claim of the writer is stored on the row, so the audit trail exists, but no read returns it. A raw `sub` is a user id, not a display name, and resolving it would mean joining against a user directory the CMS deliberately does not have. Publishing the id anyway would buy a panel nothing and would make the eventual shape — an object with a name in it — a breaking change rather than an addition.

### References between collections

A field whose `source` is a collection stores the target's **slug**. A slug is not something an editor can pick from a list, and it is not what a reader should see, so a collection nominates the field that names its items:

```jsonc
{ "key": "team-members", "displayField": "fullName", "fields": [ /* … */ ] }
```

`displayField` belongs to the collection, not to the reference. Five fields pointing at `team-members` cannot then disagree about what one of its records is called, and a client that resolves a reference gets the same label everywhere. Omit it and items are named by their slug.

One endpoint serves both halves of a picker:

```http
GET /cms/collections/team-members/lookup?q=ahmet&locale=tr&limit=20
GET /cms/collections/team-members/lookup?slugs=ahmet-yilmaz,ayse-kaya

200 → { "items": [ { "slug": "ahmet-yilmaz", "label": "Ahmet Yılmaz" } ], "total": 3 }
```

`q` matches **case-insensitively anywhere** in the `displayField` (the slug, when there is none) and `total` counts every match, not the page. `slugs` resolves what is already chosen and ignores `locale`, because a slug is unique in the collection whatever language the item is written in. Sending both is **400**: one searches, the other resolves, and a request that does both says nothing about which answer it wants. Sending neither returns the first page, which is what a picker opens on. `limit` defaults to 20 and clamps to 100; `slugs` accepts at most 100 entries.

A slug that does **not** come back from `?slugs=` is a reference whose target is gone. That is the answer, not a failure: a client renders it as "not found" and offers to clear it.

This is deliberately not `GET /{key}/{slug}`. A picker needs a name and something to store, not the record: item responses carry drafts, archived rows and `virtualItems`, none of which belong in a list nobody is editing, and one request per chosen slug would be a request per row of a table.

**Filters stay exact.** `?field=value` on the listing endpoint is equality and nothing else; contains-matching lives only in `lookup`, on the one field a definition nominated. Making filters contains-match would be meaningless for booleans and numbers, and it would silently change what every `filter=` binding already in the field renders.

### Mirrored fields

A reference stores a slug, which is the right thing to store and the wrong thing to render. `from` puts a field of the referenced item into this item's own response:

```jsonc
{ "name": "author",     "type": "Select", "source": { "kind": "collection", "collection": "team-members" } },
{ "name": "authorName", "type": "ShortText", "from": { "field": "author", "path": "fullName" } },
{ "name": "authorPhoto", "type": "Image",    "from": { "field": "author", "path": "avatar" } }
```

A field with `from` is **`readOnly` and `computed`** whether or not the file says so, exactly like a field produced by [enrichment](#read-time-enrichment): it appears in the schema, it is filled on every read, and a write that sends it is ignored rather than rejected. That is what makes this free on the client: a panel that already renders enrichment output renders mirrors with no new code, and can post an item straight back without stripping anything.

`from.field` names a reference **declared beside it, at the same level**, and that reference must be a `Select` or `StringArray` whose source is a collection. `from.path` names one **stored** field of the target; a field the target itself computes is not stored, so it cannot be mirrored. Three shapes follow from the reference's own type:

| Reference | Mirror | Result |
|---|---|---|
| `Select` | any type except `ObjectArray` and `Select` | the target's value, or `null` |
| `StringArray` | must be `StringArray` | one value per reference; unresolvable ones are skipped, so the array is short rather than holed |
| inside an `ObjectArray` | as above, declared in the same `itemFields` | resolved per row |

Resolution is **batched per response**: every slug a page needs is collected first and each referenced collection is read once, so a 50-row listing costs one extra query per referenced collection, not fifty. An archived or deleted target resolves to `null`, which is the same answer `lookup?slugs=` gives for a reference whose target is gone. When the target collection is localized, the sibling in the locale being read wins, so a Turkish listing shows Turkish names.

Two things to know before reaching for it:

- **The mirror follows the read, not the keystroke.** It is filled when the item is read, so an editor who picks a different reference sees the new mirror after the save round-trip. A panel that wants the label immediately already has it from `lookup`.
- **Mirroring crosses the target's read rules.** A public collection that mirrors a field of a private one publishes that field. Nothing stops you, exactly as nothing stops `enrich` from publishing a credentialed API's response, but it is a decision you are making in the definition file rather than an accident.

### Archive and restore

`DELETE /cms/collections/{key}/{slug}?version=` **archives**; nothing is ever hard-deleted, mirroring what `POST /cms/sync` does to blocks that leave the manifest. The `version` is required and a mismatch is **409**, because discarding someone else's newer edit is exactly as destructive as overwriting it. The caller's draft for that item is dropped at the same time.

**Archiving does not consume a version.** `Version` is the version of the *content*, and archiving does not touch content; burning one would tell every other open tab that the item changed when it did not, and would force a restore to go looking for a number it was never given.

```http
DELETE /cms/collections/news/eski-haber?version=1
200 → { "collectionKey": "news", "slug": "eski-haber", "version": 1, "references": 2 }
```

`references` counts the live items that point at this slug through a `collection` source, across every collection and including `Select` fields nested inside an `ObjectArray`. **It reports; it does not refuse.** Archiving is reversible and a dangling reference renders as "not found", so a broken reference is a temporary display problem; emptying every referencing field to keep them honest would be irreversible data loss on someone else's records. A panel shows the number and asks.

So the same version archives, restores, and still publishes afterwards: a tab that held version 1 before the round trip is still holding current content when the item comes back. The response repeats the version anyway, so an "archived — undo" affordance has everything it needs without a second request.

Concurrency is not weakened by this, because it never rested on the counter moving: archive and restore both require `?version=` and answer **409** on a mismatch, the database still checks the row it read, and any write aimed at an archived item is refused outright (below). Two racing archives simply agree.

An archived item disappears from anonymous reads entirely. Editors still reach it: `?archived=true` lists the archive (anonymous callers get **403**) and a read by slug returns it with `isArchived` and `archivedAt` set.

**Every write to an archived item is refused with a distinct 409**, carrying `"reason": "archived"` and the item's current version:

```json
{ "title": "Conflict", "status": 409, "reason": "archived", "version": 2,
  "detail": "Item 'news/eski-haber' is archived; restore it before writing to it." }
```

The reason code exists because the alternative is a lie. A version conflict says "someone edited this before you"; an archived item says "this is not editable until it comes back", and a panel that cannot tell them apart shows a merge screen for a problem merging cannot solve. It covers publishing, item drafts, drafts of a not-yet-created item that would take a reserved slug, and archiving something already archived.

Slugs of archived items stay reserved: auto-generated slugs skip past them and a user-defined create is refused. A restore therefore never collides with something created in the meantime.

### Drafts of items that do not exist yet

An item draft is keyed by its slug, so autosave is a plain `PUT /cms/collections/{key}/{slug}/draft`. An item that was never created has no slug to key on, so each editor gets **one pending-draft slot per collection and locale**: `POST /cms/collections/{key}/drafts` writes it, `DELETE /cms/collections/{key}/drafts` discards it, both answer **204**, and writing again simply replaces what is there.

One slot rather than many is a product decision. An editor writes one new item at a time, and a panel resumes the slot from the listing on every load, so opening the composer again lands on the draft that is already there instead of starting a rival one.

**Known limit: the slot overwrites silently.** Two composers open on the same collection and locale share it, and the last autosave wins with no signal to the one that lost. This is accepted rather than unnoticed, and two protections were tried and rejected before it: a `draftId` echoed back on save does not catch it, because both tabs read the same draft and carry the same id, and a per-tab slot turns "resume where you left off" into "pick which of your four drafts you meant", which is the behaviour the single slot exists to avoid. What the server does give a client is the detection it needs: the pending entry's `updatedAt` is the slot's last write, so a composer that remembers the value it loaded can compare it on the next listing and warn before it clobbers a newer draft. Preventing it server-side needs an explicit revision on the write, and that is a contract change nobody has asked for yet.

The pending draft comes back from the listing endpoint inside `virtualItems` (below), beside `items` rather than among them. Every editor request returns it whatever `?offset=`, `?sort=` or the filters say: the slot belongs to an editor and a collection, not to whatever the list is currently showing, and a composer that happens to pass a filter must not silently open an empty form. The one exception is `?archived=true`, which is a different view altogether: the archive answers for items that were taken down, and something never published belongs to neither. There is deliberately no second endpoint for reading the draft: one slot, one way to fetch it.

It used to be appended to `items` instead, as a row with an empty `id` and `version: 0`, so a client that recognised it by that sentinel now reads the sibling array instead.

### Virtual items

Two things can be true of a row an editor sees: it exists, or it is something the editor may bring into existence. The second kind has no database row, no id and no version, and there are exactly two of them — the pending draft above, and the slugs a `ClaimDerived` collection derives from the caller. They arrive together in one `virtualItems` array so a panel concatenates two sources instead of three:

```jsonc
"virtualItems": [
  { "origin": "pending", "canEdit": true,
    "data": {}, "draftData": { "title": "Half-written" }, "updatedAt": "…" },
  { "origin": "derived", "canEdit": true,
    "slug": "web", "data": { "memberCount": 12 }, "draftData": null }
]
```

There is no `id`, and no `version` except on the archived entry described below, because nothing here has a row to identify or a revision to conflict with. `version` appears exactly where it is actionable rather than as a placeholder. The key is `(origin, slug)`, unique within the array; a pending entry never carries a slug and so is unique on `origin` alone, since there is only ever one slot. Earlier drafts of this design carried `id: "00000000-…"` and `version: 0` as sentinels; separating the array made the sentinel redundant, and a shared id would have collided every key, selection and dedupe the panel builds on it.

`virtualItems` does not paginate. It is the same array at any `?offset=`, so a client walking pages should **replace** it and append only `items`.

Both kinds share one convention, the same one the old inline rows used: `data` is the published side (empty, plus whatever enrichment adds) and `draftData` is what the editor typed. There is no `createdAt`, and `updatedAt` appears only on the pending draft, because nothing here has been created yet.

Derived entries are suppressed once the slug belongs to a live item, so a virtual row never offers a write that would be refused. The check runs against the whole collection in one query rather than the current page, which is where the pre-`0fe716e` implementation got it wrong: on page two it re-offered rows that already existed.

An **archived** item is the one case where the slug is taken but the row still appears, carrying `isArchived: true` and the `version` restore needs:

```jsonc
{ "origin": "derived", "slug": "web", "canEdit": true,
  "data": { "memberCount": 12 }, "isArchived": true, "version": 3 }
```

Dropping it instead would make the row invisible in every default view — the normal listing excludes archived items and the virtual row was suppressed by the reserved slug — so the owner would have no way to learn that the slug they own is sitting in the archive, and no way to get it back except guessing that `?archived=true` holds something of theirs. The entry says "this slug is yours and its content is in the archive"; the action it offers is `POST /{key}/{slug}/restore?version=`, not a write. It carries no `draftData`, because archiving discards the item draft and refuses new ones, and its `data` is the enriched empty object rather than the archived content: a single `GET /cms/collections/{key}/{slug}` returns that to an editor when the restore screen needs it.

`origin` is not decoration: it decides which discard endpoint applies, and whether the write has a slug to aim at.

| Entry | Write it | Discard its draft |
|---|---|---|
| `pending` on `AutoGenerated` | `POST /cms/collections/{key}/` | `DELETE /cms/collections/{key}/drafts` |
| `pending` on `UserDefined` | `PUT /cms/collections/{key}/{slug}`, with the slug the composer typed | `DELETE /cms/collections/{key}/drafts` |
| `derived` | `PUT /cms/collections/{key}/{slug}` | `DELETE /cms/collections/{key}/{slug}/draft` |

**A pending entry never carries a slug**, and `POST /cms/collections/{key}/drafts` does not accept one. The slot exists precisely because the item has no address yet, and it is already addressed by collection, locale and editor, so a slug stored in it would be content rather than identity. A `UserDefined` composer keeps the slug it typed in its own form state and supplies it at publish; the cost is that a reload restores the fields but not the slug, which is the price of a slot whose lifecycle actually closes. It closes because whichever call creates the item clears the slot, `POST` and `PUT` alike, with no stored slug to reconcile against one that may have been retyped in between.

A derived entry's draft lives in the ordinary per-slug slot instead, because its slug was never in doubt. `POST /cms/collections/{key}/drafts` on a `ClaimDerived` collection is refused with **400** naming `PUT …/{slug}/draft`, rather than writing to a slot nothing reads back: one slot per editor cannot hold a leader's `web` and `mobil` at once, and the listing looks for those drafts under their slugs.

## Startup validation

A misconfigured collection that is silently skipped shows up as missing data in production with no signal pointing at the cause. So the loader refuses to boot instead: every rule violation is collected and the app fails with one error listing **every problem in every file**, each prefixed with its file name; you fix them all in one restart rather than discovering them one boot at a time.

The full rule set:

- the file must parse; unknown properties and unknown enum values are parse errors
- `key` is required, matches the pattern above, and is unique across all files (the error names both files)
- `fields` is non-empty; names are valid and unique; `itemFields` is required on `ObjectArray` and forbidden elsewhere
- `source` is required on `Select`, allowed on `StringArray`, and forbidden elsewhere; a `static` source lists at least one non-empty, non-duplicate value and a `collection` source names a valid collection key; `allowCustom` is only for those two types
- `slug.source` must be a known value; `slug.from` must exist in the schema with an allowed type
- `displayField`, if present, names a `ShortText` field of this schema
- `from`, if present, names a collection-sourced `Select` or `StringArray` field at the same level, carries a plain field name in `path`, and does not sit on a field that also declares `source`
- `locales`, if present, is non-empty with unique entries matching the `key` pattern (omit the property entirely for a single-language collection)
- every `enrich` entry passes the checks in the next section

## Read-time enrichment

An `enrich` entry augments items with data fetched from an external API **whenever the API returns items** (listings, single reads, and the responses of create/update). The fetched values exist only in responses; **stored data is never touched**. External data has its own lifecycle, and persisting a snapshot of it would go stale and pollute item versioning.

```json
"enrich": [
  {
    "url": "https://api.github.com/repos/{repo}",
    "auth": "github",
    "cacheSeconds": 300,
    "map": {
      "stars": "stargazers_count",
      "ownerAvatar": "owner.avatar_url",
      "firstTopic": "topics[0]"
    }
  }
]
```

The design is deliberately declarative: "issue a GET, pick fields from the response" covers the common cases with zero code, and everything a file can express is validated at boot. Anything beyond that (transforms, combining sources, non-GET calls) belongs in code behind the [seams](#code-seams), not in an ever-growing file dialect. Because parsing is strict, an `enrich` option this version does not support fails at boot instead of being silently ignored.

The `access` predicates are the one deliberate exception to that restraint. Permission rules are genuinely declarative — they read claims and answer yes or no — and pushing them into code would mean a deploy every time a collection changes hands. The exception is kept narrow on purpose: predicates cannot nest, cannot call out, and cannot grant, so the dialect has a ceiling it cannot grow past.

### URL templates

`{placeholders}` are filled from the item's own scalar fields, or `{slug}`. Values are URL-encoded. Placeholder names are **case-sensitive** and validated against the schema at startup, because the runtime lookup against item data is case-sensitive; a case mismatch that validated loosely would produce empty URLs in production. Allowed placeholder field types: `ShortText`, `LongText`, `Url`, `Number`, `Bool`, `Date`.

If a placeholder's value is missing or empty on a given item, the request is **skipped** and the item is returned unenriched (no log entry; an item without a `repo` simply has no GitHub data).

### Response mapping

`map` keys are the fields added to the item. A value is either a dotted path into the response JSON, with optional array indices, or an object that also declares how the panel should render the result:

```jsonc
"map": {
  "stars":       "stargazers_count",
  "ownerAvatar": { "path": "owner.avatar_url", "type": "Url", "label": "Owner avatar" }
}
```

The shorthand means `ShortText` with the target name as its label, which is right for plain text and wrong for everything else: a URL rendered as `ShortText` puts the address on screen instead of the image. Declare the type whenever the value is not prose. Every field type except `ObjectArray` and `Select` is allowed: a map entry has no way to describe an array's item shape, and no way to say where a choice list comes from.

Targets must not collide with schema fields (they would shadow editor-authored content in responses) and must be unique across all `enrich` entries of the collection. A path that does not resolve in a particular response leaves that one field absent; the other mapped fields are still applied.

Every target is published on the schema as an ordinary entry in `fields`, marked `readOnly` and **`computed`**, so a panel renders it through the paths it already has and its payload builder drops it without special-casing. It is one list rather than two because the descriptors were always the same shape; `computed` is the flag that says where the value comes from, and it is not something a definition file may declare, only something enrichment produces. Writes **ignore** those names: read an item, change one field, send the whole object back. They are dropped instead of stored, so "stored data never holds external values" still holds. Anything else the schema does not know is still a **400**, so a typo in a real field name cannot pass itself off as computed.

### Caching and resilience

Enrichment runs **per item**, so a 50-item listing means up to 50 upstream calls; without defenses the external API's latency and outages would become the CMS's. The defenses are defaults, not options:

| Behavior | Value |
|---|---|
| Response cache | `cacheSeconds`, default 300, `0` disables, max 86400; keyed by credential + resolved URL, in-memory per instance |
| Request timeout | 3 seconds |
| Non-2xx, timeout, or parse failure | item returned **unenriched**; warning logged with URL and status |

A read endpoint never fails because an enrichment source is down.

## Outbound credentials

`auth` in an `enrich` entry is a **name**; the actual credential lives in configuration, never in the collection file (the collections directory is mounted and typically committed to git; secrets do not belong there). Referencing an unknown name, or defining an incomplete credential, aborts startup.

```jsonc
// appsettings, or environment variables in compose
"Enrichment": {
  "Auth": {
    "github":       { "Type": "ApiKey", "Header": "Authorization", "Value": "Bearer ghp_xxx" },
    "internal-api": {
      "Type": "OAuth2ClientCredentials",
      "TokenEndpoint": "https://login.example.com/oauth/token",
      "ClientId": "inscribed-cms",
      "ClientSecret": "xxx",
      "Scope": "read:stats"              // optional
    }
  }
}
```

In compose, secrets travel as environment variables: `Enrichment__Auth__github__Value=Bearer ghp_xxx`.

| Type | Required keys | Optional keys | Behavior |
|---|---|---|---|
| `ApiKey` | `Header`, `Value` | | sends the header verbatim on every request |
| `OAuth2ClientCredentials` | `TokenEndpoint`, `ClientId`, `ClientSecret` | `Scope`, `AssumeLifetimeSeconds` (default 300) | fetches and caches bearer tokens itself |

`Value` is the complete header value including any scheme (`Bearer ghp_xxx`, or a bare key for APIs without one); a separate scheme field would only get in the way of the many APIs that use none.

### Token lifecycle

- Tokens are cached **in memory only**, per instance. Client-credentials tokens are cheap to re-fetch, so persisting them anywhere would widen the secret surface for no benefit; each instance simply fetches its own.
- A token is refreshed 60 seconds before expiry (or at half its lifetime for very short tokens). Expiry comes from the token response's `expires_in`; identity providers that omit it get `AssumeLifetimeSeconds`.
- Refresh is **single-flight**: concurrent enrichments during a refresh wait for one token request instead of stampeding the token endpoint.
- On a `401` from the target API the cached token is dropped and the request is retried **once** with a fresh token (covers early revocation). The retry only applies to body-less requests, which is all enrichment ever sends.
- Token requests go through a dedicated HTTP client (5-second timeout) so the credential handler never applies itself to its own token request.
- If the token endpoint itself is down, the enrichment fails like any other request: warning in the log, item returned unenriched.

### Secret hygiene

Log lines and error messages carry the credential **name** and a status code, never the value; token values are never logged. Raw secrets exist only in configuration.

> **Trust note:** an `enrich` entry directs server-side HTTP requests, which is server-side request forgery by design. Treat the collections directory with exactly the trust you give configuration: only operators write to it.

## Code seams

All collections are defined by a document, but two interfaces remain as the seams for behavior a document cannot express:

```csharp
public interface ICollectionPolicy
{
    string Key { get; }
    CollectionSchema Schema { get; }
    SlugSource SlugSource { get; }
    bool AllowAnonymousRead { get; }
    bool AppliesTo(string? tenant) => true;
    bool CanRead(ClaimsPrincipal user) => true;
    bool CanEdit(ClaimsPrincipal user, string slug);
    bool CanCreate(ClaimsPrincipal user);
    string? GetSlugSourceValue(JsonNode data);
    Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default);
}

public interface ICollectionEnricher
{
    Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default);
}
```

`CanEdit` and `CanCreate` need one fact they cannot derive from the definition file: whether the caller is an administrator, and so may act on a claim-derived item that is not theirs. That comes from [`IAdministratorPolicy`](../src/Inscribed.Application/Contracts/Identity/IAdministratorPolicy.cs), implemented by whichever auth module is installed. Collections therefore name no capability of their own; swap the auth module and the ownership override follows its vocabulary.

`AppliesTo` and `CanRead` default to `true`, which is exactly what a definition without `clients` or `access` means: no narrowing, and the route capability gate still decides first. A policy written in code therefore opts into scoping only by overriding them.

`FileCollectionPolicy` implements `ICollectionPolicy` from a parsed definition and composes one `ICollectionEnricher` per `enrich` entry (the default factory produces `HttpEnricher`). Per-user permissions, computed fields, or non-HTTP data sources are implemented against these interfaces and registered in DI; the resolver treats code-registered and file-loaded policies identically and rejects duplicate keys at startup with an error naming both sources.

## Troubleshooting

| Symptom | Cause |
|---|---|
| App will not start, `Invalid collection definition(s) in '…'` | seeding is still pending and one or more files failed validation; every error names its file, fix all listed |
| App will not start, `Collections path '…' does not exist` | an explicitly configured `Collections:Path` points at a missing directory, while the table is still empty |
| A collection answers **500** with `Collection Misconfigured` | its stored definition does not parse; the log line from the first read and `collection show --key` both list the errors, and importing a fixed document clears it on the next read |
| An imported definition does not show up | it does, on the next request; if it genuinely does not, `collection list` will show whether the import reached the table at all |
| A collection answers **404** for one tenant and works for another | its `clients` list does not name that tenant; the 404 is deliberate so the collection is not discoverable |
| An editor with `content:write` gets **403** on a collection | an `access` predicate refused them; `collection show --key` prints the rule |
| Renaming a slug answers **400** | the definition does not set `slug.editable`, or it is a `ClaimDerived` collection where the setting is not allowed |
| A write answers **409** with `reason: "moved"` | the slug in the path is an old address; `conflictingSlug` says where the item lives now, repoint and re-read |
| Renaming answers **409** with `reason: "alias"` | the target slug is held by an old address of another item; `?replaceAlias=true` takes it over |
| The old URL still works after a rename | that is the alias doing its job; the response carries the canonical `slug` and a `Content-Location` header, and drop the alias with `DELETE …/alias` when you want the old address gone |
| Editing a definition file changes nothing | the table is already seeded, so files are ignored; use `collection import --file` |
| A collection is missing right after startup | on a fresh database with no definition files, nothing was seeded; import one |
| Items load but enrichment fields are absent | check logs for `Enrichment request to … returned/failed` warnings; an empty placeholder field on the item also skips the request, silently |
| Every read is slow for one collection | enrichment cache is likely disabled (`cacheSeconds: 0`) or the map targets vary per item; check upstream latency |
