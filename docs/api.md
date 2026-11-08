# API contract

Base path: `/api/v1`. JSON uses camelCase. Send `Authorization: Bearer <access-token>` on all endpoints below. Explicitly provision an account after identity-provider login. The current OIDC issuer is the account namespace; switching issuers requires an identity migration. Configure the issuer to issue access tokens for the Kaloree audience, not ID tokens.

## Endpoints

| Method | Path | Behaviour |
|---|---|---|
| POST | `/account` | Idempotently provisions the current subject; returns internal ID / RevenueCat App User ID |
| DELETE | `/account` | Requires `X-Confirm-Delete: delete-my-account`; deletes application data |
| GET / PUT | `/profile` | Read/update profile; PUT requires current version |
| GET | `/calculations` | BMR and TDEE from completed adult profile |
| PUT | `/goals` | Calorie and macro targets; requires profile version |
| GET | `/subscription` | Verified local entitlement status |
| GET | `/foods?q=rice&page=1` | Search global and own stored foods; 30/page |
| GET | `/foods?barcode=...` | Exact stored barcode lookup |
| PUT / DELETE | `/foods/{uuid}` | Create immutable custom food / delete own food |
| GET | `/catalog/barcodes/{barcode}` | External public-reference lookup, with attribution |
| GET | `/meals?from=YYYY-MM-DD&to=YYYY-MM-DD&page=1` | Up to 31-day range; 50/page |
| PUT | `/meals/{uuid}` | Create/update whole meal snapshot using version |
| DELETE | `/meals/{uuid}?version=1` | Compare-and-delete |
| PUT / DELETE | `/weights/{YYYY-MM-DD}` | Upsert/delete weight for one date |
| GET | `/weights?from=...&to=...` | Up to 366-day range |
| PUT / DELETE | `/water/{uuid}` | Create immutable water event / delete event |
| GET | `/water?date=YYYY-MM-DD` | Water events on date |
| GET | `/summary?from=...&to=...` | Daily calorie/macronutrient totals, up to 366 days |
| GET | `/recipes?page=1` | Own recipes, 30/page |
| PUT | `/recipes/{uuid}` | Versioned recipe snapshot |
| DELETE | `/recipes/{uuid}?version=1` | Compare-and-delete |
| GET | `/plans?from=...&to=...&page=1` | Premium planned meals, up to 93-day range, 50/page |
| PUT | `/plans/{uuid}` | Premium versioned planned meal; dates within one year |
| DELETE | `/plans/{uuid}?version=1` | Delete own plan, including after premium expiry |
| POST | `/analyses` | Premium, explicit consent, mandatory `Idempotency-Key`; returns 202 |
| GET / DELETE | `/analyses/{uuid}` | Poll/delete own photo-analysis job |
| GET | `/export?page=1` | Each collection capped at 500 records/page; continue until all arrays empty |

`/health/live` and `/health/ready` are anonymous. `/webhooks/revenuecat` uses provider HMAC authentication, not user tokens. OpenAPI is generated at `/openapi/v1.json` only in Development. The worker role exposes only health routes.

## Profile and goals

```json
{
  "birthDate": "1995-03-20", "equationSex": "female", "heightCm": 170,
  "weightKg": 70, "activityFactor": 1.375, "photoConsent": true, "version": 0
}
```

Equation sex selects the equation constant; it is not a gender-identity field. Supported activity factors: 1.2, 1.375, 1.55, 1.725, 1.9. The initial product supports adults 18–100, height 100–250cm and weight 30–400kg. These application bounds are not clinical suitability determinations.

```json
{ "calories": 2100, "proteinPercent": 25, "fatPercent": 30, "version": 1 }
```

The remainder is carbohydrate. Calorie targets are manually selected (1200–6000). The backend does not automatically prescribe a calorie deficit, infer pregnancy, or treat medical conditions.

## Meal write

```json
{
  "date": "2026-11-08", "slot": "lunch", "name": "Chicken and rice",
  "source": "manual", "version": 0,
  "items": [
    { "foodId": null, "name": "Cooked rice", "grams": 150,
      "calories": 195, "protein": 4, "carbs": 42, "fat": 0.5 }
  ]
}
```

Slots: breakfast/lunch/dinner/snack. Sources: manual/photo_confirmed/recipe/barcode. Items: 1–50. If `foodId` is supplied, the backend computes nutrient totals from that accessible food and grams, ignoring caller-supplied nutrient totals. If absent, supplied totals represent the entire item portion, not values per 100g.

For `PUT /foods/{uuid}`, send `name`, optional `barcode`, `calories100g`, `protein100g`, `carbs100g`, `fat100g`. Food IDs and water-event IDs are immutable create keys; reuse the same key only for the same request. Use a new ID to replace a custom food, then delete the old one. Recipe requests contain `name`, `servings`, `items`, `version`. Recipe item snapshots cover the whole recipe; divide totals by servings when displaying portions. Plan requests contain `date`, `slot`, `name`, `items`, `version`.

Weight bodies: `{ "date": "2026-11-08", "kg": 70 }`. Water bodies: `{ "date": "2026-11-08", "millilitres": 250 }`.

## Photo analysis

```http
POST /api/v1/analyses
Authorization: Bearer ...
Idempotency-Key: a-client-generated-uuid
Content-Type: application/json
```

```json
{ "mimeType": "image/jpeg", "base64": "BASE64_IMAGE_BYTES", "consent": true }
```

JPEG/PNG/WebP; decoded size at most 5MiB; total HTTP request limit 8MiB. Upload only resized meal images. No arbitrary image URL fetching. Poll the returned `Location`. Status: queued/processing/completed/failed. `resultJson` contains a serialized `PhotoEstimate` with `foods`, `questions`, `warning`; each food has name, estimated grams, portion nutrient totals and confidence. Parse this JSON once in Flutter. Successful polling returns no image bytes or ciphertext.

Repeated keys with the same image return the same job. Reusing a key with different bytes/MIME returns 409. Keys remain valid for the 30-day job retention window. Exceeding monthly/global allowances returns 429. A revoked consent blocks new jobs and clears queued images. Already dispatched upstream requests cannot be recalled.

## Errors and sync

Errors use RFC Problem Details and a `traceId`, without stack traces. Common status codes: 400 invalid input; 401 invalid/missing token; 403 premium or consent required; 404 missing/not-owned; 409 stale version/duplicate/key conflict; 429 rate/quota limit; 503 provider disabled/unavailable.

Meal/profile/recipe/plan writes start at version 0; each successful write increments it. A network retry with an old version may return 409: fetch the current record from its collection and reconcile. Version checks prevent blind last-write-wins updates. Food and water creates reuse UUIDs safely. Pagination is offset-based in the initial implementation; concurrent edits can shift pages. For a consistent export, pause client mutations while exporting. Introduce cursor pagination/snapshot exports if high mutation rates require them.

The client must handle unprovisioned 404 by calling POST `/account`, but must never automatically reprovision immediately after an explicit account deletion. Account deletion does not delete the external identity-provider account or cancel an app-store subscription; direct users to the provider's account/subscription controls.
