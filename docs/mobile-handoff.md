# Kaloree mobile handoff

Prepared 8 November 2026. Build Flutter apps for iOS and Android in a **separate mobile repository/workspace**. This repository remains backend-only.

## Source of truth and current status

Read these files before implementing network calls:

- `openapi.json`: generated request schemas and routes.
- `api.md`: behavioural contract, versions, pagination, nutrient semantics and photo workflow.
- `architecture.md`: data flow, ownership, quotas, retention and provider boundaries.
- `operations.md`: environment configuration, identity, billing and deployment.
- `verification.md`: checks actually completed and remaining external checks.

The backend is ASP.NET Core 10 with PostgreSQL and a separate photo worker. Its recorded verification is 28 passing tests with real PostgreSQL, successful Release publish/migrations/local smoke tests, and no known vulnerable backend dependencies at audit time. **No live Railway URL, identity tenant, store products or live AI configuration has been supplied.** Do not treat a local verification as a deployed production integration.

The app calls only Kaloree's API and the selected identity/billing providers. OpenAI and nutrition-provider secret keys stay on the backend. Never call OpenAI directly from Flutter.

## Product and visual direction

Brand: **Kaloree**. Working tagline: “Snap your meal. Track your goals.” A calm, modern nutrition companion with premium glass surfaces, clear hierarchy and fast meal logging.

Suggested design tokens:

| Token | Light theme | Dark theme |
|---|---|---|
| Background | Warm ivory `#F6F7F2` | Deep forest `#101B18` |
| Primary | Forest green `#176B50` | Soft mint `#8DDFB4` |
| Main text | `#14261F` | `#F2F7F3` |
| Secondary text | `#52645B` | `#B2C4BA` |
| Accent | Apricot `#F2AD7C` | Apricot `#F2AD7C` |
| Surfaces | White/ivory, restrained translucency | Elevated forest, restrained translucency |

Validate contrast in the actual compositions; these are initial tokens, not a blanket contrast certification. Nutrient distinctions must use labels/icons as well as colour.

- Use glass primarily for the floating navigation bar, scan controls, selected summary surfaces and sheets. Use readable, more opaque surfaces for forms, charts, dense lists and destructive actions.
- Create reusable `GlassSurface`, `FloatingNavBar`, `CalorieRing`, `MacroSummary`, `MealCard`, `AsyncStateView`, `QuantityField` and `PremiumFeatureGate` components.
- Four persistent destinations: **Today, Diary, Plan, Progress**. A visually prominent central **Scan** action opens capture; it is an action, not a hidden fifth destination. Profile/settings are accessible from the top bar.
- Preserve each tab's scroll/navigation state. Respect safe areas, system gestures and Android back behaviour. Add enough bottom content padding that the floating bar never covers controls or the final list item. Hide/reposition it appropriately for keyboards and full-screen capture.
- Use a consistent spacing scale (4/8/12/16/24/32), 20–28px card radii and subtle borders/shadows. Use restrained 150–250ms transitions and haptics where appropriate; avoid constant moving gradients or nested live blurs.
- Provide light, dark and system themes. Support system text scaling, VoiceOver/TalkBack, reduced motion, increased contrast and reduced transparency. Offer an explicit opaque-surfaces setting if the platform signal is unavailable through the selected Flutter API; use a tested native bridge where needed.
- Aim for comfortable 48 logical-pixel touch targets. Support small phones, large phones, landscape where appropriate and keyboard/screen-reader navigation without clipped text.
- Prefer supportive language. No shame messaging, fabricated streaks, invented progress, or claims that a photograph measures exact calories.

