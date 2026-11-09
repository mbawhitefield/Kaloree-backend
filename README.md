# Kaloree backend

ASP.NET Core 10 / PostgreSQL backend for a Flutter nutrition application. A modular monolith and independently deployable worker share one versioned domain model. See [architecture](docs/architecture.md), [API contract](docs/api.md), and [deployment and security runbook](docs/operations.md).

The generated [OpenAPI contract](docs/openapi.json) is ready for client tooling. The [verification record](docs/verification.md) distinguishes completed checks from deployment-specific checks still required.

## Implemented

- Built-in ASP.NET Core Identity registration/login, verified email, password recovery, authenticator MFA and session revocation; explicit account provisioning and owner-scoped access. Optional OIDC mode remains available.
- Adult profile, Mifflin-St Jeor BMR/TDEE estimates, calorie and macro targets.
- Versioned meal diary with immutable nutrition snapshots; weight and water tracking; daily summaries.
- Custom foods, indexed local food search, Open Food Facts barcode lookup with source attribution.
- Recipes and premium meal planning.
- Premium entitlements reconciled with RevenueCat; signed, replay-resistant, deduplicated webhooks.
- Photo consent, encrypted temporary image storage, atomic monthly/global quotas, idempotent submission.
- Durable PostgreSQL job processing, leases, fencing, retry limits, terminal failure states and retention cleanup.
- OpenAI Responses vision adapter with strict structured outputs and bounded validation. Estimates must be explicitly reviewed and saved through the diary API.
- Paginated data export and cascading account-data deletion.
- Docker, Railway configuration, schema migrations, dependency lock files and CI.

## Local startup

Install .NET 10 and Docker Desktop. Copy `.env.example` to `.env` and generate random values for its two variables. The development signing key needs at least 64 characters.

```powershell
docker compose up --build -d
```

The API runs at `http://localhost:8080`; `/health/ready` checks the database and schema. Local OpenAPI: `/openapi/v1.json`. Compose deliberately disables paid external services.

Generate a local token using the same signing key as `.env`:

```powershell
$token = ./scripts/New-DevToken.ps1 -Subject 'local-user-1' -SigningKey $env:DEV_SIGNING_KEY
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/v1/account -Headers $headers
```

Development tokens are accepted only in the `Development` environment. Production defaults to built-in C# authentication backed by PostgreSQL; no external login provider is required. See [authentication setup and client contract](docs/authentication.md). Configure SMTP and the public API origin to enable registration/verification/recovery. The API can start before email is configured; email-dependent endpoints return 503 until configured. The local Compose token flow above is separate from the built-in login flow.

For running directly without Docker, set `ConnectionStrings__Database`, `ASPNETCORE_ENVIRONMENT=Development`, `Auth__DevelopmentTokens=true`, and `Auth__DevelopmentSigningKey`, then:

```powershell
dotnet run --project src/Kaloree.Api -- --migrate
dotnet run --project src/Kaloree.Api
```

## Tests

```powershell
dotnet restore tests/Kaloree.Tests --locked-mode
dotnet test tests/Kaloree.Tests
```

For full integration tests, supply an isolated PostgreSQL connection with database-creation privileges:

```powershell
$env:TEST_DATABASE = 'Host=localhost;Database=postgres;Username=postgres;Password=YOUR_LOCAL_TEST_PASSWORD'
dotnet test tests/Kaloree.Tests
```

The test fixture creates and drops a uniquely named database. Never use production credentials. Database tests are explicitly skipped if `TEST_DATABASE` is absent; CI supplies PostgreSQL and runs them. Tests cover token rejection, ownership, version conflicts, concurrent quota reservations, scan idempotency, leases/fencing, webhook signature/deduplication, encryption and deletion.

## External configuration still required

An operator can load an approved global food dataset with `dotnet run --project src/Kaloree.Api -- --import-foods PATH.json`. The file is an array of `{ "id": "stable-uuid", "source": "source name and attribution", "food": { "name": "...", "barcode": null, "calories100g": 0, "protein100g": 0, "carbs100g": 0, "fat100g": 0 } }`. Use source-appropriate, verified nutrition values. Imports are bounded, validated, parameterized and committed in batches of 1,000; repeat imports use stable IDs. This administrative command cannot overwrite private foods. Source licensing and attribution must permit your intended use.

Before accepting real users: configure SMTP/email delivery and the public API origin, RevenueCat products and webhook, an OpenAI project/model/key, a photo encryption key, food-data coverage and licensing, a domain, Railway services and backups. These are integration boundaries, not mocked production services. The test suite uses a fake billing provider and does not spend money on live AI calls.

This backend does not include a Flutter client, store submissions, a staff administration UI, push-notification delivery, health-platform sync, or an AI accuracy evaluation dataset. No live deployment is implied by the presence of deployment files.
