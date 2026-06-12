# CargoSync

**Import organizations into CargoWise from any spreadsheet — safely, intelligently, and with a human in the loop.**

CargoSync is a Windows desktop app (.NET 7 WinForms) that takes any Excel/CSV file of organizations and imports it into CargoWise via the eAdaptor, with smart column mapping, data quality checks and full auditability built in. Built and owned by Kishan Manohar (kishanmanohar@gmail.com).

## How an import works

```
Pick client & file
   → Step 1  Confirm the column mapping   (fuzzy + AI suggestions, self-learning per client)
   → Step 2  Data health check            (risk score, lessons learned from past rejections)
   → Step 3  Duplicates review            (only when found)
   → Step 4  Data cleaning                (only ticked fixes are applied)
   → Step 5  Fill empty fields            (postal API + AI, never overwrites)
   → Step 6  Results                      (PRS/WRN/ERR per row, re-export failed rows to fix & retry)
```

Nothing is ever sent without the operator's approval, and **Dry Run** rehearses the whole flow without transmitting anything.

## Key features

- **Any file layout** — no required headers; columns are auto-mapped (alias + fuzzy + optional AI) and every confirmed mapping is remembered per client.
- **Learns from mistakes** — CargoWise rejection reasons are memorised per client and checked against new files before sending.
- **Data quality pipeline** — pre-flight risk profile, in-file duplicate detection, value cleaning (country names → ISO codes, booleans, ports), gap enrichment.
- **Resume safely** — interrupted/crashed imports are detected; already-imported rows can be skipped (protects against duplicate orgs under CW code generation).
- **AI optional, never required** — works fully without AI; supports OpenAI, OpenRouter, Claude, Gemini, Groq, DeepSeek, Mistral and local Ollama with timeout + circuit-breaker protection.
- **Audit trail** — per-run import logs, sync ledger of everything sent vs stored, per-user activity history, sign-in with lockout protection.
- **Guided UX** — first-run wizard, in-app A-to-Z guide, step banners, tooltips everywhere.

## Building

```powershell
dotnet build OrganizationImportTool.sln          # build (exe = CargoSync.exe)
dotnet test  OrganizationImportTool.Tests        # 100 unit tests
OrganizationImportTool\Installer\build-installer.ps1   # self-contained CargoSync-Setup.exe (needs Inno Setup 6)
```

Headless verification harnesses: `CargoSync.exe --pipeline <file> [url user pass]`, `--deduptest`, `--cleantest`, `--ruletest`, `--synctest`, `--authtest`, `--profiletest`, `--enrichtest`, and `--ui-*` modes to open any screen directly.

## Notes

- User data (clients, users, ledgers) lives in `%AppData%\OrganizationImportTool\data.db`; secrets are DPAPI-encrypted.
- `Ai/DefaultAiConfig.cs` seeds a free-tier OpenRouter key on first run so AI works out of the box — rotate/spend-cap it before public distribution.
- License: see `OrganizationImportTool/Installer/LICENSE.txt`.
