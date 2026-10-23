# Stone's Hotspot — User manual

Stone's Hotspot turns a Windows PC into a paid Wi-Fi hotspot: customers join the Wi-Fi, a page (the "portal") asks
for a code, an account or a Mobile Money payment, and they then get internet for the time or data they bought. You
manage everything from the **dashboard**, on the PC or on your phone.

This manual is for the manager and the cashiers. For the technical installation and network problems see also
`README.md`, or contact Stone's IT Consulting. (French version: `manual-fr.md`.)

---

## 1. What you need

- A Windows 10 or 11 PC (64-bit), switched on during opening hours.
- Two connections: the one that brings **internet** (box, 4G modem, cable) and the one **customers** join (a Wi-Fi
  access point plugged into a second network port, or Windows' "Mobile hotspot").
- The access point must be in **access point / bridge** mode (not router mode, DHCP off).
- For tickets and receipts: an A4 printer (or PDF files).

## 2. Installation and first start

1. Run `StonesHotspot-Setup.exe` and accept the administrator prompt. Nothing else to install.
2. At the end the dashboard opens the **First-time setup** (6 steps):
   1. **Language** of the dashboard and portal (French or English).
   2. **Business name** (shown on the portal and receipts) and **Wi-Fi name**.
   3. **Administrator password** of the `admin` account (at least 8 characters). Write it down and keep it safe.
   4. **Internet and customer Wi-Fi**: choose the internet connection and the customers' one, then **Share
      the internet**. Sharing and the firewall are set up automatically. ("Later" if the hardware is not ready:
      **Network** menu.)
   5. **First plans**: adjust the prices of the three starting plans.
   6. **Finish**, then sign in with `admin` and your password.
3. The setup only opens on the PC itself. On a phone it says to finish on the PC.

The Stone's Hotspot service starts with Windows and restarts by itself after a problem. The **Stone's Hotspot**
icon near the clock shows its state; double-click it to open the dashboard.

## 3. Opening the dashboard

- On the PC: the **Stone's Hotspot** desktop icon, or <http://localhost:8080/admin>.
- On a phone connected to the hotspot Wi-Fi: <http://192.168.137.1:8080/admin>
  (or secure <https://192.168.137.1/admin>; the browser warns once because the certificate is the PC's own).
- From the internet the dashboard is **not** reachable (on purpose).
- After 2 hours without activity you must sign in again. After 5 wrong passwords, wait 5 minutes.

Two roles:

| Role | Can do |
|---|---|
| **Cashier** | Home, Till, Sell, My shift, Accounts (search, top up) |
| **Administrator** | Everything: plans, vouchers, products, reports, staff, settings, network, licence, backup… |

## 4. The cashier's day

(**My shift**, **Till** and products are part of the Pro edition; with Basic, sell with **Sell**.)

### Open the shift (**My shift**)
Count the money in the drawer, enter the **starting cash**, then **Open the shift**. Every sale of the day is added to your
shift.

### Sell a Wi-Fi code (**Sell**)
Choose the plan, the number of codes and the payment method (cash, MTN MoMo, Airtel Money). **Sell** → the ticket
shows the code and a QR code: print it (A4, 3 per row, to cut out) or read the code to the customer. For a
pre-printed card (voucher batch), type its code under **Sell a pre-printed card**.

### Take payment at the till (**Till**)
Add products (drinks, printing…) and Wi-Fi codes to the same cart, enter the amount received: the change is shown.
**Take payment** → A4 receipt (print or PDF).

### Top up a customer account (**Accounts**)
Open the customer's account (search by name or phone), **Top up the account**, enter the amount, **Take the top-up**.

### Close the shift (**My shift**)
Count the money, enter the **cash counted**, then **Close the shift**. The difference from the expected amount is shown and recorded.
Print the summary if needed.

## 5. What the customer sees

1. They join the Wi-Fi (name set in the settings). Their phone shows "Sign in to network"; otherwise they open any
   web site and the hotspot page appears.
2. On the portal, depending on what you enabled:
   - **Code**: they type the ticket code (upper/lower case and dashes do not matter).
   - **Account**: username and password (regular customers with a balance).
   - **Mobile Money**: they choose a plan, type their number, approve the payment on their phone; the code arrives
     by itself.
   - **Free access**, **password of the day**, **code by SMS** (when enabled).
3. A **Status** page shows the time or data left, with a **Log out** button (the remaining time stays on the code
   for later, depending on the plan).
4. When the time or data is used up, internet stops and the portal comes back.

A code works on one device at a time (or the number of devices of the plan). After 5 wrong codes the device waits
5 minutes; too many tries from one address are slowed down as well.

## 6. Plans and vouchers (administrator)

- **Plans**: name, price (FCFA), type (**time**, **data** or **unlimited**), countdown (only while used, or real
  time from the first login), validity, maximum speed, number of devices, logout after inactivity. A plan already
  sold cannot be deleted: disable it.
- **Vouchers**: create a batch of codes in advance (e.g. 100 "1 hour" codes), print the A4 cards with QR codes or
  export to CSV. A batch or a code can be disabled.

## 7. Live (administrator)

The list of connected customers: device, plan, time/data left, speed. For each one: **add time**, **log out**,
**block** the device (never any access) or **whitelist** it (always internet without a code: the till PC, a
printer…).

## 8. Products, reports, audit log

- **Products**: groups, prices including VAT, stock (deliveries, stock counts). Stock goes down with each sale.
- **Reports**: turnover per day, sales per plan, transactions, shifts, sessions, Mobile Money. Every report exports
  to **CSV** (Excel) or **PDF**.
- **Audit log**: who did what and when (sales, price changes, staff sign-ins…).

## 9. Free access, SMS and Mobile Money (administrator)

- **Free access**: access given after accepting the terms (e.g. 15 minutes once a day), or a **password of the day**
  to give customers (fixed, or new every day, shown on the home page).
- **Code by SMS**: the customer receives a code by SMS (USB modem or SMS provider). Limits per number, per device
  and per day.
- **Mobile Money**: enter the MTN MoMo and Airtel Money keys (given by the operators), **Test the connection**,
  choose the plans sold online. Payments appear under **Mobile Money** and in the reports. An approved payment
  creates the code by itself, even if the customer closed the page.

## 10. Bandwidth and web filter (administrator)

- **Bandwidth**: fair sharing of the connection between active devices, minimum and maximum speed, speed test of
  the internet connection.
- **Web filter**: block sites (by name) or categories; family filter (DNS). A blocked site shows a "site blocked"
  page.

## 11. Staff (administrator)

Create one account per cashier (never a shared account). The password you give is temporary: it must be changed at
first sign-in. A disabled account is signed out at once. Forgotten password: an administrator resets it in
**Staff**.

**No administrator can sign in any more?** On the PC, open a command prompt **as administrator** and type:
`"C:\Program Files\Stone's Hotspot\StonesHotspot.Service.exe" --reset-admin-password`
The new temporary password of the `admin` account is in `C:\ProgramData\StonesHotspot\initial-admin-password.txt`.

## 12. Settings (administrator)

Business name, Wi-Fi name, portal logo and colour, welcome page after login, terms of use (FR/EN), default
language, logout after inactivity, receipt header and footer, and **Keep connection logs**: after this number of
days (365 by default, 30 to 3650), old connections lose what identifies the customer (MAC address, IP address,
phone number masked). Sales and reports stay. **Check the duration the law requires** for your business.

## 13. Backups

- An **automatic backup** is made every day (the 14 latest are kept) in `C:\ProgramData\StonesHotspot\backups`.
- **Backup** → **Create a backup and download it**: regularly copy a backup to a USB stick or another computer (if the PC's disk fails, the
  backups on the PC are lost too).
- **Restore**: replaces all data with the chosen file; the current database is kept as a copy just before.

## 14. Licence

- The first 30 days are a **trial** with every feature.
- To continue: **Licence** → **Send by WhatsApp** the **Machine ID** to Stone's IT Consulting, then paste the code
  you receive (`SHS1-…`) and **Activate**. The code only works on this PC.
- **Basic** edition: codes, accounts, plans, web filter, bandwidth, basic reports. **Pro** edition: also the till,
  products, staff shifts, Mobile Money, SMS, free access, extra staff accounts.
- Before the end: reminders 30, 14, 7, 3 and 1 day(s) before (banner and tray icon). After the end: 7 days of grace,
  then the hotspot stops (customers no longer get internet). **Your data is never deleted**; a new code brings
  everything back at once.
- Moving to another PC: ask for a new code for the new Machine ID.

## 15. Updates

When a new version is published, a green banner tells administrators and the Stone's Hotspot icon offers **Install
the update**. Click it and accept the administrator prompt: the hotspot stops for about a minute and starts again.
Data, settings and licence are kept. The downloaded file is only run if it is exactly the one Stone's IT Consulting
published.

## 16. Good security habits

- One account per person, different passwords, never written on the wall.
- Give the administrator account to the manager only.
- Keep Windows updated and an antivirus running; leave Windows Firewall on.
- Download a backup at least once a week.
- If a staff member's phone is lost: change their password in **Staff**.

## 17. Common problems

| Problem | What to do |
|---|---|
| Customers do not see the hotspot page | Open **Network**: both connections must be right and sharing on. The customer can open `http://192.168.137.1` |
| "Your device could not be identified" | The customer switches their phone's Wi-Fi off and on |
| Phones get a 192.168.0.x or 192.168.1.x address | The access point is in router mode: set it to access point/bridge mode, DHCP off |
| Nobody has internet | Check the PC's internet (box, 4G credit). The red "Service stopped" banner = licence ended |
| "Too many tries" | Wait one minute |
| A blocked site still opens | The customer switches the Wi-Fi off and on |
| Power cut | When power is back, Windows and the hotspot restart by themselves; running sessions continue |

Other cases: `README.md`, *Troubleshooting* section, or contact **Stone's IT Consulting**.
