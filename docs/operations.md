# Deployment, security and operations

## Railway services

1. Push this repository to your own Git hosting account and connect it to Railway. Create a PostgreSQL service, an API service and a worker service in the same region/private network. Both app services build the root Dockerfile. PostgreSQL is not exposed publicly.
2. Set the variables below as Railway secrets/variables. `ConnectionStrings__Database` must use Npgsql connection-string syntax; Railway's `postgresql://...` URL is not passed directly to Npgsql. Build it from the database service's host, port, database, username and password references. Escape special characters in values using an Npgsql connection-string builder when provisioning.
3. Use a migration database credential for the pre-deploy command and a runtime credential for serving traffic. If initially sharing one credential, record the exception and split it before broad release. The current image reads one connection variable per process; use the migration command in a separate Railway job if using distinct credentials.
4. `railway.toml` runs `dotnet Kaloree.Api.dll --migrate` before deployment. The migrator acquires a session advisory lock, applies committed EF migrations, releases the lock and exits. It does not run the HTTP host. Avoid incompatible/destructive migrations while old replicas still serve; use expand/backfill/contract changes.
5. API role: `Service__Role=api`; generate its Railway public domain and configure the custom API domain. Worker role: `Service__Role=worker`; do not generate a public domain. Give both services identical data keys and provider configuration. Railway injects `PORT`; the app binds `0.0.0.0:PORT`.
6. Health check `/health/ready`; a healthy process verifies the Accounts table can be queried. It intentionally does not call paid external APIs. `/health/live` is process liveness.
7. Set `AllowedHosts` to the actual API hostnames; set exact CORS origins. Terminate TLS at the edge. Add edge WAF and anonymous rate/body limits before public access. Configure only known proxy IPs; the app does not trust arbitrary `X-Forwarded-*` headers. Until trusted proxy addresses are configured, authenticated distributed limits remain correct but anonymous IP-based limits may group clients behind the proxy.

## Required and optional variables

| Variable | Purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT=Production` | Disables local tokens and public OpenAPI |
| `ConnectionStrings__Database` | `Host=...;Port=5432;Database=...;Username=...;Password=...;Maximum Pool Size=30;Timeout=10` |
| `Auth__Authority` | HTTPS issuer discovery base URL, matching issued tokens |
| `Auth__Audience` | Kaloree API audience registered with the identity provider |
| `AllowedHosts` | Semicolon-separated real API hostnames |
| `Cors__Origins__0` | Exact Flutter web origin; add numbered entries as needed |
| `Service__Role` | api / worker / all |
| `AI__Enabled` | Explicit feature switch; false by default |
| `AI__ApiKey` | Server-only OpenAI project key |
| `AI__Model` | Account-accessible model supporting image input and strict JSON schema; intentionally no assumed model default |
| `AI__EncryptionKey` | Base64-encoded cryptographically random 32-byte AES key, identical on API/worker |
| `AI__MonthlyScanLimit` | Accepted unique jobs per UTC calendar month, default 100 |
| `AI__DailyGlobalLimit` | Accepted unique jobs per UTC day across all accounts, default 1000 |
| `Billing__Enabled` | Explicit billing switch, false by default |
| `Billing__ApiKey` | Server-only RevenueCat secret key authorized to read customers |
| `Billing__WebhookSecret` | RevenueCat HMAC signing secret; enable HMAC in its dashboard |
| `Billing__Environment=PRODUCTION` | Separate production and SANDBOX deployments |
| `Billing__Entitlement=premium` | Entitlement identifier configured in RevenueCat |
| `FoodCatalog__Enabled` | Enables external barcode reference lookup |
| `FoodCatalog__UserAgent` | `Kaloree/1.0 (real-support-email)` per provider requirements |
| `Proxy__KnownProxies__0` | Explicit trusted reverse-proxy address; repeat for each known proxy |

For remote public database connections require verified TLS (`SSL Mode=VerifyFull` and an appropriate trusted certificate). Prefer private networking for Railway services; assess and document TLS support for that database deployment. Never disable certificate verification to make a connection work.

The app validates required settings when features are enabled. Store secrets in Railway variables/secrets or a secret manager, never in Git or the Flutter app. Rotation: revoke old provider keys, update services and redeploy. For image encryption rotation, stop submissions, drain/delete outstanding encrypted jobs, then rotate both API/worker keys. A future keyring with key IDs is required for online rotation without draining. Losing the key makes queued photos undecryptable; jobs fail safely.

## Identity and billing setup

- Create a production identity tenant and API audience. Configure native Flutter redirect URIs and web origins. Require email verification and appropriate abuse/MFA/session policies in that provider. Use short-lived access tokens and PKCE; implement refresh/recovery through its supported SDK.
- Production startup ignores `Auth__DevelopmentTokens`; missing production authority/audience fails startup. The local signing script is never a production login mechanism. Identity deletion and token/session revocation must also be performed in the identity provider after application-data deletion.
- After POST `/api/v1/account`, use returned `billingAppUserId` as RevenueCat's identified App User ID. Configure monthly/yearly store products mapped to the `premium` entitlement; use a separate sandbox environment. Avoid anonymous purchase identities if webhooks cannot map them back to the backend account.
- Webhook URL: `https://YOUR_API/webhooks/revenuecat`. Enable HMAC signing and populate its secret. Set a project/environment filter. Test purchase, renewal, cancellation, refund, billing grace, expiry, restore and transfer in sandbox before charging real users.
- Configure RevenueCat purchase/restore behaviour to avoid unexpected account sharing. The API never grants access from a client-supplied entitlement flag. The initial webhook reconciliation supports monthly/yearly subscriptions; lifetime products are not enabled.
- Check failed webhook deliveries daily and retry them from the provider dashboard. Before launch, establish periodic authoritative reconciliation or an alert-and-retry operational owner; finite provider retries alone are not a permanent reconciliation guarantee. In a high-throughput version, move reconciliation to a durable inbox worker.

