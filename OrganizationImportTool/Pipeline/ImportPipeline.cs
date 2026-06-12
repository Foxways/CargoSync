using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Dedup;
using OrganizationImportTool.Eadaptor;
using OrganizationImportTool.Enrichment;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Logging;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Profiling;
using OrganizationImportTool.Sync;
using OrganizationImportTool.Transform;
using OrganizationImportTool.Validation;

namespace OrganizationImportTool.Pipeline
{
    /// <summary>Everything the pipeline needs to know about one import run.</summary>
    public sealed class PipelineRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        /// <summary>CargoWise owner code for the Native envelope (client company code or contract default).</summary>
        public string OwnerCode { get; set; } = string.Empty;

        /// <summary>Identical pipeline, but rows are simulated instead of transmitted.</summary>
        public bool DryRun { get; set; }

        /// <summary>Fold the operator's confirmed mapping into the client's self-learning memory.</summary>
        public bool LearnMapping { get; set; } = true;

        // Audit-log context. LogDir null = no per-run ImportLog (e.g. the CLI harness).
        public string? LogDir { get; set; }
        public string Environment { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;
    }

    public sealed class PipelineResult
    {
        public List<OrgSendOutcome> Outcomes { get; } = new();

        /// <summary>True when the operator cancelled at a review gate (or the file had no rows).</summary>
        public bool Cancelled { get; set; }
        public string? CancelledAtStage { get; set; }

        public string? ImportLogPath { get; set; }
        public TimeSpan Elapsed { get; set; }

        /// <summary>The source file's column headers, for re-exporting failed rows in their original shape.</summary>
        public IReadOnlyList<string> SourceHeaders { get; set; } = Array.Empty<string>();

        public int Ok => Outcomes.Count(o => o.Response.IsSuccess);
        public int WarningCount => Outcomes.Count(o => o.Response.IsWarning);
        public int NotSent => Outcomes.Count(o => o.Response.NotSent);
        public int Failed => Outcomes.Count - Ok;
        public int WouldSend => Outcomes.Count(o => o.Response.IsSimulatedOk);
    }

    /// <summary>
    /// The ENTIRE import flow, UI-free: read → suggest (+learned overlay + AI refine) →
    /// mapping gate → learn → profile gate → dedup gate → cleaning gate → enrichment gate →
    /// per-row rules/derive/validate/build/send → sync ledger. Form1 and the CLI harness run
    /// THIS same code with different <see cref="IPipelineUi"/> implementations, so the app and
    /// the headless verification can never drift apart again.
    /// Must be started on the UI thread when the IPipelineUi shows modal dialogs.
    /// </summary>
    public sealed class ImportPipeline
    {
        private readonly FieldContract _contract;
        private readonly SourceReaderFactory _reader;
        private readonly TemplateStore _templates;
        private readonly FeedbackStore _feedback;
        private readonly RejectionMemory _rejections;
        private readonly AiRouter? _ai;
        private readonly AiSettings _aiSettings;
        private readonly IEadaptorClient _client;
        private readonly IPipelineUi _ui;

        public ImportPipeline(FieldContract contract, SourceReaderFactory reader, TemplateStore templates,
            FeedbackStore feedback, AiRouter? ai, AiSettings aiSettings, IEadaptorClient client, IPipelineUi ui,
            RejectionMemory? rejections = null)
        {
            _contract = contract;
            _reader = reader;
            _templates = templates;
            _feedback = feedback;
            _rejections = rejections ?? new RejectionMemory();
            _ai = ai;
            _aiSettings = aiSettings;
            _client = client;
            _ui = ui;
        }

