using OrganizationImportTool.Mapping;
using OrganizationImportTool.Transform;
using Xunit;

namespace OrganizationImportTool.Tests
{
    /// <summary>
    /// Characterization tests for ValueTransformer. The numeric cases marked CURRENT BEHAVIOR
    /// document known-wrong separator handling and are updated when the normalizer is fixed (F5).
    /// </summary>
    public class ValueTransformerTests
    {
        [Theory]
        [InlineData("1", "true")]
        [InlineData("true", "true")]
        [InlineData("Yes", "true")]
        [InlineData("y", "true")]
        [InlineData("T", "true")]
        [InlineData("x", "true")]
        [InlineData("on", "true")]
        [InlineData("0", "false")]
        [InlineData("no", "false")]
        [InlineData("N", "false")]
        [InlineData("", "false")]
        [InlineData("banana", "false")]
        public void Bool_maps_common_truthy_values(string raw, string expected)
            => Assert.Equal(expected, ValueTransformer.Bool(raw));

        [Theory]
        [InlineData("2026-01-31", "2026-01-31")]
        [InlineData("31/01/2026", "2026-01-31")]
        [InlineData("1/2/2026", "2026-02-01")] // dd/MM preferred over MM/dd by format order
        [InlineData("31-01-2026", "2026-01-31")]
        [InlineData("31.01.2026", "2026-01-31")]
        [InlineData("20260131", "2026-01-31")]
        [InlineData("31 Jan 2026", "2026-01-31")]
        [InlineData("Jan 31, 2026", "2026-01-31")]
        public void Date_parses_common_formats_to_iso(string raw, string expected)
            => Assert.Equal(expected, ValueTransformer.Date(raw));

        [Fact]
        public void Date_returns_null_for_garbage()
            => Assert.Null(ValueTransformer.Date("not a date"));

        [Theory]
        [InlineData("42", "42")]
        [InlineData("-7", "-7")]
        [InlineData("1,000", "1000")]
        public void Integer_parses_plain_and_thousands(string raw, string expected)
            => Assert.Equal(expected, ValueTransformer.Integer(raw));

        [Fact]
        public void Integer_returns_null_for_non_numeric()
            => Assert.Null(ValueTransformer.Integer("abc"));

        [Fact]
        public void Integer_decimal_point_CURRENT_BEHAVIOR_strips_separator()
        {
            // KNOWN BUG (fixed in F5): "1.5" should be rejected or rounded, not become 15.
            Assert.Equal("15", ValueTransformer.Integer("1.5"));
        }

        [Theory]
        [InlineData("1.25", "1.25")]
        [InlineData("-3.5", "-3.5")]
        [InlineData("100", "100")]
        public void Decimal_parses_invariant_values(string raw, string expected)
            => Assert.Equal(expected, ValueTransformer.Decimal(raw));

        [Fact]
        public void Decimal_comma_separator_CURRENT_BEHAVIOR_strips_comma()
        {
            // KNOWN BUG (fixed in F5): European "1,5" should be 1.5, not 15.
            Assert.Equal("15", ValueTransformer.Decimal("1,5"));
            // KNOWN BUG (fixed in F5): "1.234,56" should be 1234.56, not 1.23456.
            Assert.Equal("1.23456", ValueTransformer.Decimal("1.234,56"));
        }

        [Fact]
        public void Coerce_truncates_strings_to_max_length()
        {
            var field = new ContractField { Path = "x", Type = "string", MaxLength = 5 };
            Assert.Equal("Hello", ValueTransformer.Coerce(field, "HelloWorld"));
        }

        [Fact]
        public void Coerce_trims_and_passes_through_unknown_field()
        {
            Assert.Equal("abc", ValueTransformer.Coerce(null, "  abc  "));
        }
    }
}
