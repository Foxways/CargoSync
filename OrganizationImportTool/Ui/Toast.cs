using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace OrganizationImportTool.Ui
{
    public enum ToastKind { Info, Success, Warning }

    /// <summary>
    /// Quiet in-app notifications: a small card slides into the bottom-right of the host window,
    /// auto-dismisses, never steals focus and never blocks anything. Used sparingly (lessons
    /// learned, import finished with rejections) so notifications stay a signal, not a nag.
    /// </summary>
    public static class Toast
    {
        private const int MaxVisible = 3;
        private static readonly ConditionalWeakTable<Form, List<Panel>> Active = new();

        public static void Show(Form host, string title, string message,
            ToastKind kind = ToastKind.Info, int durationMs = 7000, Action? onClick = null)
        {
            if (host.IsDisposed) return;
            if (host.InvokeRequired) { host.BeginInvoke(new Action(() => Show(host, title, message, kind, durationMs, onClick))); return; }

            var toasts = Active.GetOrCreateValue(host);
            while (toasts.Count >= MaxVisible) Dismiss(host, toasts[0]);

            Color accent = kind switch
            {
                ToastKind.Success => AppleTheme.Success,
                ToastKind.Warning => AppleTheme.Warning,
                _ => AppleTheme.Accent
            };

            int w = host.LogicalToDeviceUnits(380);
            var card = new Panel
            {
                Size = new Size(w, host.LogicalToDeviceUnits(86)),
                BackColor = Color.FromArgb(44, 44, 54),
                Cursor = onClick != null ? Cursors.Hand : Cursors.Default
            };
            var stripe = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accent };
            var titleLbl = new Label
            {
                Text = title, Dock = DockStyle.Top, Height = host.LogicalToDeviceUnits(28),
                Padding = new Padding(12, 8, 28, 0),
                Font = AppleTheme.Font(10f, FontStyle.Bold), ForeColor = accent, BackColor = Color.Transparent
            };
            var msgLbl = new Label
            {
                Text = message, Dock = DockStyle.Fill,
                Padding = new Padding(12, 0, 12, 6),
                Font = AppleTheme.Caption, ForeColor = AppleTheme.TextPrimary, BackColor = Color.Transparent
            };
            var closeBtn = new Label
            {
                Text = "✕", AutoSize = false, Size = new Size(22, 22),
                Location = new Point(card.Width - 26, 6),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Font = AppleTheme.Caption, ForeColor = AppleTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, BackColor = Color.Transparent
            };

            card.Controls.Add(msgLbl);
            card.Controls.Add(titleLbl);
            card.Controls.Add(stripe);
            card.Controls.Add(closeBtn);
            closeBtn.BringToFront();

            void ClickHandler(object? s, EventArgs e)
            {
                Dismiss(host, card);
                try { onClick?.Invoke(); } catch (Exception ex) { Logging.AppLog.Warn("Toast click handler failed", ex); }
            }
            if (onClick != null) { card.Click += ClickHandler; titleLbl.Click += ClickHandler; msgLbl.Click += ClickHandler; }
            closeBtn.Click += (s, e) => Dismiss(host, card);

            host.Controls.Add(card);
            toasts.Add(card);
            card.BringToFront();
            Restack(host, toasts);

            var life = new Timer { Interval = Math.Max(2500, durationMs) };
            life.Tick += (s, e) => { life.Dispose(); Dismiss(host, card); };
            life.Start();

            host.Resize += (s, e) => Restack(host, toasts);
        }

        private static void Dismiss(Form host, Panel card)
        {
            if (!Active.TryGetValue(host, out var toasts)) return;
            toasts.Remove(card);
            if (!card.IsDisposed) { host.Controls.Remove(card); card.Dispose(); }
            Restack(host, toasts);
        }

        private static void Restack(Form host, List<Panel> toasts)
        {
            int margin = host.LogicalToDeviceUnits(16);
            int y = host.ClientSize.Height - margin;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                if (t.IsDisposed) continue;
                y -= t.Height;
                t.Location = new Point(host.ClientSize.Width - t.Width - margin, y);
                y -= host.LogicalToDeviceUnits(8);
                t.BringToFront();
            }
        }
    }
}
