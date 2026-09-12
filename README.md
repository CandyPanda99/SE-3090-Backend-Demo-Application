# BackendApplication — Tasks API

A complete, running, heavily-commented **ASP.NET Core 10 Web API** built as teaching
material for an undergraduate lecture. The scenario is a team task board: capture work,
assign it, move it through a status workflow, close it.

Every file is written to be read aloud. The comments explain not only what the code does,
but which alternatives were rejected and why.

The API is deliberately built around **one** business resource. A single resource still
needs every layer — domain, persistence, DTOs, services, validation, error handling,
authorisation — and seeing them applied to something small makes each layer's job obvious.

**Full documentation → [`BackendApplication/README.md`](BackendApplication/README.md)**

---

## Getting started

### Prerequisites

| Tool | Needed for | Check it |
|---|---|---|
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | running the stack | `docker --version` |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | building, or running the API outside Docker | `dotnet --version` |
| `curl` + `python3` | the smoke test only | preinstalled on macOS and most Linux |

Docker Desktop must actually be **running**, not just installed — `docker ps` should
print a table rather than "Cannot connect to the Docker daemon".

---

### Option A — everything in Docker (simplest)

Run these from the repository root:

```bash
# 1. Build the image and start the API and PostgreSQL
docker compose up -d --build

# 2. Watch it come up (Ctrl+C to stop watching; the containers keep running)
docker compose logs -f api

# 3. Confirm both containers are healthy
docker compose ps
```

Step 1 takes a few minutes the first time, because it downloads the .NET SDK and
PostgreSQL images. Later runs are seconds.

You are ready when `docker compose ps` shows **both** containers as `healthy`:

```
NAME                          STATUS
backendapplication-api        Up (healthy)
backendapplication-postgres   Up (healthy)
```

Then open:

| What | URL | Credentials |
|---|---|---|
| **API reference (start here)** | <http://localhost:8080/scalar> | — |
| OpenAPI document | <http://localhost:8080/openapi/v1.json> | — |
| Liveness / readiness | `/health/live`, `/health/ready` | — |
| PostgreSQL | `localhost:5432` | `tasksuser` / `taskspass` / `tasksdb` |

The database schema is migrated and demo data seeded automatically on first start —
there is no separate setup step.

---

### Option B — database in Docker, API in your IDE

Best when you want breakpoints. PostgreSQL still runs in a container; the API runs on
your machine.

```bash
# 1. Start only the database
docker compose up -d postgres

# 2. Run the API (reads appsettings.Development.json)
dotnet run --project BackendApplication
```

The API comes up on <http://localhost:5064/scalar>. Debugging in VS Code or Rider works
the same way — the solution is `BackendApplication.sln`.

---

### Try it in 30 seconds

Log in as the seeded admin and list the task board:

```bash
# Get a token
TOKEN=$(curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@tasks.local","password":"Admin#12345"}' \
  | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["accessToken"])')

# Use it
curl -s "http://localhost:8080/api/v1/tasks?pageSize=3&sortBy=dueat" \
  -H "Authorization: Bearer $TOKEN" | python3 -m json.tool
```

Or click **Authorize** at the top of <http://localhost:8080/scalar>, paste the token, and
call any endpoint from the browser.

---

### Stopping, resetting and rebuilding

```bash
docker compose stop                 # pause; data and containers kept
docker compose down                 # remove containers; DATA KEPT in the named volume
docker compose down -v              # remove containers AND delete the database
docker compose up -d --build        # rebuild after changing code
```

To start completely fresh:

```bash
docker compose down -v && docker compose up -d --build
```

---

### Smoke test

```bash
bash BackendApplication/docs/smoke-test.sh http://localhost:8080
```

43 assertions across docs, health, auth, paging, filtering, sorting, role authorisation,
validation, the status state machine, the ownership rule, soft delete, auditing and rate
limiting. It asserts against PostgreSQL directly as well as against HTTP status codes,
because a handler can return `201 Created` having written nothing — and a mocked
repository always "saves" successfully.

It needs the Docker stack running, because it reads rows back with
`docker exec backendapplication-postgres psql`.

> The script's own final section fires 15 rapid logins to prove the rate limiter works,
> which trips the 10-per-minute limit. Running it twice inside a minute fails the auth
> section — that is the limiter working, not a broken test. Restart the API
> (`docker compose restart api`) or wait 60 seconds between runs.

---

### If something goes wrong

| Symptom | Cause and fix |
|---|---|
| `Cannot connect to the Docker daemon` | Docker Desktop is not running. Start it and retry. |
| `port is already allocated` | Something else holds 8080 or 5432. Find it with `lsof -i :8080`, or change the host port in [`docker-compose.yml`](docker-compose.yml). |
| API container restarts repeatedly | Read the reason: `docker compose logs api`. Usually the database was not ready, or a migration failed. |
| `relation "Tasks" already exists` | The volume holds a database from an incompatible schema. Reset with `docker compose down -v`. |
| 401 on every request | The access token expired (60 minutes in Development). Log in again. |
| 429 responses | The rate limiter. Wait a minute, or `docker compose restart api`. |

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

### The status state machine

| From | Allowed next |
|---|---|
| `Todo` | `InProgress`, `Cancelled` |
| `InProgress` | `Blocked`, `Done`, `Cancelled` |
| `Blocked` | `InProgress`, `Cancelled` |
| `Done` | *(terminal)* |
| `Cancelled` | *(terminal)* |