        public async Task<PipelineResult> RunAsync(PipelineRequest req, CancellationToken token)
        {
            var result = new PipelineResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            ImportLog? importLog = null;

            // AI resilience: re-arm the circuit breakers for this run, and narrate (once) when AI
            // goes offline so the operator knows the import is continuing without it.
            _ai?.ResetCircuits();
            Action<AiStatus>? aiStatusHandler = null;
            if (_ai != null)
            {
                aiStatusHandler = s =>
                {
                    if (s.Phase == AiPhase.ProviderDown)
                        _ui.Log($"AI provider {s.ProviderName} is unavailable — skipping it for the rest of this run.");
                    else if (s.Phase == AiPhase.Offline)
                        _ui.Log("AI offline — continuing without AI (deterministic rules only).");
                };
                _ai.StatusChanged += aiStatusHandler;
            }

            try
            {
                // 1) Read the file - ANY structure (xlsx/csv), no required headers.
                _ui.Log($"Reading {Path.GetFileName(req.FilePath)} ...");
                var table = _reader.Read(req.FilePath);
                if (table.RowCount == 0)
                {
                    _ui.Log("No data rows found in the file.");
                    _ui.Status("No data found !");
                    result.Cancelled = true;
                    result.CancelledAtStage = "read";
                    return result;
                }
                _ui.Log($"Loaded {table.RowCount} rows, {table.ColumnCount} columns.");
                result.SourceHeaders = table.Headers;

                // Start a detailed per-run audit log in the client's configured log folder.
                if (req.LogDir != null)
                {
                    importLog = new ImportLog(req.LogDir, req.ClientName);
                    importLog.Header(req.ClientName, req.Environment, req.Url, req.SenderId, req.FilePath, table.RowCount, req.Username);
                    if (importLog.Ok)
                    {
                        result.ImportLogPath = importLog.FilePath;
                        _ui.Log($"Detailed log: {importLog.FilePath}");
                    }
                }

                // 2) Auto-suggest header -> CargoWise field mapping (alias + fuzzy).
                var suggester = new MappingSuggester(_contract);
                var mapping = suggester.Suggest(table);

                // 2a) Self-learning memory: overlay what this client confirmed on previous uploads.
                // Highest trust, applied before AI so the AI only works on columns still unknown.
                var autoMemory = _templates.GetAuto(req.ClientId);
                if (autoMemory != null)
                {
                    int learned = TemplateMapper.ApplyLearned(autoMemory, table, _contract, mapping);
                    if (learned > 0)
                    {
                        _ui.Log($"Recalled {learned} mapping(s) from this client's history — getting smarter every upload.");
                        importLog?.Note($"Self-learning: applied {learned} remembered column mapping(s).");
                    }
                }

                // 2b) Optional AI refinement of low-confidence / unmapped columns.
                if (_ai?.IsConfigured == true)
                {
                    try
                    {
                        _ui.Status("Asking AI to refine mapping...");
                        var advisor = new AiMappingAdvisor(_ai, _aiSettings.UseAiForLowConfidenceOnly);
                        mapping = await advisor.RefineAsync(table, mapping, _contract, token);
                        _ui.Log($"AI refined {advisor.LastChangedCount} column mapping(s) using {_ai.Current.ProviderName}.");
                    }
                    catch (Exception aiEx)
                    {
                        _ui.Log($"AI refinement skipped: {aiEx.Message}");
                    }
                }

                // 3) MANDATORY user-validation gate - operator confirms/overrides every mapping.
                var confirmed = await _ui.ConfirmMappingAsync(_contract, table, mapping, req.ClientId, _templates);
                if (confirmed == null)
                {
                    _ui.Log("Mapping cancelled by user. Nothing was sent.");
                    _ui.Status("Cancelled");
                    result.Cancelled = true;
                    result.CancelledAtStage = "mapping";
                    return result;
                }
                mapping = confirmed;

                // 3b) Self-learning: remember exactly what the operator confirmed for this client,
                // folding it into the accumulating memory so the next file maps itself.
                // Skipped on a dry run — a preview must not mutate the client's learned memory.
                if (req.LearnMapping)
                {
                    try
                    {
                        var updatedMemory = TemplateMapper.LearnFrom(mapping, autoMemory, req.ClientId);
                        _templates.SaveAuto(updatedMemory, DateTime.UtcNow.ToString("o"));
                        _ui.Log("Learned this mapping — future files from this client will auto-map.");
                        importLog?.Note("Self-learning: saved confirmed mapping to client memory.");
                    }
                    catch (Exception learnEx)
                    {
                        _ui.Log($"Could not save learned mapping: {learnEx.Message}");
                    }
                }

                var includedCols = mapping.Columns
                    .Where(c => c.Include && !string.IsNullOrEmpty(c.TargetPath))
                    .ToList();

                // Build each row's mapped values once - shared by dedup and data-cleaning pre-passes.
                var rowValues = table.Rows
                    .Select(r => new RowValues { RowNumber = r.RowNumber, Values = BuildRowValues(r, includedCols, mapping) })
                    .ToList();

                // Pre-flight scans (cheap, deterministic) feed the profile dashboard's risk picture.
                var dupGroups = new List<DuplicateGroup>();
                try
                {
                    var keys = rowValues.Select(rv => new OrgKey
                    {
                        RowNumber = rv.RowNumber,
                        Code = rv.Values.TryGetValue("orgHeader.code", out var c) ? c : string.Empty,
                        Name = rv.Values.TryGetValue("orgHeader.fullName", out var n) ? n : string.Empty,
                        Country = rv.Values.TryGetValue("orgAddressCollection[].countryCode.code", out var cc) ? cc : string.Empty,
                        City = rv.Values.TryGetValue("orgAddressCollection[].city", out var ct) ? ct : string.Empty
                    }).ToList();
                    var scanner = new DuplicateScanner();
                    dupGroups = scanner.Scan(keys);
                    if (scanner.NameMatchingLimited)
                        _ui.Log($"Dedup: large file ({rowValues.Count} rows) — used exact-code matching only (fuzzy name matching skipped for performance).");
                }
                catch (Exception dupEx) { _ui.Log($"Dedup scan skipped: {dupEx.Message}"); }
                int duplicateRowCount = dupGroups.Sum(g => g.Extras.Count());

                int cleaningPreviewCount = 0;
                try { cleaningPreviewCount = (await new DataCleaner().AnalyzeAsync(rowValues, _contract, null, false, token)).Count; }
                catch (Exception cex) { AppLog.Warn("Cleaning preview count failed (profile dashboard will show 0)", cex); }

                // CargoWise feedback sync: which of these rows were already imported for this client.
                int alreadySynced = 0;
                var alreadyImportedRows = new HashSet<int>();
                try
                {
                    var synced = _feedback.SyncedCodes(req.ClientId);
                    if (synced.Count > 0)
                        foreach (var rv in rowValues)
                            if (rv.Values.TryGetValue("orgHeader.code", out var cd)
                                && !string.IsNullOrWhiteSpace(cd) && synced.Contains(cd))
                                alreadyImportedRows.Add(rv.RowNumber);
                    alreadySynced = alreadyImportedRows.Count;
                    if (alreadySynced > 0)
                        _ui.Log($"Sync: {alreadySynced} of {rowValues.Count} row(s) were already imported to CargoWise for this client.");
                }
                catch (Exception sex) { AppLog.Warn("Sync-ledger pre-check failed (already-imported count unavailable)", sex); }

                // Resume gate: offer to skip rows that already went through (this also covers a
                // previous run of this exact file that crashed mid-way - detected via the run
                // journal). Skipping protects against duplicate orgs when CargoWise regenerates codes.
                string fileHash = HashFile(req.FilePath);
                var skipAlreadyImported = new HashSet<int>();
                if (!req.DryRun && alreadySynced > 0)
                {
                    string? crashDesc = null;
                    var incomplete = fileHash.Length > 0 ? _feedback.FindIncompleteRun(req.ClientId, fileHash) : null;
                    if (incomplete != null)
                    {
                        crashDesc = $"A previous import of this exact file stopped part-way " +
                                    $"({incomplete.RowsRecorded} of {incomplete.TotalRows} rows recorded, started {incomplete.StartedUtc:g} UTC).";
                        _ui.Log("Detected an earlier import of this file that did not finish — you can resume by skipping the rows that already went through.");
                    }

                    var choice = await _ui.ConfirmResumeAsync(alreadySynced, rowValues.Count, crashDesc);
                    if (incomplete != null) _feedback.CompleteRun(incomplete.RunId); // decided: stop re-prompting
                    if (choice == ResumeChoice.Cancel)
                    {
                        _ui.Log("Import cancelled at the already-imported check. Nothing was sent.");
                        _ui.Status("Cancelled");
                        result.Cancelled = true;
                        result.CancelledAtStage = "resume";
                        return result;
                    }
                    if (choice == ResumeChoice.SkipAlreadyImported)
                    {
                        skipAlreadyImported = alreadyImportedRows;
                        _ui.Log($"Resume: skipping {skipAlreadyImported.Count} row(s) already imported successfully.");
                    }
                    else
                    {
                        _ui.Log("Re-sending all rows (existing organizations will be updated by code).");
                    }
                }

                // 3c) Data-profiling risk dashboard — a pre-flight data-health overview.
                try
                {
                    var report = new DataProfiler().Profile(rowValues, _contract, duplicateRowCount, cleaningPreviewCount, alreadySynced);
                    _ui.Log($"Profile: {report.RowCount} rows, risk {report.Level} (score {report.Score}/100).");
                    importLog?.Note($"Profile: risk {report.Level} score {report.Score}; blocking {report.BlockingRows}, dupes {report.DuplicateRows}, warnings {report.WarningRows}.");

                    // Lessons learned: CargoWise rejections from PREVIOUS imports for this client,
                    // checked against THIS file so the operator can fix repeats before sending.
                    try
                    {
                        var lessons = _rejections.ForClient(req.ClientId);
                        if (lessons.Count > 0)
                        {
                            _ui.Log($"Lessons learned: checking this file against {lessons.Count} past rejection reason(s) for this client.");
                            foreach (var lesson in lessons)
                            {
                                string line = $"Lesson from previous imports: CargoWise rejected {lesson.Count} org(s) with \"{lesson.SampleMessage}\"";
                                if (lesson.Signature.Contains("unloco") || lesson.Signature.Contains("closest port"))
                                {
                                    int missing = rowValues.Count(rv => !rv.Values.TryGetValue("orgHeader.closestPort.code", out var cp) || string.IsNullOrWhiteSpace(cp));
                                    if (missing > 0) line += $" — {missing} row(s) in THIS file have no Closest Port yet (CargoSync will try to derive it).";
                                }
                                else if (lesson.Signature.Contains("country"))
                                {
                                    int missing = rowValues.Count(rv => !rv.Values.TryGetValue("orgAddressCollection[].countryCode.code", out var cc2) || string.IsNullOrWhiteSpace(cc2));
                                    if (missing > 0) line += $" — {missing} row(s) in THIS file have no Country.";
                                }
                                report.Factors.Add(line);
                                importLog?.Note(line);
                                _ui.Log("  • " + line);
                            }
                        }
                    }
                    catch (Exception lex) { AppLog.Warn("Lessons-learned check failed", lex); }

                    if (!await _ui.ConfirmProfileAsync(report))
                    {
                        _ui.Log("Import cancelled at data profile. Nothing was sent.");
                        _ui.Status("Cancelled");
                        result.Cancelled = true;
                        result.CancelledAtStage = "profile";
                        return result;
                    }
                }
                catch (Exception profEx) { _ui.Log($"Profile skipped: {profEx.Message}"); }

                // 3d) Fuzzy dedup review.
                var skipRowNums = new HashSet<int>();
                var dupReasonByRow = new Dictionary<int, string>();
                if (dupGroups.Count > 0)
                {
                    int extras = dupGroups.Sum(g => g.Extras.Count());
                    _ui.Log($"Dedup: found {dupGroups.Count} possible duplicate group(s) covering {extras} extra row(s).");
                    importLog?.Note($"Dedup: {dupGroups.Count} possible duplicate group(s), {extras} extra row(s).");

                    var decision = await _ui.ReviewDuplicatesAsync(dupGroups);
                    if (decision.Cancelled)
                    {
                        _ui.Log("Import cancelled at duplicate review. Nothing was sent.");
                        _ui.Status("Cancelled");
                        result.Cancelled = true;
                        result.CancelledAtStage = "duplicates";
                        return result;
                    }
                    if (decision.SkipDuplicates)
                    {
                        skipRowNums = decision.RowsToSkip;
                        foreach (var g in dupGroups)
                            foreach (var extra in g.Extras)
                                dupReasonByRow[extra.RowNumber] = $"{g.Reason}; duplicate of row {g.Rows[0].RowNumber}";
                        _ui.Log($"Dedup: skipping {skipRowNums.Count} duplicate row(s); keeping the first of each group.");
                    }
                    else
                    {
                        _ui.Log("Dedup: operator chose to import all rows (duplicates included).");
                    }
                }

                // 3e) AI data cleaning + Auto-Fix: normalise values (and let AI resolve the rest) before sending.
                var cleanedByRow = new Dictionary<int, Dictionary<string, string>>();
                try
                {
                    // Only clean rows that will actually be sent (skip dedup-dropped rows).
                    var toClean = rowValues.Where(rv => !skipRowNums.Contains(rv.RowNumber)).ToList();
                    if (toClean.Count > 0)
                    {
                        _ui.Status("Cleaning data...");
                        var changes = await new DataCleaner()
                            .AnalyzeAsync(toClean, _contract, _ai, _aiSettings.Enabled, token);
                        if (changes.Count > 0)
                        {
                            int aiCount = changes.Count(c => c.Source == CleanSource.Ai);
                            _ui.Log($"Data cleaning: {changes.Count} suggested fix(es){(aiCount > 0 ? $" ({aiCount} from AI)" : "")}.");
                            importLog?.Note($"Data cleaning: {changes.Count} suggested fix(es), {aiCount} from AI.");

                            if (!await _ui.ReviewCleaningAsync(changes))
                            {
                                _ui.Log("Import cancelled at data-cleaning review. Nothing was sent.");
                                _ui.Status("Cancelled");
                                result.Cancelled = true;
                                result.CancelledAtStage = "cleaning";
                                return result;
                            }
                            cleanedByRow = DataCleaner.AcceptedOverrides(changes);
                            int applied = cleanedByRow.Sum(kv => kv.Value.Count);
                            _ui.Log($"Data cleaning: applying {applied} accepted fix(es).");
                        }
                    }
                }
                catch (Exception cleanEx)
                {
                    _ui.Log($"Data cleaning skipped: {cleanEx.Message}");
                }

                // 3f) Enrichment APIs: fill EMPTY fields from external sources (Postal API + AI).
                var enrichedByRow = new Dictionary<int, Dictionary<string, string>>();
                try
                {
                    // Enrich on the cleaned view (the Postal API needs the cleaned 2-letter country code).
                    var toEnrich = rowValues.Where(rv => !skipRowNums.Contains(rv.RowNumber))
                        .Select(rv =>
                        {
                            var v = new Dictionary<string, string>(rv.Values, StringComparer.OrdinalIgnoreCase);
                            if (cleanedByRow.TryGetValue(rv.RowNumber, out var fx)) foreach (var kv in fx) v[kv.Key] = kv.Value;
                            return new RowValues { RowNumber = rv.RowNumber, Values = v };
                        }).ToList();

                    if (toEnrich.Count > 0)
                    {
                        _ui.Status("Enriching data...");
                        var suggestions = await new EnrichmentService(_ai, _aiSettings.Enabled).RunAsync(toEnrich, _contract, token);
                        if (suggestions.Count > 0)
                        {
                            int apiN = suggestions.Count(s => s.Source == "Postal API");
                            int aiN = suggestions.Count(s => s.Source == "AI");
                            _ui.Log($"Enrichment: {suggestions.Count} empty field(s) can be filled ({apiN} Postal API, {aiN} AI).");
                            importLog?.Note($"Enrichment: {suggestions.Count} suggestion(s) ({apiN} API, {aiN} AI).");

                            if (!await _ui.ReviewEnrichmentAsync(suggestions))
                            {
                                _ui.Log("Import cancelled at enrichment review. Nothing was sent.");
                                _ui.Status("Cancelled");
                                result.Cancelled = true;
                                result.CancelledAtStage = "enrichment";
                                return result;
                            }
                            enrichedByRow = EnrichmentService.AcceptedOverrides(suggestions);
                            _ui.Log($"Enrichment: filling {enrichedByRow.Sum(kv => kv.Value.Count)} accepted field(s).");
                        }
                    }
                }
                catch (Exception enrichEx)
                {
                    _ui.Log($"Enrichment skipped: {enrichEx.Message}");
                }

                _ui.Log(req.DryRun
                    ? $"Confirmed {includedCols.Count} field mappings. Simulating {table.RowCount} organizations (no send)..."
                    : $"Confirmed {includedCols.Count} field mappings. Sending {table.RowCount} organizations...");
                importLog?.Mapping(mapping.Columns, mapping.Constants);

                // 4) Build Native XML per row and submit to eAdaptor.
                var builder = new OrganizationXmlBuilder(_contract);
                var validator = new OrgValidator(_contract);

                // Run journal: lets the NEXT upload of this file detect a crash mid-run and resume.
                string runId = req.DryRun ? string.Empty : _feedback.BeginRun(
                    req.ClientId, Path.GetFileName(req.FilePath), fileHash, table.RowCount, req.Username);

                int counter = 0;
                int throttleMs = 0; // adaptive inter-request delay; grows if CargoWise rate-limits
                foreach (var row in table.Rows)
                {
                    // Pause support: hold here until the user resumes (or stops).
                    await _ui.WaitIfPausedAsync(counter, table.RowCount, token);
                    if (token.IsCancellationRequested)
                    {
                        _ui.Log($"Upload stopped by user at {counter}/{table.RowCount}.");
                        _ui.Status("Stopped");
                        break;
                    }
                    counter++;
                    _ui.Status($"Processing organization ({counter}/{table.RowCount})");

                    // Resume: skip rows already imported successfully on a previous run.
                    if (skipAlreadyImported.Contains(row.RowNumber))
                    {
                        string skipCode = BuildRowValues(row, includedCols, mapping).TryGetValue("orgHeader.code", out var scc) ? scc : $"(row {row.RowNumber})";
                        var skipOutcome = new OrgSendOutcome
                        {
                            RowNumber = row.RowNumber, SentCode = skipCode, SentXml = string.Empty,
                            Response = EadaptorResponse.SkippedAlreadyImported("already imported successfully on a previous run"), SourceRow = row
                        };
                        result.Outcomes.Add(skipOutcome);
                        importLog?.Row(counter, skipOutcome, new List<string> { "skipped: already imported" });
                        _ui.Log($"  [{counter}] {skipCode}: SKIPPED (already imported)");
                        _ui.Progress(counter, table.RowCount);
                        continue;
                    }

                    // Dedup: skip rows the operator chose to drop as duplicates of an earlier row.
                    if (skipRowNums.Contains(row.RowNumber))
                    {
                        string dupReason = dupReasonByRow.TryGetValue(row.RowNumber, out var dr) ? dr : "duplicate of an earlier row";
                        string dupCode = BuildRowValues(row, includedCols, mapping).TryGetValue("orgHeader.code", out var dcc) ? dcc : $"(row {row.RowNumber})";
                        var dupOutcome = new OrgSendOutcome { RowNumber = row.RowNumber, SentCode = dupCode, SentXml = string.Empty, Response = EadaptorResponse.SkippedDuplicate(dupReason), SourceRow = row };
                        result.Outcomes.Add(dupOutcome);
                        importLog?.Row(counter, dupOutcome, new List<string> { "skipped: " + dupReason });
                        _ui.Log($"  [{counter}] {dupCode}: SKIPPED (duplicate) - {dupReason}");
                        _ui.Progress(counter, table.RowCount);
                        continue;
                    }

                    var values = BuildRowValues(row, includedCols, mapping);

                    // Apply the operator-accepted data-cleaning fixes for this row.
                    if (cleanedByRow.TryGetValue(row.RowNumber, out var fixes))
                        foreach (var fix in fixes)
                            values[fix.Key] = fix.Value;

                    // Apply accepted enrichment (fills empty fields from external sources).
                    if (enrichedByRow.TryGetValue(row.RowNumber, out var enrich))
                        foreach (var en in enrich)
                            values[en.Key] = en.Value;

                    // Apply the operator's no-code IF-THEN rules.
                    if (mapping.Rules.Count > 0)
                    {
                        var ruleHits = RuleEngine.Apply(mapping.Rules, row, values);
                        if (ruleHits.Count > 0)
                        {
                            _ui.Log($"  [{counter}] rule(s) applied: {string.Join(" | ", ruleHits)}");
                            importLog?.Note($"Row {row.RowNumber} rules: {string.Join(" | ", ruleHits)}");
                        }
                    }

                    // Inbuilt brain: derive values the client omitted but CargoWise needs
                    // (e.g. ClosestPort UN/LOCODE) - deterministically, with AI fallback when enabled.
                    var derived = await SmartDefaults.FillMissingAsync(values, _ai, _aiSettings.Enabled, token);

                    string code = values.TryGetValue("orgHeader.code", out var cc) ? cc : $"(row {row.RowNumber})";
                    if (derived.Count > 0)
                        _ui.Log($"  [{counter}] {code}: auto-filled {string.Join("; ", derived)}");

                    // Pre-send validation: never POST a definitely-broken row to CargoWise.
                    var report = validator.Validate(values);
                    var warnList = report.Warnings.Select(w => $"{w.Label}: {w.Message}").ToList();
                    warnList.AddRange(derived.Select(d => "auto-filled " + d));
                    if (report.HasErrors)
                    {
                        var vr = EadaptorResponse.ValidationFailed(report.ErrorText);
                        if (req.DryRun) vr.Simulated = true; // preview label: "Would NOT send (validation)"
                        // include the would-be XML so the operator can inspect even a blocked row in a dry run
                        string failXml = req.DryRun ? SafeBuild(builder, values, req.OwnerCode) : string.Empty;
                        var failOutcome = new OrgSendOutcome { RowNumber = row.RowNumber, SentCode = code, SentXml = failXml, Response = vr, SourceRow = row };
                        result.Outcomes.Add(failOutcome);
                        Logger.LogFailure($"{code} -> validation failed: {report.ErrorText}");
                        importLog?.Row(counter, failOutcome, warnList);
                        _ui.Log($"  [{counter}] {code}: {(req.DryRun ? "WOULD NOT SEND" : "NOT SENT")} - {report.ErrorText}");
                        _ui.Progress(counter, table.RowCount);
                        continue;
                    }
                    foreach (var w in report.Warnings)
                        Logger.LogSuccess($"{code} warning - {w.Label}: {w.Message}");

                    string xml = builder.Build(values, req.OwnerCode, enableCodeMapping: false);

                    // Dry run: build + record the would-be request, but never transmit it.
                    if (req.DryRun)
                    {
                        var simOutcome = new OrgSendOutcome { RowNumber = row.RowNumber, SentCode = code, SentXml = xml, Response = EadaptorResponse.SimulatedOk(code), SourceRow = row };
                        result.Outcomes.Add(simOutcome);
                        importLog?.Row(counter, simOutcome, warnList);
                        _ui.Log($"  [{counter}] {code}: would send ✓ ({xml.Length} chars of Native XML built)");
                        _ui.Progress(counter, table.RowCount);
                        continue;
                    }

                    var resp = await _client.SendAsync(xml, token);
                    var outcome = new OrgSendOutcome { RowNumber = row.RowNumber, SentCode = code, SentXml = xml, Response = resp, SourceRow = row };
                    result.Outcomes.Add(outcome);

                    // CargoWise feedback sync: record what CW told us back (sent -> stored code, PK, status).
                    try
                    {
                        _feedback.Record(new CwSyncEntry
                        {
                            ClientId = req.ClientId, ClientName = req.ClientName,
                            SentCode = code, StoredCode = resp.LocalCode, EntityPk = resp.EntityPk,
                            EntityName = resp.EntityName, Status = resp.Status, MessageNumber = resp.MessageNumber,
                            Username = req.Username, SyncedUtc = DateTime.UtcNow, RunId = runId
                        });
                    }
                    catch (Exception fex) { AppLog.Warn($"Sync ledger record failed for '{code}' (resume detection may miss this row)", fex); }

                    if (resp.IsSuccess)
                        Logger.LogSuccess($"{code} -> {resp.Outcome} ({resp.LocalCode}) msg {resp.MessageNumber}");
                    else
                        Logger.LogFailure($"{code} -> {resp.Status}: {resp.Error}");

                    // Learn from the mistake: remember CargoWise DATA rejections (not transport
                    // hiccups) so the next import for this client is warned about repeats up front.
                    if (resp.TransportOk && !resp.IsSuccess && !resp.IsWarning)
                        _rejections.Record(req.ClientId, resp.Error ?? resp.ProcessingLog, code);

                    importLog?.Row(counter, outcome, warnList);
                    _ui.Log($"  [{counter}] {code}: {resp.Status} - {resp.Outcome}");
                    _ui.Progress(counter, table.RowCount);

                    // Adaptive throttle: back off if CargoWise rate-limits / blocks, recover when clear.
                    if (resp.HttpStatus == 429 || resp.HttpStatus == 503 || (!resp.TransportOk && !resp.NotSent))
                        throttleMs = Math.Min(throttleMs == 0 ? 800 : throttleMs + 600, 5000);
                    else
                        throttleMs = Math.Max(0, throttleMs - 200);

                    if (throttleMs > 0)
                    {
                        _ui.Status($"Sent {counter}/{table.RowCount} — easing off {throttleMs} ms to avoid blocking…");
                        await Task.Delay(throttleMs);
                    }
                }

                stopwatch.Stop();

                // Close the run journal ONLY when the loop genuinely finished. A user Stop or a
                // crash leaves it open, which is exactly what triggers the resume offer next time.
                if (!req.DryRun && !token.IsCancellationRequested && runId.Length > 0)
                    _feedback.CompleteRun(runId);

                if (req.DryRun)
                {
                    int wouldSend = result.WouldSend;
                    int blocked = result.Outcomes.Count - wouldSend;
                    importLog?.Summary(result.Outcomes.Count, wouldSend, 0, blocked, 0, stopwatch.Elapsed);
                    _ui.Log($"\r\nDry run complete. {wouldSend} would be sent, {blocked} blocked by validation, of {result.Outcomes.Count}.");
                    _ui.Log("Nothing was transmitted to CargoWise. Review the preview, then click Upload to import for real.");
                    if (importLog?.Ok == true) _ui.Log($"Full details written to: {importLog.FilePath}");
                    _ui.Status($"Dry run: {wouldSend}/{result.Outcomes.Count} would send");
                }
                else
                {
                    int ok = result.Ok;
                    int warnCount = result.WarningCount;
                    int notSentCount = result.NotSent;
                    int rejected = result.Outcomes.Count - ok - warnCount - notSentCount;
                    importLog?.Summary(result.Outcomes.Count, ok, warnCount, notSentCount, rejected, stopwatch.Elapsed);

                    int skippedTotal = result.Outcomes.Count(o => o.Response.IsDuplicate || o.Response.IsAlreadyImported);
                    int blocked = notSentCount - skippedTotal;
                    var parts = new List<string> { $"{ok} succeeded" };
                    if (warnCount > 0) parts.Add($"{warnCount} stored with warnings");
                    if (rejected > 0) parts.Add($"{rejected} rejected by CargoWise");
                    if (blocked > 0) parts.Add($"{blocked} blocked (validation)");
                    if (skippedTotal > 0) parts.Add($"{skippedTotal} skipped");
                    _ui.Log($"\r\nDone. {string.Join(", ", parts)} of {result.Outcomes.Count}.");
                    if (importLog?.Ok == true) _ui.Log($"Full details written to: {importLog.FilePath}");
                    _ui.Status($"Complete: {ok}/{result.Outcomes.Count} ok");
                }

                return result;
            }
            finally
            {
                if (_ai != null && aiStatusHandler != null) _ai.StatusChanged -= aiStatusHandler;
                stopwatch.Stop();
                result.Elapsed = stopwatch.Elapsed;
                importLog?.Dispose();
            }
        }

