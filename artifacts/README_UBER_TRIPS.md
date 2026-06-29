# Uber Trips Organization — Feb 23, 2026 → present

Organized spreadsheet + proof artifacts for reimbursement, insurance claim, or records.

## Deliverable
**`Uber_Trips_Organized_Feb23_Jun2026.xlsx`** (also in /artifacts of repo)

Contains 4 sheets:
1. **Trip Log** — Every trip with:
   - Date
   - Distance (km)
   - Cost (CAD $)
   - Pickup Address
   - Drop-off / Destination
   - Receipt (YES ✓ with proof filename when available, or NO)
   - Status

2. **Monthly Summary** — Breakdown by month + grand totals.

3. **Receipt Inventory** — List of actual proof files present (PDF invoices, HTML downloads from riders.uber.com, Outlook .eml) with date + amount.

4. **Available Proof Files** — Index of every Uber-related file uploaded with this package (for audit trail).

## Key Stats (as of latest data)
- Period: February 23, 2026 – June 29, 2026
- Trips: 152
- Total distance: 559.45 km
- Total cost: $1,565.21 CAD
- Trips with proofs attached in package: 39

## New / Extended Trip
- **June 7, 2026** (Sunday evening) — 2.13 km, $9.94
  - Pickup: 742 Adelaide Ave E, Oshawa, ON L1G 2A9, CA (~11:32 p.m.)
  - Drop-off: 222 Pearson St, Oshawa, ON L1G, CA (~11:36 p.m.)
  - Proof: `001_fe56.eml` (Outlook email from Uber Receipts, received Jun 8)

## Source Files Used
- `Uber_Trips_FINAL_Feb22_May2026_*.xlsx` (base 151 trips, insurance claim format)
- `Uber_Data_Request_*.zip` + `trips_data-0_*.csv` (full rider export history)
- PDF receipts: `02_04_UBER_FEB_19...`, `79.10_blue_jays_way*.pdf`, `uber_apr_2o*.pdf`, uuid-named official invoices
- HTML raw receipts: `0ad7daee...`, `0c494b48...`, etc.
- `001_fe56.eml` (Outlook .eml for June trip)
- `UBER_JAN_28...txt` (pre-period, excluded)
- Image receipt (Feb 23 example)

Pre-Feb 23 items (Jan, Feb 19) were excluded per request start date.

## How Proofs Work
For any row with "YES ✓ (filename)", open the matching file in this folder:
- .eml → open in Outlook/Mail or text editor (full email receipt)
- .pdf → Adobe or browser (official Uber PDF)
- .html → browser (raw downloaded receipt page)

These serve as "proof of receipt of any form".

## Regenerating / Extending
If you receive more Uber emails or download more receipts (Gmail zips, Outlook exports, riders.uber.com PDFs), drop the new files into the folder and re-run the organizer script against the base + new files. The June trip was parsed from the .eml automatically.

## Notes
- "every chair" interpreted as each fare/trip logged with km, cost, date, destination.
- All values in CAD. Distances converted to km where source was miles.
- Addresses use the drop-off as "destination".
- This augments the original FINAL log (which stopped at May 18) with the June data you provided via email.

Generated: 2026-06-29 by agent.
