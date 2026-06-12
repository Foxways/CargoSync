using System.Collections.Generic;

namespace OrganizationImportTool.Help
{
    /// <summary>The complete in-app user guide, A to Z. Plain language first; jargon explained inline.</summary>
    public static class HelpContent
    {
        public static readonly IReadOnlyList<HelpTopic> Topics = new List<HelpTopic>
        {
            new("what-is", "What CargoSync does",
@"CargoSync takes a spreadsheet of companies (organizations) and puts them into CargoWise for you — safely.

You give it ANY Excel or CSV file. CargoSync figures out which column is which, checks the data for problems, shows you everything it found, and only sends to CargoWise after you say yes.

## The golden rule
Nothing is EVER sent to CargoWise without your approval. Every import walks you through review screens first, and the Dry Run button lets you practice the whole thing without sending anything at all.

## What you need
- A CargoWise connection (set up once — see ""Getting started"")
- A spreadsheet with your organizations (any layout, any column names)"),

            new("getting-started", "Getting started (one-time setup)",
@"## 1. Create your sign-in
The first time CargoSync opens it asks you to create an account (username + password). This is just for this app on this computer — it is not your CargoWise login.

## 2. Connect your CargoWise system
Click Add Client (or Get started on first run) and fill in:
- Client Name — any name you like, e.g. ""Acme Logistics (Test)""
- Environment — TST for testing, PRD for the real live system
- eAdaptor URL — the web address of your CargoWise eAdaptor. Ask your CargoWise administrator. (eAdaptor = CargoWise's electronic mailbox for incoming data.)
- Sender ID and Password — the eAdaptor account, also from your administrator
- Enterprise ID — your CargoWise enterprise code, e.g. CGD
- Company Code — optional; used as the owner code on imported organizations
- Log Folder — where import reports are saved

## 3. Test it
Click Test Connection. Green = everything works. Red tells you exactly what to fix. Then Save.

Tip: always start with a TST (test) connection. Only add PRD once your imports look right."),

            new("first-import", "Your first import, step by step",
@"## Before you start
Have your Excel/CSV file ready. Column names don't matter — CargoSync works them out.

## The flow
1. Pick your client in the Client dropdown.
2. Click Browse and choose your file.
3. Click DRY RUN (not Upload!) the first time — it does everything except send.
4. CargoSync now walks you through up to 6 screens (see the Step 1–6 topics):
- Step 1 — Confirm the mapping (always shown)
- Step 2 — Data health check (always shown)
- Step 3 — Duplicates (only if duplicates were found)
- Step 4 — Data cleaning (only if something needs fixing)
- Step 5 — Filling gaps (only if empty fields can be filled)
- Step 6 — Results
5. Happy with the dry-run results? Click Upload and approve the same screens — this time it sends for real.

## Cancelling
Cancel import on any screen stops EVERYTHING. Nothing is sent until the final send actually starts, and the results screen always shows exactly what happened."),

            new("step-mapping", "Step 1 — Confirm the mapping",
@"This screen shows how each column in YOUR file will fill a CargoWise field.

## What to check
- Each row = one of your columns. The dropdown shows the CargoWise field it will go to.
- Match shows how confident CargoSync is: green High, amber Medium, red Low.
- Untick Use to ignore a column completely.
- Amber rows need your Approve tick — CargoSync wasn't sure, so a human must confirm.

## Why this mapping?
Select any row and the panel below explains WHY that field was chosen, and which other fields were considered. Pick a different field from the dropdown if it guessed wrong — CargoSync REMEMBERS your correction for this client and maps it automatically next time.

## Tabs
- Constants & Defaults — set a fixed value for every row (e.g. Consignee = true)
- Rules — no-code IF/THEN, e.g. If Type contains IMP then set Is Consignee = true
- Value Maps — translate your codes to CargoWise values, e.g. ""AU-SYD"" → ""AUSYD""

You can save the whole setup as a Template and load it for similar files.

## Required fields
CargoWise needs at least an Organization Code and a Full Name for every row. The status bar tells you if they're still missing — map a column or set a constant."),

            new("step-profile", "Step 2 — Data health check",
@"A pre-flight overview of your file BEFORE anything happens.

## The risk score
- Low (green) — clean file, good to go
- Medium (amber) — review the listed factors; the import can still proceed
- High (red) — something will block rows, e.g. rows missing the Organization Code

## What the numbers mean
- Blocking rows — rows missing required values. They will NOT be sent (everything else still goes).
- Duplicates — rows that look like the same organization (reviewed in Step 3)
- Cleaning fixes — values CargoSync can tidy up (reviewed in Step 4)
- Already imported — rows whose code was already sent to CargoWise before (re-sending just updates them)

The per-field table shows how filled each mapped field is. Continue when you're happy."),

            new("step-duplicates", "Step 3 — Duplicates",
@"Shown only when two or more rows in your file look like the SAME organization — identical codes, or near-identical names in the same country.

## Your choice
- Skip the duplicates (recommended, ticked by default) — keeps the FIRST row of each group, skips the rest. Skipped rows appear in the results as ""Skipped (duplicate)"".
- Untick it to import every row anyway — CargoWise will merge/overwrite the same org repeatedly.

Different countries are never merged: ""Globe Trading"" in AU and ""Globe Trading"" in US stay separate."),

            new("step-cleaning", "Step 4 — Data cleaning",
@"CargoSync found values it can tidy up before sending. Examples:
- ""Australia"" → AU (country names become ISO codes)
- ""yes"" / ""y"" / ""1"" → true
- ""ausyd"" → AUSYD (port codes upper-cased)
- Double  spaces   collapsed

## Important: nothing is changed unless YOU tick it
By default every fix is UNTICKED — your data goes as-is. Tick the fixes you want (or Tick all). Rows marked AI were resolved by the AI provider (e.g. a misspelled country); they follow the same rule — unticked unless you approve.

Cancel import stops everything, as always."),

            new("step-enrichment", "Step 5 — Filling gaps",
@"Shown when EMPTY fields can be filled from trusted sources. CargoSync never overwrites a value you provided — it only offers to fill blanks.

## Sources
- Postal API — a free public postcode database: country + postcode can fill a missing city/state
- AI — e.g. inferring the country code from a city name (only when AI is on)

Tick what you want filled, then Continue."),

            new("step-results", "Step 6 — Results",
@"The final screen shows one row per organization with exactly what happened.

## Status codes (CargoWise speak, translated)
- PRS — Processed. CargoWise ACCEPTED it. ✓
- WRN — Stored, but CargoWise noted warnings — worth reading.
- ERR — Rejected. The row's detail shows CargoWise's reason.
- DUP — Skipped as a duplicate (you chose this in Step 3).
- SIM — Dry run only: ""would send"" — nothing was transmitted.
- Not sent (validation) — blocked before sending (e.g. missing Organization Code).

## Stored as a different code?
CargoWise can auto-generate organization codes. ""Created as ACMIMPSYD"" means your row was stored under that code. CargoSync remembers both codes in its Import History.

## Reports
Save Report exports the grid as CSV. Every import also writes a detailed log file to the client's log folder."),

            new("dry-run", "Dry Run vs Upload",
@"## Dry Run = practice mode
Runs the ENTIRE flow — reading, mapping, all review screens, validation, even building the exact data that would be sent — but transmits NOTHING. The results screen is titled ""Dry Run — nothing was sent"" and rows show ""Would send ✓"".

A dry run also leaves no traces: it doesn't update the import history or the learned mapping memory.

## Upload = the real thing
Same screens, but at the end each organization is sent to CargoWise and the response recorded.

## Recommendation
Dry-run every NEW file format first. When the dry run looks right, click Upload."),

            new("templates", "Templates & self-learning",
@"## Self-learning (automatic)
Every time you confirm a mapping, CargoSync remembers it FOR THAT CLIENT. Next file from the same client: your columns are recalled automatically — including your manual corrections. No clicks needed; it just gets smarter.

## Templates (manual)
On the mapping screen, Save Template stores the complete setup — column mappings, constants, rules and value maps — under a name. Load Template applies it to a new file. Templates can be per-client or global.

Use templates when you juggle several distinct file layouts; rely on self-learning for the everyday case."),

            new("rules-valuemaps", "Rules & Value Maps",
@"## Rules (IF / THEN, no code)
On the mapping screen's Rules tab, build conditions from dropdowns:
- IF column ""Type"" contains ""IMP"" THEN set Is Consignee = true
- IF column ""Region"" is empty THEN set Country = AU

Rules run on every row just before validation. They save with templates and with the client's learned memory.

## Value Maps
Per-field translations of YOUR codes to CargoWise values:
- Field Address ▸ Country: when the value is ""Oz"" send ""AU"" instead
Set them on the Value Maps tab; they apply when the row is read."),

            new("ai-features", "AI features (and the off switch)",
@"## What AI does here
- Suggests mappings for cryptic column names the fuzzy matcher can't place
- Fixes values rules can't (""Ozztralia"" → AU)
- Infers a missing country from a city; derives a closest-port code
- Copilot chat on the mapping screen — ask anything about your file

## The chip
The pill at the top of the main screen always shows the truth:
- Green ""AI: On (provider)"" — active
- Gray ""AI: Off"" — disabled; everything runs deterministically
- Amber ""AI: Unavailable — continuing without AI"" — provider down; the import carries on without it

Click the chip to toggle AI or open settings. CargoSync NEVER needs AI — it is a helper, not a dependency. If a provider stops responding mid-import, CargoSync skips it automatically and finishes the job.

## Privacy
When AI is on, column names and sample values from your file are sent to the AI provider. If data must never leave the machine, keep AI off or configure a local Ollama provider (AI Settings → Add Provider → Ollama).

## Providers
Any OpenAI-compatible service works: OpenAI, OpenRouter, Google Gemini, Groq, DeepSeek, Mistral, local Ollama, plus Anthropic Claude. Add several — they form a fallback chain (top one is tried first). Every AI suggestion still requires your approval before it touches your data."),

            new("import-history", "Import History (sync ledger)",
@"The Import History button shows everything ever sent to CargoWise for the selected client: the code you sent, the code CargoWise stored (they differ when CargoWise auto-generates codes), the status, and CargoWise's internal key.

CargoSync also uses this ledger to warn you when a file contains rows that were already imported before — re-sending them simply updates the existing organizations.

Export CSV gives you the ledger as a spreadsheet."),

            new("glossary", "Glossary — the jargon, translated",
@"- eAdaptor — CargoWise's electronic mailbox for incoming data. CargoSync posts organizations to it.
- Sender ID — the account name CargoWise gives your eAdaptor connection.
- Enterprise ID — your CargoWise system's short code (e.g. CGD).
- Owner / Company Code — which CargoWise company the organizations belong to.
- Organization Code — the unique short code every CargoWise organization has (e.g. ACMIMPSYD).
- UNLOCO / Closest Port — a 5-letter world location code, e.g. AUSYD = Sydney. CargoWise often requires one; CargoSync derives it automatically from the city or country.
- PRS / WRN / ERR — CargoWise's reply per organization: Processed / Warning / Error.
- TST vs PRD — your test system vs the real production system. Practice on TST.
- Dry Run — full rehearsal, nothing sent.
- MERGE — how rows are sent: if the code already exists in CargoWise it is UPDATED, otherwise created."),

            new("troubleshooting", "Troubleshooting & FAQ",
@"## ""Test Connection"" fails
- Unreachable: check the URL (it usually ends in /eAdaptor) and your internet/VPN.
- Sign-in failed: re-check Sender ID and Password with your CargoWise admin.

## All my rows were blocked
Almost always a missing required field. Map a column (or set a constant) for Organization Code and Full Name on the mapping screen — the red status bar lists exactly what's missing.

## A row failed with ERR
Open the row in the results screen — the detail pane shows CargoWise's own message, e.g. a missing UNLOCO or an invalid code.

## The AI chip is amber
The AI provider didn't respond. Imports continue without AI automatically. Check the API key in AI Settings (Test Connection), or just leave AI off.

## Where are my logs?
Each import writes IMPORT_<client>_<time>.log to the client's log folder (set in Add Client). App and crash logs live in %AppData%\OrganizationImportTool\Logs.

## I forgot my password
On the sign-in screen, ""Forgot password?"" lets you reset it with your current password or an administrator's approval.

## Something crashed?
CargoSync writes the full details to a crash log (the error dialog shows the path). Send that file to support and keep working — your data in CargoWise is never half-written; each organization is sent individually."),
        };

        public static HelpTopic? ById(string id)
        {
            foreach (var t in Topics) if (t.Id == id) return t;
            return null;
        }
    }
}
