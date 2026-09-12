# BackendApplication — Tasks API

A heavily-commented **ASP.NET Core 10 Web API**, built as teaching material for an
undergraduate lecture. The scenario is a team task board: capture work, assign it, move it
through a status workflow, close it.

Every file is written to be read aloud. The comments explain not only what the code does,
but which alternatives were rejected and why.

It is deliberately built around **one** business resource. The point is that a single
resource still needs every layer — domain, persistence, DTOs, services, validation,
error handling, authorisation — and that seeing them on something small makes each one's
job obvious in a way that four interlocking resources does not.

---

## Run it

### Everything in Docker

```bash
docker compose up -d --build
```

| What | URL | Credentials |
|---|---|---|
| API reference (Scalar) | <http://localhost:8080/scalar> | — |
| OpenAPI document | `/openapi/v1.json` | — |
| Health | `/health/live`, `/health/ready` | — |
| PostgreSQL | `localhost:5432` | `tasksuser` / `taskspass` / `tasksdb` |

> The compose file sets `name: backendapplication` explicitly. Compose otherwise derives
> the project name from the containing directory — here the unhelpful `8`, which would
> then prefix every image and network it builds.

### Database in Docker, API in your IDE (best for breakpoints)

```bash
docker compose up -d postgres
dotnet run --project BackendApplication        # → http://localhost:5064/scalar
```

Migrations are applied and demo data seeded automatically in Development and Docker.

### Reset everything

```bash
docker compose down -v
docker compose up -d --build
```

### Smoke test

```bash
bash BackendApplication/docs/smoke-test.sh http://localhost:8080
```

It asserts against PostgreSQL as well as against HTTP status codes, because a handler can
return `201 Created` having written nothing — and a mocked repository always "saves"
successfully.

---

## Seeded accounts

| Email | Password | Role |
|---|---|---|
| `admin@tasks.local` | `Admin#12345` | Admin |
| `lead@tasks.local` | `Lead#12345` | Lead |
| `member@tasks.local` | `Member#12345` | Member |

Log in via `POST /api/v1/auth/login`, copy `accessToken`, click **Authorize** in Scalar.

---

## The API

| Method | Route | Who |
|---|---|---|
| `GET` | `/api/v1/tasks` | any authenticated user |
| `GET` | `/api/v1/tasks/{id}` | any authenticated user |
| `POST` | `/api/v1/tasks` | Lead, Admin |
| `PUT` | `/api/v1/tasks/{id}` | assignee, Lead, Admin |
| `PATCH` | `/api/v1/tasks/{id}/status` | assignee, Lead, Admin |
| `DELETE` | `/api/v1/tasks/{id}` | Admin |

Plus `/api/v1/auth/{register,login,refresh,revoke,me}` — supporting infrastructure, so the
Tasks endpoints have an identity to authorise against.

### Listing

Paging, filtering, searching and sorting all live on `GET /api/v1/tasks`:

```
GET /api/v1/tasks?search=pagination&status=InProgress&priority=High
    &assigneeId=3&overdueOnly=true&openOnly=true
    &dueBeforeUtc=2027-01-01T00:00:00Z
    &sortBy=dueat&sortDirection=asc&pageNumber=1&pageSize=20
```

`sortBy` accepts `reference`, `title`, `status`, `priority`, `dueat`, `createdat` — mapped
through a hard-coded allow-list, so the raw string never reaches SQL. `pageSize` is
clamped to 100 rather than rejected.

---

## The status state machine

| From | Allowed next |
|---|---|
| `Todo` | `InProgress`, `Cancelled` |
| `InProgress` | `Blocked`, `Done`, `Cancelled` |
| `Blocked` | `InProgress`, `Cancelled` |
| `Done` | *(terminal)* |
| `Cancelled` | *(terminal)* |

Written once as a table in `Domain/Enums/TaskItemStatus.cs`, not as `if` statements spread
across the service. An illegal move returns **409** naming the states that *are* reachable.

Two rules ride along with it:

- Moving to `InProgress` requires an assignee → **400** `assignee_required_to_start`.
- Reaching `Done` stamps `CompletedAtUtc`; leaving it clears it. A CHECK constraint
  enforces that pairing in the database too, so it holds for rows written by a migration
  or an admin script.

Every task response carries `allowedNextStatuses`, so a client never hard-codes the table.

---

## What is demonstrated

Controllers · services · DTOs · EF Core with PostgreSQL · the repository pattern ·
middleware · filters · global exception handling with RFC 9457 ProblemDetails · JWT
authentication with role **and** row-level authorisation · URL-segment API versioning ·
offset pagination with HATEOAS links · FluentValidation alongside DataAnnotations ·
optimistic concurrency via `xmin` · soft delete with a global query filter · auditing via
a SaveChanges interceptor · connection pooling · rate limiting · health checks.

### Three details worth reading the comments for

**`TaskItem`, not `Task`.** `System.Threading.Tasks.Task` is in scope in every file thanks
to implicit usings, and every `async` method returns one. A domain type sharing that name
would mean fully-qualifying it forever. Same reasoning for `TaskItemStatus` vs the BCL's
`TaskStatus`.

**Priority has no database default, but Status does.** EF decides whether to send a column
on INSERT by comparing the property to the CLR default — `0` for an enum. `TaskItemStatus.Todo`
*is* 0, so a database default of `Todo` is harmless. `TaskPriority.Low` is also 0, so a
database default of `Medium` would mean a task explicitly created as `Low` looked "unset",
the column was omitted, and PostgreSQL quietly stored `Medium`. EF warns about exactly
this; see `Data/Configurations/TaskItemConfiguration.cs`.

**Incoming due dates are forced to UTC.** The columns are `timestamp with time zone`, and
Npgsql throws rather than guess when handed a `DateTime` whose `Kind` is `Unspecified` —
which is what `2026-12-31T17:00:00` with no offset parses as. `TaskMappings.NormaliseToUtc`
handles it; without it every such request would fail with an unhelpful 500.

---

## Project layout

```
Domain/          Entities and enums. No EF attributes beyond keys and indexes.
Data/            DbContext, configurations, interceptors, repositories, seeder, migrations.
DTOs/            The wire contract. Separate from the domain on purpose.
Mapping/         Hand-written entity <-> DTO translation, compiler-checked.
Services/        Business rules. Knows nothing about HTTP.
Controllers/     Bind, delegate, shape the response. No try/catch anywhere.
Validation/      FluentValidation rules: cross-field and database checks.
Middleware/      Exception handling, correlation ids, request logging.
Filters/         Validation and pagination-header filters, registered globally.
Configuration/   Typed options, versioning constants, OpenAPI transformers.
Extensions/      DI registration and pipeline composition.
```
