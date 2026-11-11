# Chambre Froide — cold room management

A web app for a frozen food business that sells both wholesale (whole cartons) and retail (repacked bags), built around a walk-in freezer.

Open `index.html` in any browser. It is one self-contained file with no build step and no server. Records are saved in that browser (localStorage), so use **Setup → Export backup** regularly. To try every screen, click **Load example data** (it is marked as example and can be deleted in Setup).

## What it does

| Screen | Purpose |
|---|---|
| Overview | Sales today (wholesale vs retail), stock value, alarms that need attention, stock per product, oldest cartons to sell first, 24 h temperature charts |
| Stock | Every carton and pack by batch (lot, zone, rack, received date, best-before). Oldest batch first (FIFO). Move stock to display freezers, write off thawed/expired/damaged stock |
| Receive | Log a refrigerated truck delivery: lot, best-before, supplier, cost, rack, and the product core temperature on arrival (warns above −18 °C, refuses above −12 °C) |
| Repack | Split bulk cartons into retail packs. Takes the oldest cartons, runs a timer for time out of the cold (target 15 min, limit 30 min), keeps the lot and best-before on the packs, shows a label preview |
| Sell | Wholesale (cartons from the walk-in) or retail (packs from display freezers). Stock is taken FIFO and each line records which lots were sold. Prints an invoice/receipt view |
| Temperatures | Log a round of readings for all zones, 72 h chart against each zone's limits, °F shown too, daily safety checklist (door seals, evaporator ice, generator fuel, staff thermal gear…) |
| Power & defrost | Start/stop power cuts, generator runs, defrost cycles, door-open and breakdown events. Live duration, risk warning when an outage without backup gets long, peak temperature during each event |
| Setup | Products and prices (carton weight, wholesale price, retail pack size and price, reorder level), zones and temperature limits, business rules, backup export/import |

## Default rules

- Frozen zones alarm above −18 °C, critical above −15 °C.
- A zone with no reading for 4 hours is flagged.
- Best-before within 30 days is flagged; expired stock is an alarm.
- All thresholds are editable in Setup.
