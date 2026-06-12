using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace OrganizationImportTool.Ui
{
    /// <summary>
    /// One shared ToolTip per form (created lazily, disposed with the form), so every control
    /// can get a plain-language hint with a single call: Tips.Set(form, button, "What this does").
    /// </summary>
    public static class Tips
    {
        private static readonly ConditionalWeakTable<Form, ToolTip> PerForm = new();

        public static void Set(Form form, Control control, string text) => For(form).SetToolTip(control, text);

        public static ToolTip For(Form form)
        {
            if (!PerForm.TryGetValue(form, out var tip))
            {
                tip = new ToolTip { AutoPopDelay = 14000, InitialDelay = 450, ReshowDelay = 200 };
                PerForm.Add(form, tip);
                form.Disposed += (s, e) => tip.Dispose();
            }
            return tip;
        }
    }
}
