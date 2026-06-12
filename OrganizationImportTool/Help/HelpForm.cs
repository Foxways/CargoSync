using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Help
{
    /// <summary>
    /// The in-app user guide: topic tree + search on the left, rendered page on the right.
    /// Non-modal singleton so it can stay open NEXT TO the app while the user follows along.
    /// </summary>
    public sealed class HelpForm : Form
    {
        private static HelpForm? _open;

        private TreeView _tree = null!;
        private RichTextBox _page = null!;
        private TextBox _search = null!;

        /// <summary>Standalone instance for the --ui-help debug mode.</summary>
        public static HelpForm CreateStandalone() => new() { StartPosition = FormStartPosition.CenterScreen };

        /// <summary>Open (or focus) the guide, optionally jumping to a topic id.</summary>
        public static void Open(IWin32Window? owner, string? topicId = null)
        {
            if (_open == null || _open.IsDisposed)
            {
                _open = new HelpForm();
                if (owner is Form f) _open.StartPosition = FormStartPosition.Manual;
                _open.Show(owner);
                if (owner is Form of)
                    _open.Location = new Point(
                        Math.Max(0, of.Location.X + (of.Width - _open.Width) / 2),
                        Math.Max(0, of.Location.Y + (of.Height - _open.Height) / 2));
            }
            _open.BringToFront();
            _open.Activate();
            if (topicId != null) _open.ShowTopic(topicId);
        }

        private HelpForm()
        {
            Text = "CargoSync Guide";
            ClientSize = new Size(980, 660);
            MinimumSize = new Size(720, 480);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            DoubleBuffered = true;
            AppleTheme.ApplyWindow(this);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = AppleTheme.Canvas, Padding = new Padding(10) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(250)));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // ---- left: search + topic tree ----
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, 0, 8, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(36)));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _search = new TextBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle,
                BackColor = AppleTheme.SurfaceAlt, ForeColor = AppleTheme.TextPrimary,
                Font = AppleTheme.Body, PlaceholderText = "Search topics…", Margin = new Padding(0, 4, 0, 4)
            };
            _search.TextChanged += (s, e) => FillTree(_search.Text);

            _tree = new TreeView
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                BackColor = AppleTheme.Surface, ForeColor = AppleTheme.TextPrimary,
                Font = AppleTheme.Body, ShowLines = false, FullRowSelect = true,
                HideSelection = false, ItemHeight = 30, ShowPlusMinus = false, ShowRootLines = false
            };
            _tree.AfterSelect += (s, e) => { if (e.Node?.Tag is HelpTopic t) HelpRenderer.Render(_page, t); };

            left.Controls.Add(_search, 0, 0);
            left.Controls.Add(_tree, 0, 1);

            // ---- right: rendered page ----
            _page = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
                BackColor = AppleTheme.Surface, ForeColor = AppleTheme.TextPrimary,
                Font = AppleTheme.Body, DetectUrls = false, Margin = new Padding(0)
            };

            root.Controls.Add(left, 0, 0);
            root.Controls.Add(_page, 1, 0);
            Controls.Add(root);

            FillTree(string.Empty);
            if (_tree.Nodes.Count > 0) _tree.SelectedNode = _tree.Nodes[0];
            FormAnimator.FadeIn(this);
        }

        private void FillTree(string filter)
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            foreach (var t in HelpContent.Topics)
            {
                if (filter.Length > 0 &&
                    !t.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !t.Body.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                _tree.Nodes.Add(new TreeNode(t.Title) { Tag = t });
            }
            _tree.EndUpdate();
            if (filter.Length > 0 && _tree.Nodes.Count > 0) _tree.SelectedNode = _tree.Nodes[0];
        }

        public void ShowTopic(string id)
        {
            foreach (TreeNode n in _tree.Nodes)
                if (n.Tag is HelpTopic t && t.Id == id) { _tree.SelectedNode = n; return; }
            // not in the (possibly filtered) tree - render directly
            var topic = HelpContent.ById(id);
            if (topic != null) HelpRenderer.Render(_page, topic);
        }
    }
}
