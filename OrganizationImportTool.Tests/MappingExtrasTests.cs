using System;
using System.Collections.Generic;
using OrganizationImportTool.Mapping;

namespace OrganizationImportTool.Tests
{
    public class MappingExtrasTests
    {
        private static MappingResult Sample()
        {
            var m = new MappingResult();
            m.Rules.Add(new TransformRule
            {
                WhenColumn = "Type", Op = RuleOp.Equals, WhenValue = "IMP",
                ThenField = "orgHeader.isConsignee", ThenValue = "true",
                Conditions = { new RuleCondition { Column = "Country", Op = RuleOp.Equals, Value = "AU" } },
                Actions = { new RuleAction { Field = "orgHeader.category", Value = "BUS" } }
            });
            m.Constants["orgHeader.isActive"] = "true";
            m.ValueMaps["orgAddressCollection[].countryCode.code"] =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AUS"] = "AU", ["*"] = "ZZ" };
            return m;
        }

        [Fact]
        public void Round_trips_through_json()
        {
            var json = MappingExtras.From(Sample(), "Master").ToJson();
            var back = MappingExtras.FromJson(json);

            Assert.Equal("Master", back.Name);
            Assert.Single(back.Rules);
            Assert.Equal(RuleOp.Equals, back.Rules[0].Op);
            Assert.Single(back.Rules[0].Conditions);
            Assert.Single(back.Rules[0].Actions);
            Assert.Equal("true", back.Constants["orgHeader.isActive"]);
            Assert.Equal("AU", back.ValueMaps["orgAddressCollection[].countryCode.code"]["AUS"]);
        }

        [Fact]
        public void Value_maps_keep_case_insensitivity_after_load()
        {
            var back = MappingExtras.FromJson(MappingExtras.From(Sample()).ToJson());
            // case-insensitive lookups must survive the JSON round-trip
            Assert.Equal("AU", back.ValueMaps["orgAddressCollection[].countryCode.code"]["aus"]);
        }

        [Fact]
        public void ApplyTo_merge_keeps_existing()
        {
            var target = new MappingResult();
            target.Constants["existing"] = "keep";
            target.ValueMaps["p"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X"] = "Y" };

            var extras = MappingExtras.From(Sample());
            extras.ValueMaps["p"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["A"] = "B" };
            extras.ApplyTo(target, replace: false);

            Assert.Equal("keep", target.Constants["existing"]);        // pre-existing constant kept
            Assert.Equal("true", target.Constants["orgHeader.isActive"]); // imported one added
            Assert.Equal("Y", target.ValueMaps["p"]["X"]);             // existing map entry kept
            Assert.Equal("B", target.ValueMaps["p"]["A"]);             // imported map entry merged in
            Assert.Single(target.Rules);
        }

        [Fact]
        public void ApplyTo_replace_clears_first()
        {
            var target = new MappingResult();
            target.Constants["existing"] = "keep";
            target.Rules.Add(new TransformRule { WhenColumn = "X", WhenValue = "1", ThenField = "f", ThenValue = "v" });

            MappingExtras.From(Sample()).ApplyTo(target, replace: true);

            Assert.False(target.Constants.ContainsKey("existing")); // cleared
            Assert.True(target.Constants.ContainsKey("orgHeader.isActive"));
            Assert.Single(target.Rules); // only the imported rule
        }

        [Fact]
        public void Imported_rules_are_cloned_not_shared()
        {
            var extras = MappingExtras.From(Sample());
            var t1 = new MappingResult();
            var t2 = new MappingResult();
            extras.ApplyTo(t1, replace: true);
            extras.ApplyTo(t2, replace: true);
            t1.Rules[0].ThenValue = "changed";
            Assert.NotEqual("changed", t2.Rules[0].ThenValue); // independent instances
        }
    }
}
