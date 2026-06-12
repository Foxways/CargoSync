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
        [InlineData("1,000", "1000")]      // US thousands
        [InlineData("1.234.567", "1234567")] // EU thousands
        [InlineData("2.0", "2")]            // integral decimal is fine for an int field
        public void Integer_parses_plain_and_thousands(string raw, string expected)
            => Assert.Equal(expected, ValueTransformer.Integer(raw));

        [Theory]
        [InlineData("abc")]
        [InlineData("1.5")]   // fractional value must NOT silently become 15
        [InlineData("1,5")]
        public void Integer_rejects_non_integral_values(string raw)
            => Assert.Null(ValueTransformer.Integer(raw));

        [Theory]
        [InlineData("1.25", "1.25")]
        [InlineData("-3.5", "-3.5")]
        [InlineData("100", "100")]
        [InlineData("1,5", "1.5")]           // European decimal comma
        [InlineData("1.234,56", "1234.56")]  // EU thousands + decimal comma
        [InlineData("1,234.56", "1234.56")]  // US thousands + decimal point
        [InlineData("1,000", "1000")]        // lone comma w/ 3 digits = thousands
        [InlineData("0.125", "0.125")]       // single dot stays a decimal point
        public void Decimal_handles_both_locale_conventions(string raw, string expected)
            => Assert.Equal(expected, ValueTransformer.Decimal(raw));

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
