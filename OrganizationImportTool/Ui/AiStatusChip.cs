using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ai;

namespace OrganizationImportTool.Ui
{
    /// <summary>
    /// A small pill in the main-window header that always shows whether AI is On (green),
    /// Off (gray) or Unavailable (amber, sticky until a call succeeds again), and offers a
    /// one-click toggle. The app never depends on AI - this chip makes that visible.
    /// </summary>
    public sealed class AiStatusChip : Guna2Button
    {
        private AiSettings? _settings;
        private AiRouter? _router;
        private Action<AiStatus>? _routerHandler;
        private bool _unavailable; // sticky within a run: set by Offline/Exhausted, cleared by Succeeded

        /// <summary>Raised when the user picks "Turn AI on/off" from the chip menu.</summary>
        public event Action? ToggleRequested;

        /// <summary>Raised when the user picks "AI Settings…" from the chip menu.</summary>
        public event Action? OpenSettingsRequested;

        public AiStatusChip()
        {
            BorderRadius = 15;
            BorderThickness = 1;
            Font = AppleTheme.Body;
            Cursor = Cursors.Hand;
            // Anchor Top+Bottom stretches the chip to (row_height − Margin.Top − Margin.Bottom) at any DPI,
            // matching the adjacent DockStyle.Fill buttons whose heights also scale with the DPI-adjusted row.
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            Margin = new Padding(0, 11, 12, 11);  // symmetric vertical margin — chip fills (row − 22px) matching sibling buttons

            var menu = new ContextMenuStrip();
            AppleTheme.DarkenMenu(menu);
            var toggle = menu.Items.Add("Turn AI on/off");
            toggle.Click += (s, e) => ToggleRequested?.Invoke();
            var settings = menu.Items.Add("AI Settings…");
            settings.Click += (s, e) => OpenSettingsRequested?.Invoke();
            menu.Opening += (s, e) => toggle.Text = _settings?.Enabled == true ? "Turn AI off" : "Turn AI on";
            Click += (s, e) => menu.Show(this, new Point(0, Height));
        }

        /// <summary>Attach to the live settings + router (re-call after the router is rebuilt).</summary>
        public void Bind(AiSettings settings, AiRouter? router)
        {
            if (_router != null && _routerHandler != null) _router.StatusChanged -= _routerHandler;
            _settings = settings;
            _router = router;
            _unavailable = false;

            if (router != null)
            {
                _routerHandler = status =>
                {
                    // StatusChanged can fire from a worker continuation - marshal to the UI thread.
                    if (IsHandleCreated && InvokeRequired) { BeginInvoke(new Action(() => OnAiStatus(status))); }
                    else OnAiStatus(status);
                };
                router.StatusChanged += _routerHandler;
            }
            RefreshChip();
        }

        private void OnAiStatus(AiStatus status)
        {
            if (status.Phase is AiPhase.Offline or AiPhase.Exhausted) _unavailable = true;
            else if (status.Phase == AiPhase.Succeeded) _unavailable = false;
            RefreshChip();
        }

        /// <summary>Recompute the chip's text and colors from the current AI state.</summary>
        public void RefreshChip()
        {
            bool configured = _settings?.Enabled == true && _settings.FallbackChain.Any();
            if (!configured)
            {
                Apply("●  AI: Off", AppleTheme.TextSecondary);
            }
            else if (_unavailable)
            {
                Apply("●  AI: Unavailable — continuing without AI", AppleTheme.Warning);
            }
            else
            {
                string provider = _settings!.FallbackChain.First().Name;
                Apply($"●  AI: On ({provider})", AppleTheme.Success);
            }
        }

        private void Apply(string text, Color accent)
        {
            Text = text;
            ForeColor = accent;
            BorderColor = Color.FromArgb(90, accent);
            FillColor = Color.FromArgb(22, accent);
            HoverState.FillColor = Color.FromArgb(40, accent);
            HoverState.ForeColor = accent;
            Width = TextRenderer.MeasureText(text, Font).Width + 34;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _router != null && _routerHandler != null)
                _router.StatusChanged -= _routerHandler;
            base.Dispose(disposing);
        }
    }
}
