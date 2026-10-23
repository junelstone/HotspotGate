# Manual test — Phase 9: Mobile Money (MTN MoMo, Airtel Money)

Run the **published** service on Windows with the hotspot working. Start with the operators' **sandbox** keys;
repeat the important steps with production keys and a real 100 FCFA plan before opening to customers.

## Getting sandbox keys

| Operator | Steps |
|---|---|
| MTN MoMo | Sign up at <https://momodeveloper.mtn.com>, subscribe to **Collections**, copy the **primary key** (= subscription key). Create an **API user** and **API key** for the sandbox (the portal's "Sandbox User Provisioning", or `POST /v1_0/apiuser` then `POST /v1_0/apiuser/{id}/apikey`). Environment `sandbox`, currency **EUR** (the sandbox refuses XAF) |
| Airtel Money | Sign up at <https://developers.airtel.africa>, create an application with the **Collection** product for **Congo (CG)**, copy the **client id / client secret** of the UAT (staging) environment |

## Settings

| # | Step | Expected |
|---|------|----------|
| 1 | **Mobile Money** → MTN on, keys typed, **Enregistrer** | "Enregistré". The key fields are empty again with "Enregistrée (tapez pour la remplacer)" |
| 2 | **Tester la connexion** (MTN, then Airtel) | "connexion réussie, les clés sont acceptées". Wrong key → "connexion impossible" with the operator's answer (HTTP 401…) |
| 3 | Tick only the plans to sell online (e.g. "1 heure", "Pass 24 heures"), save | — |

## Buying on the portal (sandbox)

| # | Step | Expected |
|---|------|----------|
| 4 | Phone on the hotspot Wi-Fi → portal → **Acheter un forfait avec Mobile Money** | Only the chosen plans; only the operators that are set up |
| 5 | MTN, number `05 …` | "Ce numéro n'est pas un numéro MTN MoMo." |
| 6 | MTN, number `06 …` → **Payer** | "Validez le paiement sur votre téléphone…"; the page refreshes by itself. In the sandbox there is no real prompt: MTN approves automatically after a few seconds |
| 7 | Wait | "Paiement reçu, vous êtes connecté !", the code shown, internet works; SMS with the code if SMS sending is set up (Phase 8) |
| 8 | **Rapports** → **Mobile Money** | The payment, "Payé", MTN's transaction number, receipt, code. **Opérations** shows it as MTN MoMo, without a cashier |
| 9 | Close the waiting page right after **Payer**; wait 30 s | The payment still completes (background check); code sent by SMS |
| 10 | Airtel: repeat 6–8 with an Airtel number. **Check in the UAT logs/portal how Airtel expects the Congo number** (we send the 9 digits `05…`); if Airtel refuses the number, report it | Same as MTN |

## With production keys (real money: use a 100 FCFA plan)

| # | Step | Expected |
|---|------|----------|
| 11 | MTN: production URL and environment given by MTN Congo, currency XAF, production keys. Pay from a real MoMo number | The phone shows MTN's prompt; type the PIN → connected within ~10 s |
| 12 | Refuse the prompt (cancel) | "Le paiement a été refusé…", no code, nothing charged |
| 13 | Ignore the prompt for 3 minutes | "…n'a pas été validé à temps". If you approve late: in **Mobile Money** press **Vérifier** → "Payé", code sent by SMS |
| 14 | Compare **Rapports → Mobile Money** of the day with the operator's merchant statement | Same transactions and total |
| 15 | Optional: a public https address forwarded to this PC (port 8080), set as "Adresse publique", registered as callback | Payments complete within 1–2 s instead of up to 5 s |

If a payment behaves unexpectedly, send: the reference shown on the waiting page, the time, the operator, and the
latest `service-*.log` (it never contains keys; numbers are masked).