## Security controls and privacy

- Issuer, audience, signature, algorithm allowlist and expiry validation; development tokens restricted by environment.
- Owner checks on every private endpoint, bounded input sizes, decimal nutrient bounds, positive-quantity database constraints, parameterized SQL, concurrency checks and transactional quotas.
- No remote image URL ingestion, payment-card storage, password storage, request-body logging or generated stack traces in API errors. Logs use route templates rather than actual URLs and omit query strings, bearer tokens, images, food descriptions and profile values. Framework exception detail logging is suppressed; sanitized error type and trace ID are emitted instead.
- CORS is an explicit allowlist without cookies/credential sharing. Native bearer-token APIs do not use cookie authentication. If introducing a browser BFF/cookie flow, add CSRF protection and secure HttpOnly/SameSite cookies as a separate change.
- API and worker containers run as a non-root user. Runtime database credentials should have CRUD only on application tables; migrations require a separate privileged execution role. Do not give the runtime role superuser, role creation or database creation permission. `pg_trgm` installation requires an allowed migration role.
- Temporary photos are AES-GCM encrypted in PostgreSQL with an owner-bound authentication tag. Database encryption/backups and secret separation are still required. Results are retained for 30 days. Terminal images are cleared immediately; abandoned images after 24 hours are cleared by cleanup, which runs every five minutes. Retention requires at least one worker running even when AI is disabled.
- Account deletion removes private database records and account-specific counters, including jobs and entitlements. Usage aggregates and minimal event IDs contain no raw webhook payload. Provider copies, external identity records, and backup retention have independent deletion policies. Explain these policies in the product's privacy notice; application deletion does not cancel a paid subscription.
- Revoking photo consent clears queued photos. An already transmitted provider request cannot be recalled. Do not promise zero upstream retention solely because `store=false` is sent; the provider account's data controls also apply.

## Monitoring and incident response

The app writes structured JSON logs with route, method, response status, duration and trace ID. `Kaloree.Backend` exposes .NET Meter instruments `kaloree.http.duration`, `kaloree.photo.attempts`, `kaloree.photo.failures`; attach a .NET diagnostics/OpenTelemetry collector to export them. No telemetry exporter credentials or external dashboard are assumed. Runtime ASP.NET/.NET metrics are also available through standard diagnostics.

Alert on: readiness failure; rising 5xx/429; p95 API latency; PostgreSQL connections/CPU/disk; oldest queued job age; processing leases expiring repeatedly; provider 429/error rates; webhook delivery failures; and backup failure. Never put account IDs or image hashes into metric labels.

Operational queue queries (run over the private database connection):

```sql
SELECT "Status", count(*), min("CreatedAt") FROM "AnalysisJobs" GROUP BY "Status";
SELECT count(*) FROM "AnalysisJobs" WHERE "Status"='processing' AND "LeaseUntil" < now();
SELECT pg_size_pretty(pg_database_size(current_database()));
```

During an AI incident, set `AI__Enabled=false` on API and worker to pause new submissions and processing; workers continue cleanup. Existing provider requests finish or time out. Restore after resolving keys/model/quota problems. Do not automatically replay failed jobs with new keys, which would charge quota again without user intent.

## Backup, rollback and capacity

Configure daily encrypted backups and, where supported, point-in-time recovery. Suggested initial targets: RPO ≤24h and RTO ≤4h. Prove these with a restore to an isolated service; merely enabling backups does not prove recovery. Record retention, storage location and access controls. A managed HA database/PITR plan is required for tighter targets.

Use backward-compatible deployments and retain the previous image for rollback. Rollback application code independently of schema when possible; do not automatically run destructive down-migrations. Restore only after assessing data-loss implications and stopping affected writes.

Size each replica's connection pool against total database connections; use `Maximum Pool Size=30` as a starting cap and account for workers, migrations and ops connections. Measure before tuning. Use an upstream hard spending policy plus job quotas; retries can produce multiple model calls per accepted job. No throughput, availability, AI accuracy or currency-cost guarantee has been established by unit tests.

## Release acceptance

Run the Release build and all PostgreSQL tests, dependency audit, image build/scan and staging smoke tests. Then verify actual OIDC tenant access, signed sandbox billing lifecycle, live AI examples with representative cuisines, food-data source licensing, private networking/TLS, CORS/host restrictions, backup restoration and log redaction. These are deployment-specific checks; repository tests use no live provider credentials.
