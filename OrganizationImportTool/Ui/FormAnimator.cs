using System;
using System.Windows.Forms;

namespace OrganizationImportTool.Ui
{
    /// <summary>Smooth, dependency-free window animations (fade in/out) for a polished, non-jittery feel.</summary>
    public static class FormAnimator
    {
        /// <summary>Fade the form in from transparent when it first shows.</summary>
        public static void FadeIn(Form form, int durationMs = 200)
        {
            try
            {
                form.Opacity = 0d;
                form.Shown += (s, e) =>
                {
                    var t = new Timer { Interval = 12 };
                    double step = 12.0 / Math.Max(80, durationMs);
                    t.Tick += (s2, e2) =>
                    {
                        double next = form.Opacity + step;
                        if (next >= 1d) { form.Opacity = 1d; t.Stop(); t.Dispose(); }
                        else form.Opacity = next;
                    };
                    t.Start();
                };
            }
            catch { /* never let an animation break a window */ }
        }

        /// <summary>Fade the form out smoothly before it closes (use for non-modal/top-level windows only).</summary>
        public static void EnableFadeOnClose(Form form, int durationMs = 150)
        {
            bool closing = false;
            form.FormClosing += (s, e) =>
            {
                if (closing || e.CloseReason == CloseReason.WindowsShutDown) return;
                closing = true;
                e.Cancel = true;
                var t = new Timer { Interval = 12 };
                double step = 12.0 / Math.Max(80, durationMs);
                t.Tick += (s2, e2) =>
                {
                    double next = form.Opacity - step;
                    if (next <= 0d) { t.Stop(); t.Dispose(); form.Close(); }
                    else form.Opacity = next;
                };
                t.Start();
            };
        }
    }
}
