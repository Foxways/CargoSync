using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;

namespace OrganizationImportTool.Ui
{
    /// <summary>
    /// A searchable replacement for Guna2ComboBox, which silently forces DropDownList and
    /// therefore can never support type-to-search. This is a Guna2TextBox plus a floating
    /// ListBox: typing filters the items (contains, case-insensitive), Up/Down navigates,
    /// Enter or click selects, Esc closes, and leaving the box snaps to an exact match or
    /// reverts to the current selection. API mirrors the ComboBox members Form1 uses
    /// (Items / SelectedIndex / SelectedItem / SelectedIndexChanged), so it drops in.
    /// </summary>
    public class SearchComboBox : UserControl
    {
        private readonly Guna2TextBox _text;
        private readonly ListBox _list;
        private readonly Panel  _listHost;  // 1px dark border around the floating list
        private readonly List<string> _all = new();
        private int _selected = -1;
        private bool _suppress;

        public event EventHandler? SelectedIndexChanged;

        private readonly Label _arrow;

        public SearchComboBox()
        {
            _text = GunaUi.TextBox("Select or type to search…");
            _text.Dock = DockStyle.Fill;
            _text.TextChanged += (s, e) => { if (!_suppress) OpenList(_text.Text); };
            // Enter fires when the inner textbox gets focus (click or Tab) - the wrapper's
            // own Click never fires for clicks landing on the hosted textbox.
            _text.Enter += (s, e) => OpenList(showAll: true);
            _text.KeyDown += Text_KeyDown;
            _text.Leave += (s, e) => OnLeftControl();

            // Dropdown arrow, like the old combo: a Label so it never steals focus, which
            // would fire Leave and close the list before the toggle could open it.
            _arrow = new Label
            {
                Text = "▼", Dock = DockStyle.Right, Width = 30, Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter, ForeColor = AppleTheme.TextSecondary,
                Font = new Font(AppleTheme.Body.FontFamily, 8f), BackColor = Color.Transparent
            };
            _arrow.Click += (s, e) =>
            {
                if (_list.Visible) { CloseList(); return; }
                if (!_text.Focused) _text.Focus(); // Enter handler opens the full list
                OpenList(showAll: true);
            };

            _list = new ListBox
            {
                TabStop = false,
                BorderStyle = BorderStyle.None,
                Font = AppleTheme.Body,
                BackColor = AppleTheme.Surface,
                ForeColor = AppleTheme.TextPrimary,
                IntegralHeight = false,
                ItemHeight = 26,
                DrawMode = DrawMode.OwnerDrawFixed
            };
            _list.DrawItem += List_DrawItem;
            _list.MouseDown += List_MouseDown;
            _list.MouseMove += (s, e) => { int i = _list.IndexFromPoint(e.Location); if (i >= 0) _list.SelectedIndex = i; };

            // Host panel provides the 1px Hairline border around the floating list.
            _listHost = new Panel { Visible = false, BackColor = AppleTheme.Hairline, Padding = new Padding(1) };
            _list.Dock = DockStyle.Fill;
            _listHost.Controls.Add(_list);

            Controls.Add(_text);
            Controls.Add(_arrow);
            Height = 38;
        }

        // ---------------- ComboBox-compatible API ----------------

        public sealed class ItemCollection
        {
            private readonly SearchComboBox _o;
            internal ItemCollection(SearchComboBox o) => _o = o;
            public int Count => _o._all.Count;
            public object this[int index] => _o._all[index];
            public void Add(object item) => _o._all.Add(item?.ToString() ?? string.Empty);
            public void Clear()
            {
                _o._all.Clear();
                _o._selected = -1;
                _o._suppress = true;
                _o._text.Text = string.Empty;
                _o._suppress = false;
                _o.CloseList();
            }
        }

        private ItemCollection? _items;
        public ItemCollection Items => _items ??= new ItemCollection(this);

        public int SelectedIndex
        {
            get => _selected;
            set => Select(value, raiseEvent: true);
        }

        public object? SelectedItem => _selected >= 0 && _selected < _all.Count ? _all[_selected] : null;