Apple recommends applying glass sparingly and adapting materials to accessibility settings: [materials guidance](https://developer.apple.com/design/human-interface-guidelines/materials).

## Screens and working flows

| Area | Required behaviour |
|---|---|
| Startup | Load environment, restore secure session, route through login/onboarding, show configuration errors clearly |
| Identity | Provider signup/login, verified-email flow if configured, password recovery, logout; PKCE through the system browser |
| Onboarding | Adult date of birth, equation sex explanation, height/weight, metric/imperial presentation, activity factor, calorie/macronutrient targets, optional photo consent |
| Today | Selected local date, consumed/target calories, macro totals, meal sections, water, quick add, honest empty states |
| Diary | Date navigation, breakfast/lunch/dinner/snacks, search, custom foods, barcode scan, portion editing, manual meal entry, edit/delete |
| Scan | Just-in-time permission, camera/gallery, preview/retake, consent, compression, submit, resumable polling, review/edit, explicit save |
| Recipes | Create/edit/delete, servings, ingredient snapshots, per-serving preview and manual confirmed logging |
| Plan | Premium planned meals, future dates, edit/delete, confirmation before copying a planned meal to the actual diary |
| Progress | Weight entries, calorie/macro history from available API summaries, accessible charts and date ranges |
| Premium | Provider-loaded offerings/prices, monthly/yearly terms, restore purchases, manage subscription, pending reconciliation |
| Settings | Units/theme/accessibility, profile/goals, photo consent, privacy/terms/support, paginated export, application-data deletion |

Use loading, empty, error, retry, offline and permission-denied states on every relevant screen. Preserve unsaved forms and provide confirmation/undo where technically safe. Do not ask for camera/photos/notification permissions at startup.

## Mobile structure

Use Flutter stable with Dart null safety and feature-oriented modules. Verify package versions and native platform requirements at implementation time.

```text
lib/
  app/                 bootstrap, environments, router, app shell
  core/
    network/           API client, auth refresh, Problem Details, pagination
    security/          token vault, local encryption, redaction
    storage/           local schema, migrations, outbox, account isolation
    sync/              mutation replay, conflicts, lifecycle handling
    theme/             design tokens, accessible glass materials
    widgets/           reusable shared components
  features/
    auth/ onboarding/ dashboard/ diary/ foods/ scanning/
    recipes/ planning/ progress/ subscription/ settings/
test/
integration_test/
```

Suggested tools: Riverpod for state/dependency injection; GoRouter for routing; Dio for HTTP; generated immutable DTOs/JSON mapping; Drift or equivalent typed local storage with a verified encrypted SQLite integration; platform secure storage for credentials/DB keys; a maintained OIDC/AppAuth integration; RevenueCat's Flutter SDK; maintained camera/photo-picker/barcode packages. Do not add multiple libraries solving the same problem. Confirm plugin maintenance, permissions, license and iOS/Android compatibility.

Repository interfaces isolate API, storage, identity and billing. Widgets should not issue raw HTTP calls or contain token handling. Avoid an unnecessary general-purpose domain framework.

## Environment handoff

Provide a checked-in example configuration and separate dev/staging/production flavours. Nonsecret client configuration can use `--dart-define` or flavour config:

```text
APP_ENV
API_ORIGIN                  # e.g. https://api.example.com; append /api/v1 in one place
OIDC_ISSUER
OIDC_CLIENT_ID_IOS
OIDC_CLIENT_ID_ANDROID
OIDC_AUDIENCE
OIDC_REDIRECT_URI_IOS
OIDC_REDIRECT_URI_ANDROID
REVENUECAT_PUBLIC_IOS_KEY
REVENUECAT_PUBLIC_ANDROID_KEY
PRIVACY_POLICY_URL
TERMS_URL
SUPPORT_URL
```

These are placeholders until supplied, not working endpoints or credentials. Public SDK keys and client IDs are not server secrets. Never embed an OIDC client secret in a native public client. Release builds must reject HTTP API origins and production demo mode. Development-only cleartext exceptions must not reach release manifests.

Product owner/platform setup also needs: owned iOS bundle ID/Android application ID, Apple team/App Store Connect access, Google Play access, signing infrastructure, identity redirect registrations, RevenueCat products/entitlement mapping, legal/support URLs, and macOS/Xcode for iOS builds. Do not invent owned identifiers or upload without the required account access.

## Identity and secure lifecycle

1. Use Authorization Code + PKCE in the provider-supported system-browser session. Configure and validate redirect/deep-link handling, state and nonce through the supported SDK.
2. Use an **access token** for the configured Kaloree audience, never an ID token. Keep access tokens in memory where possible; store refresh credentials only in Keychain/Keystore-backed storage.
3. After login, explicitly `POST /api/v1/account`; retain its returned internal account ID. Fetch profile and complete onboarding if required.
4. Serialize concurrent refresh attempts; retry eligible requests once after successful refresh. Prevent refresh loops. If refresh fails, preserve permitted local drafts, stop sync and request login.
5. Scope local data, queued mutations and any billing identity to the internal account. Cancel old-account requests and ignore late responses after account switching. Clear/destroy the prior local database key and credentials on logout/deletion as defined by the privacy policy. Never replay one person's outbox under another token.
6. After explicit account deletion, do not automatically call account provisioning. Application-data deletion does not delete the identity-provider account or cancel store billing; explain and provide the appropriate separate actions.

Do not log tokens, authorization headers, photos, health/profile values or food descriptions. Redact crash reporting and analytics. Mask sensitive app-switcher previews when backgrounded where practical. Any biometric local lock is convenience access control, not a substitute for server authentication. Obfuscation is defence in depth, not a secret store. Avoid brittle certificate pinning without an operational rotation/recovery design.

## Exact backend rules

- Origin plus `/api/v1`, camelCase JSON, bearer access token. Read `api.md` for all routes. No `/auth/login`, `/auth/refresh`, chat-coach, notification-token or health-sync endpoints exist here.
- Profile has no persisted display-name/avatar/unit/theme fields. Get optional display identity from the identity provider; keep units/theme device-local unless a backend extension is agreed.
- Store/transmit metric values: kilograms and centimetres. Convert imperial values at the presentation boundary. Keep decimal precision; never convert numeric strings inconsistently by locale.
- Use `YYYY-MM-DD` **local diary calendar dates**. Do not obtain them by converting local midnight to UTC and truncating. Operational timestamps and scan quotas are UTC.
- Profile, meal, recipe and plan writes use versions. New records start at version 0; successful writes increment it. Goal updates share the profile version. Refresh profile after goal changes.
- Food and water UUIDs are immutable create keys. A changed payload with an existing UUID returns 409; create a new ID when replacing a food. Local edit UX must reflect these semantics.
- Food nutrients are per 100g; manual meal-item nutrients are totals for the submitted portion. For a known `foodId`, server-computed nutrients are authoritative. Keep a private/reference food snapshot in a draft so users can still review it offline.
- Barcode catalog responses do **not** return a persistent Kaloree `foodId`. Use returned nutrition to create a custom food with a client UUID, or build an explicit manual nutrient snapshot. Preserve source attribution. Don't use the barcode as an invented food UUID.
- Global text food search depends on a loaded catalog. An empty backend is valid; show an honest empty result plus custom-food/barcode alternatives. Don't silently substitute fake search results.
- There is no single-meal GET route or incremental change-feed. Fetch all necessary collection pages for a known date range and locate the UUID. Never assume page 1 is complete.
- Summary contains meal nutrient totals and current calorie target. Fetch water and weights separately. It does not supply historical goal changes, exercise calories, sleep, body-fat or micronutrients. Don't fabricate those metrics.
- The current backend enforces premium on photo analysis and plan create/read. Goals, recipes, tracking and export are not currently premium-gated. Do not claim security enforcement exists for a client-only product gate.
- `GET /subscription` reports premium and a nominal scan allowance; it does not expose actual remaining scans or billing usage. Its current allowance field is fixed at 100 even if server configuration differs. Don't present invented remaining-count gauges. Handle scan-limit errors precisely.
- Backend errors use Problem Details; `title` contains the stable error code and `traceId` supports troubleshooting. Branch on status/code, not arbitrary provider wording.

## Photo workflow

```text
capture/pick → preview → explicit consent → resize/strip metadata
→ POST analyses with stable Idempotency-Key → persist job ID
→ poll own job → parse resultJson → review/edit → PUT confirmed meal
```

- Supported transport: JPEG, PNG, WebP. Convert unsupported HEIC/native captures before submission. Maximum decoded image 5MiB; JSON request limit 8MiB; base64 increases payload size. Compress to a much smaller practical target without destroying detail. Correct orientation and remove EXIF/location metadata.
- Consent is required both in profile and the request body. Explain external AI processing before sending. Keep consent revocation available.
- Reuse the **same key and encoded image bytes/MIME** on a network retry. Recompression creates different bytes and needs a new intentional submission. Never create a fresh key automatically after an uncertain upload response.
- Persist the job ID and resume polling after app restart. Poll while foregrounded with bounded exponential backoff/jitter, pause in background, honour cancellation, and avoid a second scan. The backend does not push completion notifications.
- Queued/processing/completed/failed are real states. Completed `resultJson` is a JSON **string** containing `foods`, `questions`, `warning`; parse it into a separate typed model.
- Confidence is categorical low/medium/high, not a calibrated percentage. Make foods, grams and nutrients editable, and show the application's estimate warning. Ask the user to review oils/sauces/portion assumptions.
- Follow-up questions are prompts for the user to correct the draft. There is no conversational follow-up endpoint; don't imply the model processes replies automatically.
- Confirmed save is a separate `PUT /meals/{clientUuid}` with source `photo_confirmed`. Never auto-save an AI estimate. Prevent double taps and reconcile an uncertain save by UUID/version.
- Display distinct errors for consent, expired premium, monthly scan limit, global service limit, unsupported/corrupt photo and provider unavailability. Manual entry remains usable.
- Photos have no server read/download endpoint. The API deletes temporary images after processing; don't design a persistent photo gallery around nonexistent storage.

## Purchases and entitlement UX

Use RevenueCat's identified App User ID returned by account provisioning. Load offerings, actual localized prices and trial eligibility from the SDK. Never hard-code prices or universal trial claims. Use platform-supported store billing and current storefront rules; keep purchase verification on the server/provider. [Google billing guidance](https://developer.android.com/google/play/billing).

After purchase/restore, refresh `/subscription` with bounded backoff while the backend webhook reconciles. Present “Purchase received — activating premium” if entitlement has not propagated yet. A successful local purchase callback is not permission to bypass a backend 403. Handle pending/cancelled purchases, renewals, grace periods, expiration, refund and account changes. Include restore and manage-subscription actions, terms, renewal terms and privacy links. Export/deletion must remain accessible after premium expiry.

## Offline and sync

Encrypt sensitive local storage with a random per-account key held in platform secure storage. Verify actual native database encryption; don't use a plaintext fallback in production. Exclude sensitive databases, keys and photo drafts from inappropriate backups. Handle a missing/unavailable key by a safe recovery flow.

Offline support means cached diary/progress reads and queued manual edits; **AI analysis, live catalog, login and purchases require network**. Persist an outbox with operation UUID, owner, resource UUID, exact payload, expected version, dependencies and retry state. Serialize mutations per resource. Coalesce safe local edits, not unrelated writes.

- On 401: single refresh attempt, then pause queue.
- On 403: surface consent/premium state; do not repeatedly replay the blocked job.
- On 404: distinguish missing data from `account_not_provisioned`; never provision for every unrelated 404.
- On 409: refetch relevant paginated collection, compare UUID/version, and present a merge/review action when needed. No blind force overwrite. Meal/profile/recipe/plan version retries may conflict even after an earlier successful uncertain request.
- On 429: back off; distinguish fixed user/month service limits from transient throttling.
- On network/5xx: bounded retry with jitter for replay-safe operations. Preserve user drafts and show pending sync.
- Weight PUTs are day-level last-write-wins, not versioned; disclose/reconcile competing local edits instead of inventing server concurrency support.

There are no deletion tombstones/change-feed endpoints. Refresh authoritative date ranges on foreground/manual refresh; reconcile locally known IDs only after fetching complete ranges. Do not mistake absence on page 1 for deletion. A conflict-free multi-device sync engine cannot be assumed from these endpoints alone.

## Testing and release acceptance

Require `dart format`, `flutter analyze`, unit/widget tests, mobile integration tests and actual device/simulator visual review. Test dark/light, large text, reduced motion/transparency, screen readers, small screens, keyboard-safe forms and floating-nav overlap. Use image captures from rendered screens for QA; source-code review alone is insufficient for UI acceptance.

Integration scenarios: onboarding and unit conversion; manual/custom/barcode logging; pagination; offline write/restart/reconnect; version conflict; token expiry; account switch with pending requests; consent revocation; scan retry without duplicate charge; delayed scan completion; editing before confirmed save; premium propagation/restore/expiry; multi-page export; deletion without accidental reprovisioning.

Create Android release AAB and iOS archive/TestFlight preparation using the available signing infrastructure. Windows can run Android tasks; final iOS builds/signing require macOS/Xcode. Add CI with Android and macOS jobs. Validate platform permissions/deep links, privacy manifests and store disclosures against actual plugin data collection. No HealthKit/Health Connect permission is needed until those integrations exist. Local reminder functionality may be implemented on-device with opt-in settings; remote push and health sync require separately specified integrations.

Deliver source, reproducible setup, config examples, test reports, representative screenshots, release instructions and a truthful list of missing credentials/unverified steps. Store uploads and production claims require actual successful account-backed execution.
