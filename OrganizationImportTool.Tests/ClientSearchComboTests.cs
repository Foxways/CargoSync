using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Tests
{
    /// <summary>
    /// Guards the client type-to-search picker. (History: Guna2ComboBox silently forces
    /// DropDownList, so typing was impossible - SearchComboBox replaced it.)
    /// WinForms controls need an STA thread.
    /// </summary>
    public class ClientSearchComboTests
    {
        private static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw error;
        }

        [Fact]
        public void Selecting_by_typed_name_is_case_insensitive_and_raises_event()
        {
            RunSta(() =>
            {
                using var cb = new SearchComboBox();
                cb.Items.Add("Acme Logistics");
                cb.Items.Add("Globe Trading");

                int events = 0;
                cb.SelectedIndexChanged += (s, e) => events++;

                Assert.True(cb.TrySelectExact("globe trading"));
                Assert.Equal(1, cb.SelectedIndex);
                Assert.Equal("Globe Trading", cb.SelectedItem);
                Assert.Equal("Globe Trading", cb.Text);
                Assert.Equal(1, events);

                // Re-selecting the same client must not re-fire (it would wipe the chosen file).
                Assert.True(cb.TrySelectExact("GLOBE TRADING"));
                Assert.Equal(1, events);

                Assert.False(cb.TrySelectExact("no such client"));
            });
        }

        [Fact]
        public void Clear_resets_selection_and_text()
        {
            RunSta(() =>
            {
                using var cb = new SearchComboBox();
                cb.Items.Add("Acme Logistics");
                cb.SelectedIndex = 0;
                Assert.NotNull(cb.SelectedItem);

                cb.Items.Clear();
                Assert.Equal(0, cb.Items.Count);
                Assert.Equal(-1, cb.SelectedIndex);
                Assert.Null(cb.SelectedItem);
                Assert.Equal(string.Empty, cb.Text);
            });
        }
    }
}
