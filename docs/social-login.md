# Social login (Google / Facebook) — not built, decision needed

The specification asks for a note instead of an implementation. This is what "Se connecter avec Google /
Facebook" on the portal would require.

## What it would give

Customers log in with their Google or Facebook account instead of a code or an SMS. The business learns a name and
an e-mail address (with the customer's consent), and nothing is paid for SMS.

## What it needs

| Need | Why | Effort / cost |
|---|---|---|
| A **public domain name with HTTPS** (e.g. `wifi.stones-it.cg`) pointing to a server on the internet | Google and Facebook only send the customer back to a registered `https://` address; `http://192.168.137.1` is refused | Domain ~10–15 €/year; a small server or a tunnel service |
| A **Google Cloud** project and a **Meta for Developers** app, each with an OAuth client | Identifies the hotspot to Google/Facebook | Free; Meta requires business verification and an app review (several days to weeks) |
| A published **privacy policy** and data-deletion page | Required by both for apps going live | One page, in French and English |
| Letting the login sites through the gate **before** login | The phone must reach `accounts.google.com`, `facebook.com`, their CDNs… while not logged in | Long, changing domain lists; customers can browse parts of Google/Facebook for free through them |
| Code: OAuth "authorization code" flow on the public server, which then tells the hotspot "device X logged in as Y" | The login happens on the internet side, the hotspot must trust that answer (signed message) | About one phase of work, plus testing with both providers |

## Risks

- Captive-portal browsers on iPhone and Android often block Google sign-in pages ("disallowed_useragent"):
  customers would have to open a full browser, which many will not manage.
- Facebook regularly changes its review rules; the login can stop working until the app is reviewed again.
- Free browsing of Google/Facebook through the walled garden before login.

## Recommendation

SMS login (Phase 8) already identifies customers by phone number, which is what most people in Congo use, with
no internet account to maintain. Social login is worth it only if the business already has (or plans) a public
website with HTTPS. **Decision for the owner: build it later, or not at all?**
