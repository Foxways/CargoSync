using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Tests
{
    public class GeometryTableBuilderTests
    {
        private static WordBox W(string text, double x, double y) => new()
        {
            Text = text, X = x, Y = y, Width = text.Length * 7.0, Height = 10
        };

        private static List<WordBox> Page(params WordBox[] words) => new(words);

        [Fact]
        public void Reconstructs_a_simple_table()
        {
            var page = Page(
                W("Code", 50, 100), W("Name", 150, 100), W("City", 300, 100),
                W("ORG1", 50, 120), W("Acme", 150, 120), W("Sydney", 300, 120),
                W("ORG2", 50, 140), W("Beta", 150, 140), W("Perth", 300, 140));

            var t = GeometryTableBuilder.Build(new[] { page }, "t.pdf", "t");
            Assert.NotNull(t);
            Assert.Equal(new[] { "Code", "Name", "City" }, t!.Headers);
            Assert.Equal(2, t.RowCount);
            Assert.Equal("Acme", t.Rows[0]["Name"]);
            Assert.Equal("Perth", t.Rows[1]["City"]);
        }

        [Fact]
        public void Multi_word_cells_join_left_to_right()
        {
            var page = Page(
                W("Code", 50, 100), W("Name", 150, 100), W("City", 300, 100),
                W("ORG1", 50, 120), W("Acme", 150, 120), W("Pty", 185, 120), W("Ltd", 215, 120), W("Sydney", 300, 120),
                W("ORG2", 50, 140), W("Beta", 150, 140), W("Perth", 300, 140));

            var t = GeometryTableBuilder.Build(new[] { page }, "t.pdf", "t");
            Assert.Equal("Acme Pty Ltd", t!.Rows[0]["Name"]);
        }

        [Fact]
        public void Titles_and_page_numbers_are_dropped()
        {
            var page = Page(
                W("ORGANIZATION-REPORT", 200, 60),
                W("Code", 50, 100), W("Name", 150, 100), W("City", 300, 100),
                W("ORG1", 50, 120), W("Acme", 150, 120), W("Sydney", 300, 120),
                W("ORG2", 50, 140), W("Beta", 150, 140), W("Perth", 300, 140),
                W("1", 300, 700));

            var t = GeometryTableBuilder.Build(new[] { page }, "t.pdf", "t");
            Assert.Equal(2, t!.RowCount);
            Assert.Equal("Code", t.Headers[0]);
        }

        [Fact]
        public void Repeated_headers_on_later_pages_are_skipped()
        {
            var page1 = Page(
                W("Code", 50, 100), W("Name", 150, 100),
                W("ORG1", 50, 120), W("Acme", 150, 120),
                W("ORG2", 50, 140), W("Beta", 150, 140));
            var page2 = Page(
                W("Code", 50, 100), W("Name", 150, 100),
                W("ORG3", 50, 120), W("Gamma", 150, 120));

            var t = GeometryTableBuilder.Build(new[] { page1, page2 }, "t.pdf", "t");
            Assert.Equal(3, t!.RowCount);
            Assert.Equal("Gamma", t.Rows[2]["Name"]);
        }

        [Fact]
        public void Missing_cells_stay_empty_in_the_right_columns()
        {
            var page = Page(
                W("Code", 50, 100), W("Name", 150, 100), W("City", 300, 100),
                W("ORG1", 50, 120), W("Acme", 150, 120), W("Sydney", 300, 120),
                W("ORG2", 50, 140), W("Perth", 300, 140)); // no Name

            var t = GeometryTableBuilder.Build(new[] { page }, "t.pdf", "t");
            Assert.Equal(string.Empty, t!.Rows[1]["Name"]);
            Assert.Equal("Perth", t.Rows[1]["City"]);
        }

        [Fact]
        public void No_table_returns_null()
        {
            var page = Page(W("just", 50, 100), W("a", 90, 100), W("sentence", 110, 100));
            Assert.Null(GeometryTableBuilder.Build(new[] { page }, "t.pdf", "t"));
        }

        [Fact]
        public void Confidence_reflects_fill_ratio()
        {
            var page = Page(
                W("Code", 50, 100), W("Name", 150, 100),
                W("ORG1", 50, 120), W("Acme", 150, 120));
            var t = GeometryTableBuilder.Build(new[] { page }, "t.pdf", "t");
            Assert.Equal(1.0, GeometryTableBuilder.Confidence(t!), 2);
        }
    }
}
