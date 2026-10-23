# Phase 9 — Mobile Money (plan)

Goal: a customer buys a Wi-Fi plan on the portal with **MTN MoMo** or **Airtel Money**, approves on their phone with
their PIN, and is online with a code that is also sent by SMS. Every payment ends in a known state and appears in
the reports and a daily reconciliation.

The owner has no API keys yet: the operators are implemented from their published APIs, tested against a
simulated operator, and switched on later by typing the keys in the dashboard (sandbox first, then production).

## Customer flow (portal, Pro)

1. Portal → **Acheter avec Mobile Money** → choose a plan (only plans the admin marked as sold online).
2. Choose MTN MoMo or Airtel Money, type the number (`06…` MTN, `05…`/`04…` Airtel — checked against each
   operator's number starts).
3. The hotspot asks the operator for a payment ("Request to Pay"). The phone shows the operator's prompt; the
   customer types their PIN. The page waits and checks every 3 seconds.
4. Paid → a code is created on the plan, recorded as a sale (method MTN MoMo / Airtel Money, operator transaction
   id), the device is logged in, the code is shown and sent by SMS (to use later or on another device).
5. Declined / not approved within 3 minutes / operator error → clear message, nothing charged by us, try again.

## Making sure every payment ends correctly

- Each payment has our own reference (a UUID) sent to the operator, so the same payment is never created twice.
- **Polling** (works without a public address): the waiting page and a background job every 5 s ask the
  operator for the state of pending payments, even if the customer closed the page.
- **Callbacks** (optional): with a public `https://` address configured, operators are told to call
  `/api/payments/mtn` or `/api/payments/airtel`. A callback is only a hint: the hotspot asks the operator for the
  real state with its own credentials — a forged callback cannot create a code.
- **Idempotency**: the payment row is "claimed" with a single conditional update (pending → processing) before
  anything is created, so a callback and a poll arriving together, or a callback sent twice, give one code and one
  sale.
- **Late success**: a payment that timed out but that the operator later reports as successful (admin
  **Vérifier**, or a late callback within 24 h) still gets its code, sent by SMS.
- Limits: one pending payment per device, at most 5 payment requests per device per hour (no spamming customers'
  phones with prompts).

## Operators

| | MTN MoMo — Collection API | Airtel Money — Collections API |
|---|---|---|
| Token | `POST /collection/token/` (Basic api user:key + subscription key) | `POST /auth/oauth2/token` (client id/secret) |
| Request | `POST /collection/v1_0/requesttopay`, `X-Reference-Id` = our UUID | `POST /merchant/v1/payments/`, `transaction.id` = our reference |
| State | `GET /collection/v1_0/requesttopay/{id}` → PENDING / SUCCESSFUL / FAILED | `GET /standard/v1/payments/{id}` → TIP / TS / TF / TA |
| Sandbox | `sandbox.momodeveloper.mtn.com`, target `sandbox`, currency **EUR** | `openapiuat.airtel.africa` |
| Production | URL and target (e.g. `mtncongo`) given by MTN, currency XAF | `openapi.airtel.africa`, country CG, XAF |

Settings page **Mobile Money** (admin): per operator on/off, environment, base URL, keys (stored encrypted like
the SMS provider, never shown again), **Tester la connexion** (gets a token), plans sold online, optional public
callback address, list of recent payments with **Vérifier**.

## Reports

- Reports show payment method MTN MoMo / Airtel Money (already a payment method in sales, turnover, staff reports
  — online sales appear as "En ligne").
- New **Mobile Money** report (Pro): per day and operator — successful (count, amount), failed, timed out — and
  every payment with our reference, the operator's transaction id, number, plan, code and state: what is needed to
  check against the operator's statement.

## Tests

Simulated operator: success, decline, timeout then late success, operator error at request; duplicate callback and
concurrent poll+callback give one code; device limits; MTN and Airtel clients against a fake HTTP server (headers,
bodies, token reuse, status mapping, sandbox currency); callback endpoints re-query; portal end-to-end; report.