An illegal move returns **409** naming the states that *are* reachable. Every task
response carries `allowedNextStatuses`, so a client never hard-codes the table above.

---

## Tests and coverage

Unit tests live in [`BackendApplication.Tests`](BackendApplication.Tests) and need nothing
running — no database, no container. They are a deliberate sample of the domain rules
rather than a full sweep of the codebase, so expect the coverage percentage to be low and
to move as the suite grows.

```bash
# Just the tests
dotnet test

# Tests plus an OpenCover report, the same way CI does it
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings --results-directory coverage
```

The report lands at `coverage/<guid>/coverage.opencover.xml`. Format and exclusions are set
once in [`coverlet.runsettings`](coverlet.runsettings) — generated EF Core migrations are
left out, since scaffolding is not a decision anyone tests.

On every push and pull request to `main`, the `Codacy Coverage Reporter` job in
[`.github/workflows/codacy.yml`](.github/workflows/codacy.yml) runs the same command and
uploads the result, following Codacy's
[coverage reporter guide](https://docs.codacy.com/coverage-reporter/). It needs one
repository secret, **`CODACY_PROJECT_TOKEN`** — the same token the security-scan job uses,
found under *Codacy → your repository → Settings → Coverage*. Without it the upload step
fails while the tests still run.

---

## Security scanning

Four free tools run against this repository, and every one of them reports into the
same place - the repository's **Security** tab. That aggregation is the point: findings
arrive from different scanners in different formats, and GitHub merges them into one
triage list. Paying products such as Kenna exist to do exactly that; on a public
repository it costs nothing.

| Tool | What it looks at | Where it is configured |
|---|---|---|
| **CodeQL** | The C# itself - injection, path traversal, unsafe deserialisation | [`security.yml`](.github/workflows/security.yml) |
| **Trivy** (config) | Dockerfile misconfiguration, committed secrets | [`security.yml`](.github/workflows/security.yml) |
| **Trivy** (image) | CVEs in the OS packages and NuGet assemblies that actually ship | [`security.yml`](.github/workflows/security.yml) |
| **Dependabot** | Vulnerable NuGet packages, stale actions, stale base images | [`dependabot.yml`](.github/dependabot.yml) |
| **Dependency review** | Fails a pull request that *adds* a vulnerable dependency | [`security.yml`](.github/workflows/security.yml) |

Codacy runs alongside these from [`codacy.yml`](.github/workflows/codacy.yml). None of it
needs a paid plan, and only Codacy needs a secret.

### The planted vulnerability

`BackendApplication.csproj` pins **SixLabors.ImageSharp 2.1.8** on purpose. It is three
years of advisories behind - two high, two moderate - and nothing in the code references
it, so no vulnerable path is reachable at runtime. It is there because a security demo
that finds nothing teaches nothing.

You can see it without any CI at all:

```bash
dotnet list package --vulnerable --include-transitive
```

It also appears as `NU1903` warnings on every build. The full explanation, including how
to remove it, is in the comment above that `ItemGroup`.

> **Before the demo:** Dependabot will open a pull request that upgrades ImageSharp and
> quietly fixes the thing you were going to demonstrate. Leave that PR unmerged until
> afterwards - then merge it live and watch the alerts close.

### What each tool will actually find

Worth setting expectations before showing this to a room:

- **Trivy (image)** - always finds something. A local run of this exact scan reported
  **17 CVEs in the Ubuntu 24.04 packages** inside `mcr.microsoft.com/dotnet/aspnet:10.0`,
  none of which any application code caused, and none of which had a fix available yet.
  That is the lesson: you can write perfect code and still ship vulnerabilities. It also
  found the **4 ImageSharp advisories** in the published output.
- **Trivy (config and secrets)** - comes back clean. The Dockerfile already runs as a
  non-root user and leaks nothing, so there is no misconfiguration to report. Worth
  showing anyway: a green scanner you have seen find real problems elsewhere is
  evidence, not decoration.
- **Dependabot** and **`dotnet list package`** - find the planted ImageSharp advisories.
- **Dependency review** - the only gate that *blocks*. It compares against the base
  branch, so it fires on the pull request that introduces a bad dependency and stays
  silent afterwards.
- **CodeQL** - likely finds little here, and that is a fair result rather than a broken
  setup. It reads the application's own code, and this codebase parameterises its queries,
  hashes its passwords and validates its input.

## What is demonstrated

Controllers · services · DTOs · EF Core with PostgreSQL · the repository pattern ·
middleware · filters · global exception handling with RFC 9457 ProblemDetails · JWT
authentication with role **and** row-level authorisation · URL-segment API versioning ·
offset pagination with HATEOAS links · FluentValidation alongside DataAnnotations ·
optimistic concurrency via `xmin` · soft delete with a global query filter · auditing via
a SaveChanges interceptor · connection pooling · rate limiting · health checks.

See [`BackendApplication/README.md`](BackendApplication/README.md) for the three subtle
details worth reading the code comments for — the `TaskItem` naming clash with
`System.Threading.Tasks`, the enum-default trap that would have silently discarded a
`Priority` of `Low`, and why incoming due dates must be forced to UTC.
