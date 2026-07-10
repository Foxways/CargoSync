using System;
using System.Collections.Generic;
using OrganizationImportTool.Mapping;

namespace OrganizationImportTool.Tests
{
    public class ValueMapTests
    {
        private static MappingResult WithMap(string path, params (string src, string outp)[] pairs)
        {
            var m = new MappingResult();
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (src, outp) in pairs) dict[src] = outp;
            m.ValueMaps[path] = dict;
            return m;
        }

        [Fact]
        public void Exact_match_wins_and_is_case_insensitive()
        {
            var m = WithMap("p", ("AUS", "AU"), ("NZL", "NZ"));
            Assert.Equal("AU", m.ApplyValueMap("p", "AUS"));
            Assert.Equal("AU", m.ApplyValueMap("p", " aus "));   // trimmed + case-insensitive
            Assert.Equal("NZ", m.ApplyValueMap("p", "NZL"));
        }

        [Fact]
        public void Unmatched_passes_through_when_no_default()
        {
            var m = WithMap("p", ("AUS", "AU"));
            Assert.Equal("ZZ", m.ApplyValueMap("p", "ZZ"));
        }

        [Fact]
        public void No_map_for_path_passes_through()
        {
            var m = new MappingResult();
            Assert.Equal("anything", m.ApplyValueMap("p", "anything"));
        }

        [Fact]
        public void Wildcard_glob_prefix_and_question_mark()
        {
            var m = WithMap("p", ("Aus*", "AU"), ("N?", "NZ"));
            Assert.Equal("AU", m.ApplyValueMap("p", "Australia"));
            Assert.Equal("NZ", m.ApplyValueMap("p", "NZ"));   // N? = N + one char
        }

        [Fact]
        public void Regex_pattern_matches()
        {
            var m = WithMap("p", ("regex:^A(U|US|USTRALIA)$", "AU"));
            Assert.Equal("AU", m.ApplyValueMap("p", "AU"));
            Assert.Equal("AU", m.ApplyValueMap("p", "australia"));
            Assert.Equal("XX", m.ApplyValueMap("p", "XX")); // no match -> passthrough
        }

        [Fact]
        public void Catch_all_default_applies_when_nothing_else_matches()
        {
            var m = WithMap("p", ("AUS", "AU"), ("*", "ZZ"));
            Assert.Equal("AU", m.ApplyValueMap("p", "AUS"));  // exact still wins
            Assert.Equal("ZZ", m.ApplyValueMap("p", "whatever")); // default for the rest
        }

        [Fact]
        public void Exact_beats_wildcard_and_default()
        {
            var m = WithMap("p", ("AUS", "AU"), ("A*", "AA"), ("*", "ZZ"));
            Assert.Equal("AU", m.ApplyValueMap("p", "AUS"));   // exact
            Assert.Equal("AA", m.ApplyValueMap("p", "ABC"));   // wildcard A*
            Assert.Equal("ZZ", m.ApplyValueMap("p", "QQQ"));   // default
        }

        [Fact]
        public void Malformed_regex_never_matches()
        {
            var m = WithMap("p", ("regex:[unclosed", "X"));
            Assert.Equal("input", m.ApplyValueMap("p", "input")); // passthrough, no crash
        }
    }
}
