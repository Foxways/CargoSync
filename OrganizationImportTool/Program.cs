using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OrganizationImportTool.Eadaptor;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Security;

namespace OrganizationImportTool
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Last-line-of-defence: any unhandled exception is crash-logged and shown as a
            // friendly dialog (console line in CLI modes) instead of killing the app silently.
            Logging.CrashHandler.Headless =
                args.Length > 0 && !args[0].StartsWith("--ui-", StringComparison.OrdinalIgnoreCase);
            Logging.CrashHandler.Install();

            // If a secret ever has to be stored un-encrypted (DPAPI failure), make it visible.
            SecretProtector.ProtectFailed += msg =>
            {
                if (Logging.CrashHandler.Headless) Console.WriteLine("SECURITY WARNING: " + msg);
                else MessageBox.Show(msg, "CargoSync - Security Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };

            // Put the whole app in dark mode so every native scrollbar (grids, text boxes, the main
            // screen) renders dark to match the theme.
            Ui.AppleTheme.EnableDarkMode();

            // First-run only: pre-configure OpenRouter so AI features work out-of-the-box for every
            // user. No-op once the user has their own ai-settings.json (never overwrites their key).
            Ai.DefaultAiConfig.SeedDefaultsIfMissing();

            // Headless self-test: `OrganizationImportTool.exe --selftest <url> <user> <pass>`
            // Verifies the XML builder + eAdaptor client against a live endpoint without the UI.
            if (args.Length > 0 && args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.Run(args).GetAwaiter().GetResult());
                return;
            }
            if (args.Length > 0 && args[0].Equals("--addclient", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.AddClient(args));
                return;
            }
            if (args.Length > 0 && args[0].Equals("--filetest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.FileTest(args).GetAwaiter().GetResult());
                return;
            }
            if (args.Length > 0 && args[0].Equals("--authtest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.AuthTest());
                return;
            }
            if (args.Length > 0 && args[0].Equals("--pipeline", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.Pipeline(args).GetAwaiter().GetResult());
                return;
            }
            if (args.Length > 0 && args[0].Equals("--makesample", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.MakeSample(args));
                return;
            }
            if (args.Length > 0 && args[0].Equals("--makeicon", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.MakeIcon(args));
                return;
            }
            if (args.Length > 0 && args[0].Equals("--addai", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.AddAi(args));
                return;
            }
            if (args.Length > 0 && args[0].Equals("--learntest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.LearnTest(args));
                return;
            }
            if (args.Length > 0 && args[0].Equals("--deduptest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.DedupTest());
                return;
            }
            if (args.Length > 0 && args[0].Equals("--cleantest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.CleanTest());
                return;
            }
            if (args.Length > 0 && args[0].Equals("--profiletest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.ProfileTest());
                return;
            }
            if (args.Length > 0 && args[0].Equals("--ruletest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.RuleTest());
                return;
            }
            if (args.Length > 0 && args[0].Equals("--synctest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.SyncTest());
                return;
            }
            if (args.Length > 0 && args[0].Equals("--enrichtest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.EnrichTest().GetAwaiter().GetResult());
                return;
            }
            if (args.Length > 0 && args[0].StartsWith("--ui-", StringComparison.OrdinalIgnoreCase) && args[0] != "--ui-ai" && args[0] != "--ui-create")
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string mode = args[0].ToLowerInvariant();
                try
                {
                    switch (mode)
                    {
                        case "--ui-client": Application.Run(new EAdaptorSetupForm()); return;
                        case "--ui-wizard": Application.Run(new Onboarding.WelcomeWizard(() => 0, Ai.AiSettings.Load()) { ShowInTaskbar = true }); return;
                        case "--ui-help": Application.Run(Help.HelpForm.CreateStandalone()); return;
                        case "--ui-about": Application.Run(new Help.AboutForm { ShowInTaskbar = true, StartPosition = FormStartPosition.CenterScreen }); return;
                        case "--ui-forgot": Application.Run(new Auth.ForgotPasswordForm(new Auth.UserStore())); return;
                        case "--ui-main": Application.Run(new Form1(new Auth.User { Id = 1, Username = "demo" })); return;
                        case "--ui-map":
                        {
                            var contract = FieldContract.Load();
                            var table = new Ingestion.SourceReaderFactory().Read(args.Length > 1 ? args[1] : "");
                            var mapping = new MappingSuggester(contract).Suggest(table);
                            Application.Run(new Mapping.MappingForm(contract, table, mapping, "1", new Mapping.TemplateStore()));
                            return;
                        }
                        case "--ui-results":
                        {
                            var list = new List<OrgSendOutcome>
                            {
                                new OrgSendOutcome { RowNumber = 1, SentCode = "ZZACME001", SentXml = "<Native/>", Response = new EadaptorResponse { TransportOk = true, Status = "PRS", LocalCode = "ACMIMPSYD", MessageNumber = "00000000060001", HttpStatus = 200, ProcessingLog = "OrgHeader - 1 inserts" } },
                                new OrgSendOutcome { RowNumber = 2, SentCode = "ZZBAD002", SentXml = "<Native/>", Response = new EadaptorResponse { TransportOk = true, Status = "ERR", HttpStatus = 200, Error = "UNLOCO required", ProcessingLog = "Error - UNLOCO required" } },
                            };
                            Application.Run(new ResponsePreviewForm(list));
                            return;
                        }
                        case "--ui-dedup":
                        {
                            var groups = SelfTest.SampleDuplicateGroups();
                            var dlg = new Dedup.DuplicateReviewForm(groups);
                            Ui.StepBanner.Attach(dlg, 3, 6, "Review possible duplicates",
                                "This step only appears when duplicates were found. Cancel stops the whole import.", "step-duplicates");
                            Application.Run(dlg);
                            return;
                        }
                        case "--ui-clean":
                        {
                            Application.Run(new Transform.DataCleaningForm(SelfTest.SampleCleaningChanges()));
                            return;
                        }
                        case "--ui-profile":
                        {
                            Application.Run(new Profiling.ProfileDashboardForm(SelfTest.BuildSampleProfile()));
                            return;
                        }
                        case "--ui-enrich":
                        {
                            Application.Run(new Enrichment.EnrichmentReviewForm(SelfTest.SampleEnrichment()));
                            return;
                        }
                        case "--ui-sync":
                        {
                            var entries = new Sync.FeedbackStore().ForClient("PIPELINE_TEST");
                            Application.Run(new Sync.SyncViewerForm("Pipeline", entries));
                            return;
                        }
                        case "--ui-copilot":
                        {
                            var contract = FieldContract.Load();
                            var table = new Ingestion.SourceReaderFactory().Read(args.Length > 1 ? args[1] : "");
                            var mapping = new MappingSuggester(contract).Suggest(table);
                            var router = new Ai.AiRouter(Ai.AiSettings.Load(), new Ai.TokenUsageStore());
                            Application.Run(new Ai.CopilotForm(router, contract, table, mapping));
                            return;
                        }
                        case "--ui-results-dry":
                        {
                            var vf = EadaptorResponse.ValidationFailed("Organization Code: required value is missing");
                            vf.Simulated = true;
                            var list = new List<OrgSendOutcome>
                            {
                                new OrgSendOutcome { RowNumber = 1, SentCode = "ZZACME001", SentXml = "<Native version=\"2.0\">…built…</Native>", Response = EadaptorResponse.SimulatedOk("ZZACME001") },
                                new OrgSendOutcome { RowNumber = 2, SentCode = "ZZGLOBE002", SentXml = "<Native version=\"2.0\">…built…</Native>", Response = EadaptorResponse.SimulatedOk("ZZGLOBE002") },
                                new OrgSendOutcome { RowNumber = 3, SentCode = "(row 3)", SentXml = string.Empty, Response = vf },
                            };
                            Application.Run(new ResponsePreviewForm(list));
                            return;
                        }
                    }
                }
                catch (Exception ex) { MessageBox.Show(ex.ToString()); }
                return;
            }
            if (args.Length > 0 && args[0].Equals("--ui-create", StringComparison.OrdinalIgnoreCase))
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var st = new Auth.UserStore(); try { st.EnsureSchema(); } catch (Exception ex) { Logging.AppLog.Warn("User schema check failed", ex); }
                Application.Run(new Auth.CreateUserForm(st));
                return;
            }
            if (args.Length > 0 && args[0].Equals("--ui-ai", StringComparison.OrdinalIgnoreCase))
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Ai.AiSettingsForm(Ai.AiSettings.Load(), new Ai.TokenUsageStore()));
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // ---- Authentication gate ----
            var users = new Auth.UserStore();
            try { users.EnsureSchema(); } catch (Exception ex) { Logging.AppLog.Error("User schema creation failed - sign-in may not work", ex); }

            Auth.User? currentUser = null;
            using (var login = new Auth.LoginForm(users))
            {
                Application.Run(login);
                currentUser = login.AuthenticatedUser;
            }
            if (currentUser == null) return; // window closed without signing in

            Application.Run(new Form1(currentUser));
        }
    }

    /// <summary>Console verification of the build+send pipeline against a real eAdaptor.</summary>
    internal static class SelfTest
    {
        /// <summary>Seed/refresh the "my cargowise" TST client in data.db. Idempotent.</summary>
        public static int AddClient(string[] args)
        {
            try
            {
                string name = "my cargowise";
                string url = "https://your-cargowise-host/eAdaptor";
                string user = "sender-id";
                string pass = args.Length > 1 ? args[1] : "";
                string logPath = AppPaths.LogsDir;

                string dbPath = AppPaths.DbPath;
                using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
                conn.Open();

                // remove any existing entry with this name, then insert fresh
                int? existingClientId = null;
                using (var c = new SQLiteCommand("SELECT Id FROM Clients WHERE Name=@n", conn))
                {
                    c.Parameters.AddWithValue("@n", name);
                    var r = c.ExecuteScalar();
                    if (r != null && r != DBNull.Value) existingClientId = Convert.ToInt32(r);
                }
                if (existingClientId.HasValue)
                {
                    using var d1 = new SQLiteCommand("DELETE FROM EAdaptors WHERE ClientId=@id", conn);
                    d1.Parameters.AddWithValue("@id", existingClientId.Value); d1.ExecuteNonQuery();
                    using var d2 = new SQLiteCommand("DELETE FROM Clients WHERE Id=@id", conn);
                    d2.Parameters.AddWithValue("@id", existingClientId.Value); d2.ExecuteNonQuery();
                }

                using (var ins = new SQLiteCommand("INSERT INTO Clients (Name) VALUES (@n)", conn))
                { ins.Parameters.AddWithValue("@n", name); ins.ExecuteNonQuery(); }
                int clientId = (int)conn.LastInsertRowId;

                using (var ea = new SQLiteCommand(
                    "INSERT INTO EAdaptors (ClientId, Environment, URL, SenderID, Password, LogPath, CompanyCode, EnterpriseID) " +
                    "VALUES (@c,@e,@u,@s,@p,@l,@cc,@ent)", conn))
                {
                    ea.Parameters.AddWithValue("@c", clientId);
                    ea.Parameters.AddWithValue("@e", "TST");
                    ea.Parameters.AddWithValue("@u", url);
                    ea.Parameters.AddWithValue("@s", user);
                    ea.Parameters.AddWithValue("@p", SecretProtector.Protect(pass));
                    ea.Parameters.AddWithValue("@l", logPath);
                    ea.Parameters.AddWithValue("@cc", "");      // empty -> builder uses contract owner default (CENGLOBAL)
                    ea.Parameters.AddWithValue("@ent", "CGD");
                    ea.ExecuteNonQuery();
                }

                Console.WriteLine($"Client '{name}' saved (clientId={clientId}). DB: {dbPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ADDCLIENT EXCEPTION: " + ex.Message);
                return 4;
            }
        }

        /// <summary>End-to-end headless test of the FULL pipeline:
        /// read any file -> fuzzy auto-map -> build Native XML -> send -> report.
        /// Usage: --filetest &lt;file&gt; &lt;url&gt; &lt;user&gt; &lt;pass&gt;</summary>
        public static async Task<int> FileTest(string[] args)
        {
            try
            {
                string file = args.Length > 1 ? args[1] : "";
                string url = args.Length > 2 ? args[2] : "";
                string user = args.Length > 3 ? args[3] : "";
                string pass = args.Length > 4 ? args[4] : "";

                var contract = FieldContract.Load();
                var table = new Ingestion.SourceReaderFactory().Read(file);
                Console.WriteLine($"Read '{Path.GetFileName(file)}': {table.RowCount} rows, {table.ColumnCount} cols.");

                // Load configured AI (if any) so we exercise the AI mapping advisor + UNLOCO derivation.
                var aiSettings = Ai.AiSettings.Load();
                Ai.AiRouter router = null;
                if (aiSettings.Enabled && aiSettings.FallbackChain.Any())
                {
                    var first = aiSettings.FallbackChain.First();
                    router = new Ai.AiRouter(aiSettings, new Ai.TokenUsageStore());
                    Console.WriteLine($"AI enabled: {first.Name} ({first.Model})");
                }

                var suggester = new MappingSuggester(contract);
                var mapping = suggester.Suggest(table);

                if (router != null)
                {
                    var advisor = new Mapping.AiMappingAdvisor(router, aiSettings.UseAiForLowConfidenceOnly);
                    mapping = await advisor.RefineAsync(table, mapping, contract);
                    Console.WriteLine($"AI refined {advisor.LastChangedCount} low-confidence column(s).");
                }

                Console.WriteLine("Final mapping (header -> CargoWise field [confidence/source]):");
                foreach (var c in mapping.Columns)
                    Console.WriteLine($"  {c.SourceHeader,-16} -> {c.TargetPath ?? "(unmapped)",-34} [{c.Confidence}/{c.Source}]");
                if (mapping.UnmappedRequired.Count > 0)
                    Console.WriteLine("  WARNING unmapped required: " + string.Join(", ", mapping.UnmappedRequired.ConvertAll(f => f.Label)));

                var included = mapping.Columns.FindAll(c => c.Include && !string.IsNullOrEmpty(c.TargetPath));
                var builder = new OrganizationXmlBuilder(contract);
                var validator = new Validation.OrgValidator(contract);
                var client = new EadaptorClient(url, user, pass);

                using var importLog = new Logging.ImportLog(Path.GetTempPath(), "filetest");
                importLog.Header("filetest", "TST", url, user, file, table.RowCount);
                importLog.Mapping(mapping.Columns, mapping.Constants);

                int ok = 0, notSent = 0, n = 0;
                foreach (var row in table.Rows)
                {
                    n++;
                    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var col in included)
                    {
                        string v = row[col.SourceHeader];
                        if (!string.IsNullOrWhiteSpace(v)) values[col.TargetPath!] = v.Trim();
                    }
                    var derived = await Transform.SmartDefaults.FillMissingAsync(values, router, aiSettings.Enabled);
                    foreach (var d in derived) Console.WriteLine($"   brain: {d}");

                    string code = values.TryGetValue("orgHeader.code", out var cc) ? cc : $"(row {row.RowNumber})";

                    var report = validator.Validate(values);
                    var warns = report.Warnings.Select(w => $"{w.Label}: {w.Message}").ToList();
                    if (report.HasErrors)
                    {
                        notSent++;
                        var fo = new OrgSendOutcome { RowNumber = row.RowNumber, SentCode = code, Response = EadaptorResponse.ValidationFailed(report.ErrorText) };
                        importLog.Row(n, fo, warns);
                        Console.WriteLine($"  NOT SENT (row {row.RowNumber}): {report.ErrorText}");
                        continue;
                    }

                    string xml = builder.Build(values, contract.OwnerCodeDefault, false);
                    if (string.IsNullOrEmpty(url)) { Console.WriteLine($"  build-only {code}: {xml.Length} chars"); continue; }
                    var resp = await client.SendAsync(xml);
                    if (resp.IsSuccess) ok++;
                    importLog.Row(n, new OrgSendOutcome { RowNumber = row.RowNumber, SentCode = code, SentXml = xml, Response = resp }, warns);
                    Console.WriteLine($"  SEND {code}: {resp.Status} - {resp.Outcome}  (stored {resp.LocalCode}, msg {resp.MessageNumber})");
                    if (!resp.IsSuccess) Console.WriteLine($"       error: {resp.Error}");
                }
                importLog.Summary(table.RowCount, ok, 0, notSent, table.RowCount - ok - notSent, TimeSpan.Zero);
                Console.WriteLine($"Result: {ok}/{table.RowCount} succeeded.");
                Console.WriteLine($"---- DETAILED LOG ({importLog.FilePath}) ----");
                importLog.Dispose();
                Console.WriteLine(File.ReadAllText(importLog.FilePath));
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FILETEST EXCEPTION: " + ex);
                return 5;
            }
        }

        /// <summary>
        /// Run the FULL pre-send pipeline headlessly via the SAME ImportPipeline the app uses,
        /// auto-accepting every review gate, and submit to eAdaptor.
        /// No URL = build-only (dry run). Usage: --pipeline &lt;file&gt; [url] [user] [pass]
        /// </summary>
        public static async Task<int> Pipeline(string[] args)
        {
            try
            {
                string file = args.Length > 1 ? args[1] : "";
                string url = args.Length > 2 ? args[2] : "";
                string user = args.Length > 3 ? args[3] : "";
                string pass = args.Length > 4 ? args[4] : "";

                var contract = FieldContract.Load();
                Console.WriteLine($"=== FULL PIPELINE (shared ImportPipeline): {Path.GetFileName(file)} ===\n");

                var aiSettings = Ai.AiSettings.Load();
                Ai.AiRouter router = null;
                if (aiSettings.Enabled && aiSettings.FallbackChain.Any())
                {
                    var first = aiSettings.FallbackChain.First();
                    router = new Ai.AiRouter(aiSettings, new Ai.TokenUsageStore());
                    Console.WriteLine($"AI provider: {first.Name} ({first.Model})\n");
                }

                bool dryRun = string.IsNullOrEmpty(url); // no URL = build-only preview, nothing transmitted
                var pipeline = new Pipeline.ImportPipeline(
                    contract, new Ingestion.SourceReaderFactory(), new Mapping.TemplateStore(),
                    new Sync.FeedbackStore(), router, aiSettings,
                    new EadaptorClient(url, user, pass), new Pipeline.HeadlessPipelineUi());

                var result = await pipeline.RunAsync(new Pipeline.PipelineRequest
                {
                    FilePath = file,
                    ClientId = "PIPELINE_TEST",
                    ClientName = "Pipeline",
                    Username = "pipeline",
                    OwnerCode = contract.OwnerCodeDefault,
                    DryRun = dryRun,
                    LearnMapping = false,   // the harness must not mutate a client's learned memory
                    LogDir = null
                }, System.Threading.CancellationToken.None);

                int skipped = result.Outcomes.Count(o => o.Response.IsDuplicate);
                int blocked = result.NotSent - skipped;
                Console.WriteLine(dryRun
                    ? $"\n=== PIPELINE COMPLETE (build-only): {result.WouldSend} would send, {blocked} blocked (validation), {skipped} skipped (duplicate), of {result.Outcomes.Count} rows ==="
                    : $"\n=== PIPELINE COMPLETE: {result.Ok} sent, {blocked} blocked (validation), {skipped} skipped (duplicate), of {result.Outcomes.Count} rows ===");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("PIPELINE EXCEPTION: " + ex); return 5; }
        }

        /// <summary>Configure an OpenRouter AI provider into AiSettings (encrypted) and test it.
        /// Usage: --addai &lt;apiKey&gt; [model]</summary>
        public static int AddAi(string[] args)
        {
            try
            {
                string apiKey = args.Length > 1 ? args[1] : "";
                string model = args.Length > 2 ? args[2] : "openai/gpt-oss-120b:free";

                var settings = Ai.AiSettings.Load();
                var p = Ai.AiProviderProfile.OpenRouterTemplate();
                p.Model = model;
                p.ApiKey = apiKey;
                p.Enabled = true;

                settings.Providers.RemoveAll(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                settings.Providers.Insert(0, p);
                settings.Enabled = true;
                settings.Save();
                Console.WriteLine($"Saved OpenRouter provider (model {model}), AI enabled. Key stored DPAPI-encrypted.");

                var resp = Ai.AiRouter.TestAsync(p).GetAwaiter().GetResult();
                Console.WriteLine($"Test connection: success={resp.Success}  reply='{(resp.Text ?? "").Trim()}'  tokens={resp.TotalTokens}  {(resp.Error ?? "")}");
                return resp.Success ? 0 : 7;
            }
            catch (Exception ex) { Console.WriteLine("ADDAI EXCEPTION: " + ex); return 8; }
        }

        /// <summary>
        /// Headless proof of the self-learning auto-template loop. Usage: --learntest [csv]
        /// Pass 1: operator confirms a mapping (incl. manual fixes) → memory saved.
        /// Pass 2: a fresh suggest on the same client recalls those fixes with zero clicks.
        /// </summary>
        public static int LearnTest(string[] args)
        {
            try
            {
                string file = args.Length > 1 ? args[1]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reference", "sample_cryptic.csv");
                const string clientId = "LEARNTEST_CLIENT";

                var contract = FieldContract.Load();
                var table = new Ingestion.SourceReaderFactory().Read(file);
                Console.WriteLine($"File '{Path.GetFileName(file)}': {table.RowCount} rows, headers = {string.Join(", ", table.Headers)}");

                // isolated temp template store so we never touch the real %AppData% memory
                string dir = Path.Combine(Path.GetTempPath(), "oit_learntest_templates");
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                var store = new Mapping.TemplateStore(dir);
                var suggester = new MappingSuggester(contract);

                // ---------- PASS 1: fresh suggest, operator fixes cryptic headers, learn ----------
                var m1 = suggester.Suggest(table);
                Console.WriteLine("\nPASS 1 — fresh suggestion (what fuzzy alone produced):");
                Dump(m1);

                // Simulate the operator manually correcting the two cryptic headers fuzzy missed.
                Force(m1, "AcctRef", "orgHeader.code");
                Force(m1, "LegalEntity", "orgHeader.fullName");
                Console.WriteLine("Operator manually fixed: AcctRef -> orgHeader.code, LegalEntity -> orgHeader.fullName");

                var memory = TemplateMapper.LearnFrom(m1, store.GetAuto(clientId), clientId);
                store.SaveAuto(memory, DateTime.UtcNow.ToString("o"));
                Console.WriteLine($"Saved client memory: {memory.Entries.Count(e => !e.IsConstant)} remembered column(s).");

                // ---------- PASS 2: brand-new run, recall from memory (no clicks) ----------
                var m2 = suggester.Suggest(table);
                int mappedBefore = m2.Columns.Count(c => !string.IsNullOrEmpty(c.TargetPath));
                int recalled = TemplateMapper.ApplyLearned(store.GetAuto(clientId)!, table, contract, m2);
                int mappedAfter = m2.Columns.Count(c => !string.IsNullOrEmpty(c.TargetPath));
                Console.WriteLine($"\nPASS 2 — same client, new file. Recalled {recalled} mapping(s) from memory " +
                                  $"(mapped columns {mappedBefore} -> {mappedAfter}):");
                Dump(m2);

                // Verify the manual fixes survived into pass 2 as remembered (Template/High).
                bool ok =
                    Check(m2, "AcctRef", "orgHeader.code") &
                    Check(m2, "LegalEntity", "orgHeader.fullName");
                Console.WriteLine(ok
                    ? "\nRESULT: PASS — the tool learned the operator's corrections and auto-applied them."
                    : "\nRESULT: FAIL — corrections were not recalled.");
                return ok ? 0 : 9;

                static void Force(MappingResult m, string header, string target)
                {
                    var c = m.Columns.FirstOrDefault(x => string.Equals(x.SourceHeader, header, StringComparison.OrdinalIgnoreCase));
                    if (c == null) return;
                    c.TargetPath = target; c.Include = true;
                    c.Source = MappingSource.Manual; c.Confidence = MappingConfidence.High;
                }
                static bool Check(MappingResult m, string header, string target)
                {
                    var c = m.Columns.FirstOrDefault(x => string.Equals(x.SourceHeader, header, StringComparison.OrdinalIgnoreCase));
                    bool good = c != null && string.Equals(c.TargetPath, target, StringComparison.OrdinalIgnoreCase)
                                && c.Source == MappingSource.Template;
                    Console.WriteLine($"  check {header,-14} -> {target,-22} : {(good ? "recalled (Template/High)" : "MISSING")}");
                    return good;
                }
                static void Dump(MappingResult m)
                {
                    foreach (var c in m.Columns)
                        Console.WriteLine($"    {c.SourceHeader,-14} -> {c.TargetPath ?? "(unmapped)",-22} [{c.Confidence}/{c.Source}]");
                }
            }
            catch (Exception ex) { Console.WriteLine("LEARNTEST EXCEPTION: " + ex); return 8; }
        }

        /// <summary>Headless proof of the within-file fuzzy duplicate scanner. Usage: --deduptest</summary>
        public static int DedupTest()
        {
            try
            {
                var keys = new List<Dedup.OrgKey>
                {
                    new() { RowNumber = 1, Code = "ZZACME001", Name = "Acme Imports Pty Ltd",  Country = "AU", City = "Sydney" },
                    new() { RowNumber = 2, Code = "ZZGLOBE002", Name = "Globe Traders LLC",     Country = "AU", City = "Melbourne" },
                    new() { RowNumber = 3, Code = "ZZACME001", Name = "Acme Imports (Sydney)",  Country = "AU", City = "Sydney" },   // same CODE as row 1
                    new() { RowNumber = 4, Code = "ZZACME9",   Name = "ACME IMPORTS",           Country = "AU", City = "Sydney" },   // same NAME as row 1 (suffix stripped)
                    new() { RowNumber = 5, Code = "ZZGLOBE9",  Name = "Globe Traders",          Country = "US", City = "Houston" },  // same name as row 2 but DIFFERENT country
                    new() { RowNumber = 6, Code = "ZZUNIQ006", Name = "Pacific Freight Co",     Country = "AU", City = "Perth" },    // unique
                };

                var groups = new Dedup.DuplicateScanner().Scan(keys);
                Console.WriteLine($"Scanned {keys.Count} rows -> {groups.Count} duplicate group(s):\n");
                foreach (var g in groups)
                    Console.WriteLine($"  rows [{g.RowList}]  ({g.Confidence:P0})  {g.Reason}\n     {g.Names}");

                // Expected: rows 1,3,4 cluster into one Acme group (1&3 by code, 4 joins by name);
                // rows 2 (AU) and 5 (US) NOT merged (different country); row 6 untouched.
                var acme = groups.FirstOrDefault(g => g.Rows.Any(r => r.RowNumber == 1));
                bool acmeCluster = acme != null && acme.Rows.Select(r => r.RowNumber).OrderBy(x => x).SequenceEqual(new[] { 1, 3, 4 });
                bool row4Merged = acme != null && acme.Rows.Any(r => r.RowNumber == 4);
                bool crossCountryAvoided = !groups.Any(g => g.Rows.Any(r => r.RowNumber == 2) && g.Rows.Any(r => r.RowNumber == 5));
                bool uniqueUntouched = !groups.Any(g => g.Rows.Any(r => r.RowNumber == 6));

                Console.WriteLine();
                Console.WriteLine($"  check Acme cluster = rows 1,3,4 (code+name) : {(acmeCluster ? "PASS" : "FAIL")}");
                Console.WriteLine($"  check row 4 joined by fuzzy name            : {(row4Merged ? "PASS" : "FAIL")}");
                Console.WriteLine($"  check cross-country NOT merged (2 vs 5)     : {(crossCountryAvoided ? "PASS" : "FAIL")}");
                Console.WriteLine($"  check unique row 6 left alone               : {(uniqueUntouched ? "PASS" : "FAIL")}");
                bool ok = acmeCluster && row4Merged && crossCountryAvoided && uniqueUntouched;
                Console.WriteLine(ok ? "\nRESULT: PASS" : "\nRESULT: FAIL");
                return ok ? 0 : 9;
            }
            catch (Exception ex) { Console.WriteLine("DEDUPTEST EXCEPTION: " + ex); return 8; }
        }

        /// <summary>Sample duplicate groups for the --ui-dedup visual check.</summary>
        public static List<Dedup.DuplicateGroup> SampleDuplicateGroups() => new()
        {
            new Dedup.DuplicateGroup
            {
                Reason = "Identical organization code", Confidence = 1.0,
                Rows =
                {
                    new Dedup.OrgKey { RowNumber = 1, Code = "ZZACME001", Name = "Acme Imports Pty Ltd", Country = "AU" },
                    new Dedup.OrgKey { RowNumber = 3, Code = "ZZACME001", Name = "Acme Imports (Sydney)", Country = "AU" },
                }
            },
            new Dedup.DuplicateGroup
            {
                Reason = "Similar company name (92%, same country AU)", Confidence = 0.92,
                Rows =
                {
                    new Dedup.OrgKey { RowNumber = 1, Code = "ZZACME001", Name = "Acme Imports Pty Ltd", Country = "AU" },
                    new Dedup.OrgKey { RowNumber = 4, Code = "ZZACME9", Name = "ACME IMPORTS", Country = "AU" },
                }
            },
        };

        /// <summary>Headless proof of the deterministic data-cleaning pass. Usage: --cleantest</summary>
        public static int CleanTest()
        {
            try
            {
                var contract = FieldContract.Load();
                var rows = new List<Transform.RowValues>
                {
                    new()
                    {
                        RowNumber = 1,
                        Values = new(StringComparer.OrdinalIgnoreCase)
                        {
                            ["orgHeader.code"] = "  ZZACE  001 ",
                            ["orgHeader.fullName"] = "Acme   Imports   Pty Ltd ",
                            ["orgAddressCollection[].countryCode.code"] = "Australia",
                            ["orgHeader.isConsignee"] = "Yes",
                            ["orgHeader.closestPort.code"] = "ausyd",
                            ["orgAddressCollection[].orgAddressCapabilityCollection[].addressType"] = "ofc",
                        }
                    }
                };

                // Deterministic only (no AI) so the test is reproducible.
                var changes = new Transform.DataCleaner()
                    .AnalyzeAsync(rows, contract, router: null, aiEnabled: false).GetAwaiter().GetResult();

                Console.WriteLine($"{changes.Count} change(s):");
                foreach (var c in changes)
                    Console.WriteLine($"  row{c.RowNumber}  {c.Path,-55}  \"{c.Original}\" -> \"{c.Cleaned}\"   [{c.Reason}]");

                string Got(string path) => changes.FirstOrDefault(c => c.Path == path)?.Cleaned ?? "(no change)";
                bool ws    = Got("orgHeader.code") == "ZZACE 001";
                bool name  = Got("orgHeader.fullName") == "Acme Imports Pty Ltd";
                bool ctry  = Got("orgAddressCollection[].countryCode.code") == "AU";
                bool boolN = Got("orgHeader.isConsignee") == "true";
                bool port  = Got("orgHeader.closestPort.code") == "AUSYD";
                bool enm   = Got("orgAddressCollection[].orgAddressCapabilityCollection[].addressType") == "OFC";

                Console.WriteLine();
                Console.WriteLine($"  whitespace collapse (code)     : {(ws ? "PASS" : "FAIL")}");
                Console.WriteLine($"  whitespace collapse (name)     : {(name ? "PASS" : "FAIL")}");
                Console.WriteLine($"  country name -> ISO code       : {(ctry ? "PASS" : "FAIL")}");
                Console.WriteLine($"  boolean Yes -> true            : {(boolN ? "PASS" : "FAIL")}");
                Console.WriteLine($"  port code upper-cased          : {(port ? "PASS" : "FAIL")}");
                Console.WriteLine($"  enum case fixed (ofc -> OFC)   : {(enm ? "PASS" : "FAIL")}");
                bool ok = ws && name && ctry && boolN && port && enm;

                // Live AI Auto-Fix scenario (only if a provider is configured): values the rules can't resolve.
                var aiSettings = Ai.AiSettings.Load();
                if (aiSettings.Enabled && aiSettings.FallbackChain.Any())
                {
                    Console.WriteLine("\n--- AI Auto-Fix (live) ---");
                    var router = new Ai.AiRouter(aiSettings, new Ai.TokenUsageStore());
                    var aiRows = new List<Transform.RowValues>
                    {
                        new() { RowNumber = 9, Values = new(StringComparer.OrdinalIgnoreCase)
                        {
                            ["orgAddressCollection[].countryCode.code"] = "Ozztralia",
                            ["orgAddressCollection[].orgAddressCapabilityCollection[].addressType"] = "office",
                        }}
                    };
                    var aiChanges = new Transform.DataCleaner()
                        .AnalyzeAsync(aiRows, contract, router, aiEnabled: true).GetAwaiter().GetResult();
                    foreach (var c in aiChanges)
                        Console.WriteLine($"  {c.Path}  \"{c.Original}\" -> \"{c.Cleaned}\"   [{c.Reason} / {c.Source}]");
                    if (aiChanges.Count == 0) Console.WriteLine("  (AI returned no corrections)");
                }

                Console.WriteLine(ok ? "\nRESULT: PASS" : "\nRESULT: FAIL");
                return ok ? 0 : 9;
            }
            catch (Exception ex) { Console.WriteLine("CLEANTEST EXCEPTION: " + ex); return 8; }
        }

        /// <summary>Sample cleaning changes for the --ui-clean visual check.</summary>
        public static List<Transform.CleaningChange> SampleCleaningChanges() => new()
        {
            new() { RowNumber = 1, Path = "orgHeader.fullName", FieldLabel = "Header ▸ Full Name", Original = "Acme   Imports  Pty Ltd ", Cleaned = "Acme Imports Pty Ltd", Reason = "trimmed/collapsed whitespace", Source = Transform.CleanSource.Auto },
            new() { RowNumber = 1, Path = "orgAddressCollection[].countryCode.code", FieldLabel = "Address ▸ Country", Original = "Australia", Cleaned = "AU", Reason = "country name → ISO code", Source = Transform.CleanSource.Auto },
            new() { RowNumber = 1, Path = "orgHeader.isConsignee", FieldLabel = "Roles ▸ Is Consignee", Original = "Yes", Cleaned = "true", Reason = "normalised to true/false", Source = Transform.CleanSource.Auto },
            new() { RowNumber = 2, Path = "orgAddressCollection[].countryCode.code", FieldLabel = "Address ▸ Country", Original = "Ozztralia", Cleaned = "AU", Reason = "AI corrected", Source = Transform.CleanSource.Ai },
            new() { RowNumber = 3, Path = "orgAddressCollection[].orgAddressCapabilityCollection[].addressType", FieldLabel = "Address ▸ Address Type", Original = "office", Cleaned = "OFC", Reason = "AI corrected", Source = Transform.CleanSource.Ai },
        };

        /// <summary>Sample rows for the profiler: complete, complete-no-port, and a blocking missing-code row.</summary>
        private static List<Transform.RowValues> SampleProfileRows() => new()
        {
            new() { RowNumber = 1, Values = new(StringComparer.OrdinalIgnoreCase) {
                ["orgHeader.code"]="ZZACME001", ["orgHeader.fullName"]="Acme Imports Pty Ltd",
                ["orgAddressCollection[].address1"]="12 Harbour Rd", ["orgAddressCollection[].city"]="Sydney",
                ["orgAddressCollection[].countryCode.code"]="AU", ["orgHeader.closestPort.code"]="AUSYD", ["orgHeader.isConsignee"]="true" } },
            new() { RowNumber = 2, Values = new(StringComparer.OrdinalIgnoreCase) {
                ["orgHeader.code"]="ZZGLOBE002", ["orgHeader.fullName"]="Globe Traders LLC",
                ["orgAddressCollection[].address1"]="88 Market St", ["orgAddressCollection[].city"]="Melbourne",
                ["orgAddressCollection[].countryCode.code"]="AU" } },
            new() { RowNumber = 3, Values = new(StringComparer.OrdinalIgnoreCase) {
                ["orgHeader.fullName"]="Missing Code Co", ["orgAddressCollection[].city"]="Perth",
                ["orgAddressCollection[].countryCode.code"]="AU" } },  // missing required Code -> blocking
        };

        /// <summary>Build a representative profile report (for --ui-profile and --profiletest).</summary>
        public static Profiling.ProfileReport BuildSampleProfile()
        {
            var contract = FieldContract.Load();
            // pretend dedup found 1 extra duplicate and cleaning suggested 2 fixes
            return new Profiling.DataProfiler().Profile(SampleProfileRows(), contract, duplicateRows: 1, cleaningFixes: 2);
        }

        /// <summary>Headless proof of the profiler risk scoring. Usage: --profiletest</summary>
        public static int ProfileTest()
        {
            try
            {
                var r = BuildSampleProfile();
                Console.WriteLine($"Rows={r.RowCount}  MappedFields={r.MappedFieldCount}  Risk={r.Level} (score {r.Score})");
                Console.WriteLine($"Blocking={r.BlockingRows}  Duplicates={r.DuplicateRows}  Warnings={r.WarningRows}  CleaningFixes={r.CleaningFixes}");
                Console.WriteLine("Factors:");
                foreach (var f in r.Factors) Console.WriteLine("  • " + f);
                Console.WriteLine("Field fill rates:");
                foreach (var f in r.Fields) Console.WriteLine($"  {f.Label,-28} {f.FillRate,6:P0}  distinct {f.Distinct}  {f.Note}");

                bool highRisk = r.Level == Profiling.RiskLevel.High;            // because a required field is missing
                bool blocking1 = r.BlockingRows == 1;                           // row 3 missing Code
                bool codeFactor = r.Factors.Any(f => f.Contains("Organization Code", StringComparison.OrdinalIgnoreCase));
                bool fieldsProfiled = r.Fields.Count >= 6;

                Console.WriteLine();
                Console.WriteLine($"  check High risk (missing required)   : {(highRisk ? "PASS" : "FAIL")}");
                Console.WriteLine($"  check 1 blocking row                 : {(blocking1 ? "PASS" : "FAIL")}");
                Console.WriteLine($"  check Code-missing risk factor       : {(codeFactor ? "PASS" : "FAIL")}");
                Console.WriteLine($"  check fields profiled                : {(fieldsProfiled ? "PASS" : "FAIL")}");
                bool ok = highRisk && blocking1 && codeFactor && fieldsProfiled;
                Console.WriteLine(ok ? "\nRESULT: PASS" : "\nRESULT: FAIL");
                return ok ? 0 : 9;
            }
            catch (Exception ex) { Console.WriteLine("PROFILETEST EXCEPTION: " + ex); return 8; }
        }

        /// <summary>Headless proof of the no-code rule engine (operators + enabled flag). Usage: --ruletest</summary>
        public static int RuleTest()
        {
            try
            {
                var row = new Ingestion.SourceRow { RowNumber = 1 };
                row["Type"] = "IMPORTER"; row["Country"] = "Australia"; row["Ref"] = "";

                var rules = new List<Mapping.TransformRule>
                {
                    new() { WhenColumn = "Type", Op = Mapping.RuleOp.Equals, WhenValue = "IMPORTER", ThenField = "orgHeader.isConsignee", ThenValue = "true" },
                    new() { WhenColumn = "Country", Op = Mapping.RuleOp.Contains, WhenValue = "aust", ThenField = "orgAddressCollection[].countryCode.code", ThenValue = "AU" },
                    new() { WhenColumn = "Ref", Op = Mapping.RuleOp.IsEmpty, ThenField = "orgHeader.note", ThenValue = "no-ref" },
                    new() { WhenColumn = "Type", Op = Mapping.RuleOp.StartsWith, WhenValue = "IMP", ThenField = "orgHeader.fullName", ThenValue = "Importer Co" },
                    new() { WhenColumn = "Type", Op = Mapping.RuleOp.NotEquals, WhenValue = "EXPORTER", ThenField = "orgHeader.code", ThenValue = "OK" },
                    new() { Enabled = false, WhenColumn = "Type", Op = Mapping.RuleOp.Equals, WhenValue = "IMPORTER", ThenField = "orgHeader.code", ThenValue = "SHOULD-NOT-FIRE" },
                };

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var applied = Mapping.RuleEngine.Apply(rules, row, values);

                Console.WriteLine($"{applied.Count} rule(s) fired:");
                foreach (var a in applied) Console.WriteLine("  • " + a);
                Console.WriteLine("Resulting values:");
                foreach (var kv in values) Console.WriteLine($"  {kv.Key} = {kv.Value}");

                string Got(string p) => values.TryGetValue(p, out var v) ? v : "(unset)";
                bool eq    = Got("orgHeader.isConsignee") == "true";
                bool cont  = Got("orgAddressCollection[].countryCode.code") == "AU";
                bool empty = Got("orgHeader.note") == "no-ref";
                bool starts= Got("orgHeader.fullName") == "Importer Co";
                bool neq   = Got("orgHeader.code") == "OK";
                bool disabled = Got("orgHeader.code") != "SHOULD-NOT-FIRE";
                bool count = applied.Count == 5;

                Console.WriteLine();
                Console.WriteLine($"  equals               : {(eq ? "PASS" : "FAIL")}");
                Console.WriteLine($"  contains             : {(cont ? "PASS" : "FAIL")}");
                Console.WriteLine($"  is empty             : {(empty ? "PASS" : "FAIL")}");
                Console.WriteLine($"  starts with          : {(starts ? "PASS" : "FAIL")}");
                Console.WriteLine($"  not equals           : {(neq ? "PASS" : "FAIL")}");
                Console.WriteLine($"  disabled rule skipped: {(disabled ? "PASS" : "FAIL")}");
                Console.WriteLine($"  5 rules fired        : {(count ? "PASS" : "FAIL")}");
                bool ok = eq && cont && empty && starts && neq && disabled && count;
                Console.WriteLine(ok ? "\nRESULT: PASS" : "\nRESULT: FAIL");
                return ok ? 0 : 9;
            }
            catch (Exception ex) { Console.WriteLine("RULETEST EXCEPTION: " + ex); return 8; }
        }

        /// <summary>Headless proof of the CargoWise feedback/sync ledger. Usage: --synctest</summary>
        public static int SyncTest()
        {
            try
            {
                string db = Path.Combine(Path.GetTempPath(), "oit_synctest.db");
                try { if (File.Exists(db)) File.Delete(db); } catch { }

                var store = new Sync.FeedbackStore(db);
                store.Record(new Sync.CwSyncEntry { ClientId = "C1", ClientName = "Acme", SentCode = "ZZ1", StoredCode = "ACME1", EntityPk = "GUID-1", EntityName = "OrgHeader", Status = "PRS", MessageNumber = "001", Username = "alice" });
                store.Record(new Sync.CwSyncEntry { ClientId = "C1", ClientName = "Acme", SentCode = "ZZ2", StoredCode = "ACME2", EntityPk = "GUID-2", Status = "PRS", Username = "alice" });
                store.Record(new Sync.CwSyncEntry { ClientId = "C1", ClientName = "Acme", SentCode = "ZZ3", StoredCode = "", Status = "ERR", Username = "alice" });   // rejected
                store.Record(new Sync.CwSyncEntry { ClientId = "C2", ClientName = "Globe", SentCode = "OTHER", StoredCode = "GLOBE1", Status = "PRS", Username = "bob" });

                var synced = store.SyncedCodes("C1");
                var ledger = store.ForClient("C1");
                int count = store.CountForClient("C1");

                Console.WriteLine($"C1 synced codes: {string.Join(", ", synced.OrderBy(x => x))}");
                Console.WriteLine($"C1 ledger rows : {ledger.Count}  (PRS count {count})");
                foreach (var e in ledger) Console.WriteLine($"   {e.SentCode} -> {e.StoredCode}  [{e.Status}] pk={e.EntityPk} by {e.Username}");

                bool sentAndStored = synced.Contains("ZZ1") && synced.Contains("ACME1") && synced.Contains("ZZ2") && synced.Contains("ACME2");
                bool errExcluded = !synced.Contains("ZZ3");
                bool otherClientExcluded = !synced.Contains("OTHER") && !synced.Contains("GLOBE1");
                bool ledger3 = ledger.Count == 3;
                bool prs2 = count == 2;

                Console.WriteLine();
                Console.WriteLine($"  sent+stored codes recorded   : {(sentAndStored ? "PASS" : "FAIL")}");
                Console.WriteLine($"  rejected (ERR) not 'synced'  : {(errExcluded ? "PASS" : "FAIL")}");
                Console.WriteLine($"  other client isolated        : {(otherClientExcluded ? "PASS" : "FAIL")}");
                Console.WriteLine($"  full ledger = 3 rows         : {(ledger3 ? "PASS" : "FAIL")}");
                Console.WriteLine($"  PRS count = 2                : {(prs2 ? "PASS" : "FAIL")}");
                bool ok = sentAndStored && errExcluded && otherClientExcluded && ledger3 && prs2;
                Console.WriteLine(ok ? "\nRESULT: PASS" : "\nRESULT: FAIL");
                return ok ? 0 : 9;
            }
            catch (Exception ex) { Console.WriteLine("SYNCTEST EXCEPTION: " + ex); return 8; }
        }

        /// <summary>Live proof of the enrichment providers (free Postal API + AI). Usage: --enrichtest</summary>
        public static async Task<int> EnrichTest()
        {
            try
            {
                var contract = FieldContract.Load();
                var P = Enrichment.AddressPaths.PostCode; var C = Enrichment.AddressPaths.Country;
                var CT = Enrichment.AddressPaths.City; var ST = Enrichment.AddressPaths.State;

                var rows = new List<Transform.RowValues>
                {
                    new() { RowNumber = 1, Values = new(StringComparer.OrdinalIgnoreCase) { [C] = "AU", [P] = "2000" } },     // missing city/state → Postal API
                    new() { RowNumber = 2, Values = new(StringComparer.OrdinalIgnoreCase) { [C] = "US", [P] = "90210" } },    // → Beverly Hills / CA
                    new() { RowNumber = 3, Values = new(StringComparer.OrdinalIgnoreCase) { [CT] = "Singapore" } },           // missing country → AI infers SG
                };

                var aiSettings = Ai.AiSettings.Load();
                bool aiEnabled = aiSettings.Enabled && aiSettings.FallbackChain.Any();
                Ai.AiRouter router = aiEnabled ? new Ai.AiRouter(aiSettings, new Ai.TokenUsageStore()) : null;
                Console.WriteLine($"AI {(aiEnabled ? "enabled" : "off")}. Calling the free Postal API (Zippopotam.us)…\n");

                var suggestions = await new Enrichment.EnrichmentService(router, aiEnabled).RunAsync(rows, contract);
                foreach (var s in suggestions)
                    Console.WriteLine($"  row{s.RowNumber}  {s.Path} = \"{s.Value}\"   [{s.Source}: {s.Basis}]");

                string Got(int row, string path) =>
                    suggestions.FirstOrDefault(s => s.RowNumber == row && s.Path == path)?.Value ?? "(none)";

                bool auCity = Got(1, CT) is var c1 && c1.Length > 0 && c1 != "(none)";   // a Sydney-area locality (e.g. The Rocks)
                bool nsw = string.Equals(Got(1, ST), "NSW", StringComparison.OrdinalIgnoreCase);
                bool beverly = Got(2, CT).StartsWith("Beverly", StringComparison.OrdinalIgnoreCase);
                bool ca = string.Equals(Got(2, ST), "CA", StringComparison.OrdinalIgnoreCase);
                bool sg = !aiEnabled || string.Equals(Got(3, C), "SG", StringComparison.OrdinalIgnoreCase);

                Console.WriteLine();
                Console.WriteLine($"  Postal API: AU 2000 → a city      : {(auCity ? "PASS" : "FAIL")}");
                Console.WriteLine($"  Postal API: AU 2000 → state NSW   : {(nsw ? "PASS" : "FAIL")}");
                Console.WriteLine($"  Postal API: US 90210 → Beverly... : {(beverly ? "PASS" : "FAIL")}");
                Console.WriteLine($"  Postal API: US 90210 → state CA   : {(ca ? "PASS" : "FAIL")}");
                Console.WriteLine($"  AI: city Singapore → country SG   : {(sg ? "PASS" : "FAIL")}{(aiEnabled ? "" : " (AI off, skipped)")}");
                bool ok = auCity && nsw && beverly && ca && sg;
                Console.WriteLine(ok ? "\nRESULT: PASS" : "\nRESULT: FAIL (network/AI variance possible)");
                return ok ? 0 : 9;
            }
            catch (Exception ex) { Console.WriteLine("ENRICHTEST EXCEPTION: " + ex); return 8; }
        }

        /// <summary>Sample enrichment suggestions for the --ui-enrich visual check.</summary>
        public static List<Enrichment.EnrichmentSuggestion> SampleEnrichment() => new()
        {
            new() { RowNumber = 1, Path = Enrichment.AddressPaths.City, FieldLabel = "Address ▸ City", Value = "The Rocks", Source = "Postal API", Basis = "postcode 2000, AU" },
            new() { RowNumber = 1, Path = Enrichment.AddressPaths.State, FieldLabel = "Address ▸ State / Province", Value = "NSW", Source = "Postal API", Basis = "postcode 2000, AU" },
            new() { RowNumber = 2, Path = Enrichment.AddressPaths.City, FieldLabel = "Address ▸ City", Value = "Beverly Hills", Source = "Postal API", Basis = "postcode 90210, US" },
            new() { RowNumber = 3, Path = Enrichment.AddressPaths.Country, FieldLabel = "Address ▸ Country", Value = "SG", Source = "AI", Basis = "inferred from city \"Singapore\"" },
            new() { RowNumber = 4, Path = Enrichment.AddressPaths.Country, FieldLabel = "Address ▸ Country", Value = "AE", Source = "AI", Basis = "inferred from city \"Dubai\"" },
        };

        /// <summary>Generate a rich sample organizations .xlsx (5 rows, many columns) for UI testing.
        /// Usage: --makesample [path]</summary>
        public static int MakeSample(string[] args)
        {
            try
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "sample_organizations.xlsx");

                var headers = new[]
                {
                    "Account Code", "Company Name", "Address Line 1", "City", "State", "Postcode", "Country",
                    "Closest Port", "Is Consignee", "Is Consignor", "Is Forwarder", "Email", "Phone", "Contact Name"
                };
                var rows = new[]
                {
                    new[] { "ZZSAMPLE001", "Northwind Traders Pty Ltd", "12 Harbour Road",   "Sydney",    "NSW", "2000",   "Australia",            "AUSYD", "Yes", "No",  "No",  "ops@northwind.com.au",  "+61 2 9000 1000", "Jane Smith"   },
                    new[] { "ZZSAMPLE002", "Blue Ocean Logistics LLC", "200 Bay Street",    "Auckland",  "AUK", "1010",   "New Zealand",          "NZAKL", "No",  "Yes", "Yes", "info@blueocean.co.nz",  "+64 9 300 2000",  "Tom Brown"    },
                    new[] { "ZZSAMPLE003", "Sahara Exports FZE",       "Plot 7 Free Zone",  "Dubai",     "DU",  "00000",  "United Arab Emirates", "AEDXB", "No",  "Yes", "No",  "sales@sahara.ae",       "+971 4 500 3000", "Ahmed Khan"   },
                    new[] { "ZZSAMPLE004", "Pacific Rim Trading Co",   "9 Quay Street",     "Singapore", "SG",  "049315", "Singapore",            "SGSIN", "Yes", "No",  "No",  "contact@pacrim.sg",     "+65 6000 4000",   "Li Wei"       },
                    new[] { "ZZSAMPLE005", "Atlantic Freight GmbH",    "5 Hafenstrasse",    "Hamburg",   "HH",  "20457",  "Germany",              "DEHAM", "No",  "No",  "Yes", "kontakt@atlantic.de",   "+49 40 600 5000", "Hans Mueller" },
                };

                using var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.AddWorksheet("Organizations");
                for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
                ws.Row(1).Style.Font.Bold = true;
                ws.Row(1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromArgb(30, 30, 38);
                ws.Row(1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                for (int r = 0; r < rows.Length; r++)
                    for (int c = 0; c < rows[r].Length; c++)
                        ws.Cell(r + 2, c + 1).Value = rows[r][c];
                ws.Columns().AdjustToContents();
                ws.SheetView.FreezeRows(1);
                wb.SaveAs(path);

                Console.WriteLine($"Wrote {rows.Length} organizations x {headers.Length} columns to:\n  {path}");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("MAKESAMPLE EXCEPTION: " + ex); return 8; }
        }

        /// <summary>Render the app logo into a multi-resolution .ico (PNG-encoded). Usage: --makeicon [path]</summary>
        public static int MakeIcon(string[] args)
        {
            try
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appicon.ico");
                int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };

                var pngs = new List<byte[]>();
                foreach (int s in sizes)
                {
                    using var bmp = new System.Drawing.Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.Clear(System.Drawing.Color.Transparent);
                        Ui.LogoBadge.Render(g, new System.Drawing.RectangleF(0, 0, s, s));
                    }
                    using var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }

                using (var fs = File.Create(path))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);   // ICONDIR
                    int offset = 6 + 16 * sizes.Length;
                    for (int i = 0; i < sizes.Length; i++)
                    {
                        bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));   // width  (0 = 256)
                        bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));   // height
                        bw.Write((byte)0); bw.Write((byte)0);              // colours, reserved
                        bw.Write((short)1); bw.Write((short)32);           // planes, bpp
                        bw.Write(pngs[i].Length);                          // bytes in resource
                        bw.Write(offset);                                  // image offset
                        offset += pngs[i].Length;
                    }
                    foreach (var p in pngs) bw.Write(p);
                }

                // also drop a 256px PNG preview next to it
                string png256 = Path.ChangeExtension(path, ".png");
                File.WriteAllBytes(png256, pngs[^1]);

                Console.WriteLine($"Wrote icon ({string.Join("/", sizes)} px) to:\n  {path}\n  {png256}");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("MAKEICON EXCEPTION: " + ex); return 8; }
        }

        /// <summary>Headless verification of the authentication + activity store.</summary>
        public static int AuthTest()
        {
            try
            {
                string db = Path.Combine(Path.GetTempPath(), "oit_authtest.db");
                try { if (File.Exists(db)) File.Delete(db); } catch { }

                var store = new Auth.UserStore(db);
                store.EnsureSchema();

                Console.WriteLine("create alice : " + (store.CreateUser("alice", "secret123", "my first pet") ?? "OK"));
                Console.WriteLine("dup rejected : " + (store.CreateUser("alice", "x1234", null) != null));
                Console.WriteLine("auth correct : " + (store.Authenticate("alice", "secret123") != null));
                Console.WriteLine("auth wrong   : rejected=" + (store.Authenticate("alice", "wrongpw") == null));
                Console.WriteLine("hint         : " + store.GetHint("alice"));
                Console.WriteLine("reset pw     : " + (store.ResetPassword("alice", "newpass1") ?? "OK"));
                Console.WriteLine("old pw fails : " + (store.Authenticate("alice", "secret123") == null));
                Console.WriteLine("new pw works : " + (store.Authenticate("alice", "newpass1") != null));

                var u = store.Authenticate("alice", "newpass1")!;
                var act = new Auth.ActivityStore(db);
                act.Record(u.Id, u.Username, "my cargowise", "sample_organizations.csv", 3, 3, 0, 0);
                var recent = act.Recent(5);
                Console.WriteLine($"activity rows: {recent.Count}");
                if (recent.Count > 0)
                {
                    var a = recent[0];
                    Console.WriteLine($"  -> {a.Username} imported {a.Succeeded}/{a.Total} to '{a.ClientName}' ({a.FileName})");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("AUTHTEST EXCEPTION: " + ex);
                return 6;
            }
        }

        public static async Task<int> Run(string[] args)
        {
            try
            {
                string url = args.Length > 1 ? args[1] : "";
                string user = args.Length > 2 ? args[2] : "";
                string pass = args.Length > 3 ? args[3] : "";

                var contract = FieldContract.Load();
                Console.WriteLine($"Contract loaded: {contract.Fields.Count} fields (schema {contract.SchemaVersion}).");

                // Simulate a confirmed mapping for one simple organization.
                var values = new Dictionary<string, string>
                {
                    ["orgHeader.code"] = "ZZSELFTEST1",
                    ["orgHeader.fullName"] = "ZZ Self Test Org",
                    ["orgHeader.isActive"] = "true",
                    ["orgHeader.isConsignee"] = "yes",
                    ["orgHeader.closestPort.code"] = "AUMEL",
                    ["orgAddressCollection[].code"] = "HOME",
                    ["orgAddressCollection[].address1"] = "1 Self Test Street",
                    ["orgAddressCollection[].city"] = "Melbourne",
                    ["orgAddressCollection[].state"] = "VIC",
                    ["orgAddressCollection[].postCode"] = "3000",
                    ["orgAddressCollection[].countryCode.code"] = "AU",
                    ["orgAddressCollection[].relatedPortCode.code"] = "AUMEL",
                };

                var builder = new OrganizationXmlBuilder(contract);
                string xml = builder.Build(values, ownerCode: "CENGLOBAL", enableCodeMapping: false);
                Console.WriteLine("---- Generated Native XML ----");
                Console.WriteLine(xml);

                if (string.IsNullOrEmpty(url))
                {
                    Console.WriteLine("(no url given - build-only test)");
                    return 0;
                }

                Console.WriteLine("---- Sending to eAdaptor ----");
                var client = new EadaptorClient(url, user, pass);
                var resp = await client.SendAsync(xml);
                Console.WriteLine($"Transport OK: {resp.TransportOk}  HTTP {resp.HttpStatus}");
                Console.WriteLine($"Status: {resp.Status}  Outcome: {resp.Outcome}");
                Console.WriteLine($"External: {resp.ExternalCode}  Local: {resp.LocalCode}  Entity: {resp.EntityName}");
                Console.WriteLine($"MessageNumber: {resp.MessageNumber}");
                Console.WriteLine($"ProcessingLog:\n{resp.ProcessingLog}");
                if (!resp.IsSuccess) Console.WriteLine($"Error: {resp.Error}");

                return resp.IsSuccess ? 0 : 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SELFTEST EXCEPTION: " + ex);
                return 3;
            }
        }
    }
}