        /// <summary>Map one source row to CargoWise target-path values (column maps + constants), pre-derivation.</summary>
        public static Dictionary<string, string> BuildRowValues(SourceRow row, IReadOnlyList<ColumnMapping> includedCols, MappingResult mapping)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in includedCols)
            {
                string v = row[col.SourceHeader];
                if (string.IsNullOrWhiteSpace(v)) continue;
                // apply any client value-map (code lookups / conditionals) before transform
                values[col.TargetPath!] = mapping.ApplyValueMap(col.TargetPath!, v.Trim());
            }
            // constants / defaults apply to every row
            foreach (var kv in mapping.Constants)
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    values[kv.Key] = kv.Value;
            return values;
        }

        /// <summary>Build Native XML without throwing — used to preview a validation-blocked row in a dry run.</summary>
        private static string SafeBuild(OrganizationXmlBuilder builder, Dictionary<string, string> values, string ownerCode)
        {
            try { return builder.Build(values, ownerCode, enableCodeMapping: false); }
            catch { return string.Empty; }
        }

        /// <summary>SHA-256 of the source file - identifies "the same file" across runs for resume.</summary>
        private static string HashFile(string path)
        {
            try
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                using var fs = File.OpenRead(path);
                return Convert.ToHexString(sha.ComputeHash(fs));
            }
            catch (Exception ex)
            {
                AppLog.Warn("File hash failed (crash-resume detection disabled for this run)", ex);
                return string.Empty;
            }
        }
    }
}
