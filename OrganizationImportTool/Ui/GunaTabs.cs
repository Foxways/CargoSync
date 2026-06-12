using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Guna.UI2.WinForms;

namespace OrganizationImportTool.Ui
{
    /// <summary>
    /// A modern segmented "tab" control built entirely from Guna controls (the library has no
    /// TabControl). A pill-button header switches between content pages - replaces the old,
    /// white-headered WinForms TabControl for a consistent dark look.
    /// </summary>
    public class GunaTabs : Panel
    {
        private readonly FlowLayoutPanel _header;
        private readonly Panel _content;
        private readonly List<(Guna2Button btn, Control page)> _tabs = new();

        public GunaTabs()
        {
            Dock = DockStyle.Fill;
            BackColor = AppleTheme.Canvas;

            _header = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 46, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, BackColor = AppleTheme.Canvas, Padding = new Padding(2, 6, 2, 6)
            };
            _content = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas };

            Controls.Add(_content);
            Controls.Add(_header);
        }

        public void AddTab(string title, Control page)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = _tabs.Count == 0;
            _content.Controls.Add(page);

            var btn = GunaUi.Button(title, primary: _tabs.Count == 0);
            btn.Size = new Size(Math.Max(150, TextRenderer.MeasureText(title, btn.Font).Width + 40), 34);
            btn.Margin = new Padding(0, 0, 8, 0);
            var captured = page;
            btn.Click += (s, e) => Select(captured);
            _header.Controls.Add(btn);

            _tabs.Add((btn, page));
            if (_tabs.Count == 1) Select(page);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_tabs.Count > 0) Select(_tabs[0].page); // always open on the first tab
        }

        public void Select(Control page)
        {
            foreach (var (btn, pg) in _tabs)
            {
                bool active = pg == page;
                pg.Visible = active;
                btn.FillColor = active ? AppleTheme.Accent : AppleTheme.SecondaryFill;
                btn.ForeColor = active ? Color.White : AppleTheme.TextPrimary;
                btn.BorderColor = AppleTheme.Hairline;
                btn.BorderThickness = active ? 0 : 1;
                if (active) pg.BringToFront();
            }
        }
    }
}
