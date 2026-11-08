# Verification record — 8 November 2026

Completed locally on Windows using .NET SDK 10.0.301 and an isolated PostgreSQL 17.6 cluster bound to localhost.

| Check | Result |
|---|---|
| Release test build | Passed, no compiler warnings/errors |
| Full suite with real PostgreSQL | 28 passed, 0 failed, 0 skipped |
| Authentication negatives | Missing/expired/wrong audience/wrong signature rejected |
| Ownership and versioning | Cross-account access rejected; stale writes rejected |
| Concurrent quotas | Atomic maximum maintained under contention |
| Photo idempotency | Concurrent duplicate submissions created one job and one quota charge |
| Worker leases | Single job owner; old fencing token cannot overwrite reclaimed job |
| Billing security | HMAC tampering/replay rejected; event ID deduplicated |
| Data integrity | Food snapshot calculations, premium plan enforcement and cascading deletion verified |
| Provider transport | Mocked OpenAI structured requests/retry classification and RevenueCat grace-period parsing verified |
| Catalog import | Repeat imports remain unique; private food IDs cannot be overwritten |
| NuGet vulnerability audit | No known vulnerable packages reported for backend and transitive dependencies |
| EF migration/model comparison | No pending model changes |
| Release publish | Passed |
| Published migration CLI | Applied both migrations to a fresh disposable database |
| Published API readiness | `/health/ready` returned `ready` |
| Published private endpoint | Anonymous `/api/v1/profile` returned 401 |
| Generated API contract | 25 paths; bearer and RevenueCat HMAC security schemes; saved as `docs/openapi.json` |

Not verified here: Docker image execution (Docker is unavailable), Railway deployment, real identity-provider login, live OpenAI recognition accuracy/cost, live food-catalog requests, actual store purchases/refunds, external dashboards or backup restoration. No real provider credentials were used. Load/soak testing and external security review have not been performed.

Live deployment requires the account-specific configuration in `operations.md`. The production identity provider, AI and billing integrations intentionally fail closed or remain disabled until configured.