        public override string Text
        {
            get => _text.Text;
            set => _text.Text = value ?? string.Empty;
        }

        /// <summary>Select the item exactly matching <paramref name="name"/> (case-insensitive). False if absent.</summary>
        public bool TrySelectExact(string name)
        {
            int idx = _all.FindIndex(n => string.Equals(n, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;
            Select(idx, raiseEvent: true);
            return true;
        }

        // ---------------- behaviour ----------------

        private void Select(int index, bool raiseEvent)
        {
            if (index < -1 || index >= _all.Count) index = -1;
            bool changed = index != _selected;
            _selected = index;
            _suppress = true;
            _text.Text = index >= 0 ? _all[index] : string.Empty;
            _text.SelectionStart = _text.Text.Length;
            _suppress = false;
            CloseList();
            if (changed && raiseEvent) SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Text_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Down:
                case Keys.Up:
                    if (!_list.Visible) OpenList(showAll: true);
                    if (_list.Items.Count > 0)
                    {
                        int i = _list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
                        _list.SelectedIndex = Math.Max(0, Math.Min(_list.Items.Count - 1, i));
                    }
                    e.Handled = true; e.SuppressKeyPress = true;
                    break;

                case Keys.Enter:
                    if (_list.Visible && _list.SelectedItem is string hl) Select(_all.IndexOf(hl), raiseEvent: true);
                    else if (_list.Visible && _list.Items.Count > 0) Select(_all.IndexOf((string)_list.Items[0]!), raiseEvent: true);
                    else TrySelectExact(_text.Text);
                    e.Handled = true; e.SuppressKeyPress = true;
                    break;

                case Keys.Escape:
                    CloseList();
                    e.Handled = true; e.SuppressKeyPress = true;
                    break;
            }
        }

        private void List_MouseDown(object? sender, MouseEventArgs e)
        {
            int i = _list.IndexFromPoint(e.Location);
            if (i >= 0 && _list.Items[i] is string name) Select(_all.IndexOf(name), raiseEvent: true);
        }

        private void List_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool hot = (e.State & DrawItemState.Selected) != 0;
            Color bg  = hot ? AppleTheme.Accent : AppleTheme.Surface;
            Color fg  = hot ? Color.White : AppleTheme.TextPrimary;
            using var bgBrush = new SolidBrush(bg);
            e.Graphics.FillRectangle(bgBrush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, _list.Items[e.Index]?.ToString(), AppleTheme.Body,
                new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                fg, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void OpenList(string? filter = null, bool showAll = false)
        {
            var form = FindForm();
            if (form == null || _all.Count == 0) return;
            if (_listHost.Parent != form) form.Controls.Add(_listHost);

            string q = showAll ? string.Empty : (filter ?? string.Empty).Trim();
            var matches = q.Length == 0
                ? _all
                : _all.Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var m in matches) _list.Items.Add(m);
            _list.EndUpdate();

            if (_list.Items.Count == 0) { _listHost.Visible = false; return; }
            _list.SelectedIndex = 0;

            var p = form.PointToClient(PointToScreen(new Point(0, Height)));
            int h = Math.Min(_list.ItemHeight * Math.Min(_list.Items.Count, 8) + 4, 220);
            _listHost.SetBounds(p.X, p.Y, Math.Max(Width, 120), h);
            _listHost.Visible = true;
            _listHost.BringToFront();
        }

        private void CloseList() => _listHost.Visible = false;

        private void OnLeftControl()
        {
            // Clicking an item moves focus to the list before its MouseDown runs - don't
            // close/snap underneath that click; List_MouseDown completes the selection.
            if (_listHost.Visible && _list.ClientRectangle.Contains(_list.PointToClient(MousePosition)))
                return;

            if (!TrySelectExact(_text.Text))
                Select(_selected, raiseEvent: false); // revert text to the real selection
            CloseList();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _text.Enabled = Enabled;
            if (!Enabled) CloseList();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _listHost.Parent?.Controls.Remove(_listHost); _listHost.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
