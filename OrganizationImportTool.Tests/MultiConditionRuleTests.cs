using System;
using System.Collections.Generic;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Mapping;

namespace OrganizationImportTool.Tests
{
    public class MultiConditionRuleTests
    {
        private static SourceRow Row(params (string col, string val)[] cells)
        {
            var r = new SourceRow { RowNumber = 1 };
            foreach (var (col, val) in cells) r[col] = val;
            return r;
        }

        private static Dictionary<string, string> Apply(TransformRule rule, SourceRow row)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            RuleEngine.Apply(new[] { rule }, row, values);
            return values;
        }

        [Fact]
        public void Scalar_single_condition_rule_still_works()
        {
            var rule = new TransformRule
            {
                WhenColumn = "Type", Op = RuleOp.Contains, WhenValue = "IMP",
                ThenField = "orgHeader.isConsignee", ThenValue = "true"
            };
            var values = Apply(rule, Row(("Type", "IMPORTER")));
            Assert.Equal("true", values["orgHeader.isConsignee"]);
        }

        [Fact]
        public void And_requires_all_conditions()
        {
            var rule = new TransformRule
            {
                Logic = RuleLogic.And,
                WhenColumn = "Type", Op = RuleOp.Equals, WhenValue = "IMPORTER",
                Conditions = { new RuleCondition { Column = "Country", Op = RuleOp.Equals, Value = "AU" } },
                ThenField = "orgHeader.code", ThenValue = "OK"
            };
            Assert.Equal("OK", Apply(rule, Row(("Type", "IMPORTER"), ("Country", "AU")))["orgHeader.code"]);
            Assert.False(Apply(rule, Row(("Type", "IMPORTER"), ("Country", "US"))).ContainsKey("orgHeader.code"));
        }

        [Fact]
        public void Or_requires_any_condition()
        {
            var rule = new TransformRule
            {
                Logic = RuleLogic.Or,
                WhenColumn = "Type", Op = RuleOp.Equals, WhenValue = "IMPORTER",
                Conditions = { new RuleCondition { Column = "Type", Op = RuleOp.Equals, Value = "EXPORTER" } },
                ThenField = "orgHeader.isActive", ThenValue = "true"
            };
            Assert.Equal("true", Apply(rule, Row(("Type", "EXPORTER")))["orgHeader.isActive"]);
            Assert.Equal("true", Apply(rule, Row(("Type", "IMPORTER")))["orgHeader.isActive"]);
            Assert.False(Apply(rule, Row(("Type", "OTHER"))).ContainsKey("orgHeader.isActive"));
        }

        [Fact]
        public void Multiple_actions_all_apply()
        {
            var rule = new TransformRule
            {
                WhenColumn = "Type", Op = RuleOp.Equals, WhenValue = "IMPORTER",
                ThenField = "orgHeader.isConsignee", ThenValue = "true",
                Actions =
                {
                    new RuleAction { Field = "orgHeader.category", Value = "BUS" },
                    new RuleAction { Field = "orgHeader.note", Value = "auto" }
                }
            };
            var values = Apply(rule, Row(("Type", "IMPORTER")));
            Assert.Equal("true", values["orgHeader.isConsignee"]);
            Assert.Equal("BUS", values["orgHeader.category"]);
            Assert.Equal("auto", values["orgHeader.note"]);
        }

        [Fact]
        public void Disabled_rule_does_not_fire()
        {
            var rule = new TransformRule
            {
                Enabled = false,
                WhenColumn = "Type", Op = RuleOp.Equals, WhenValue = "IMPORTER",
                ThenField = "orgHeader.code", ThenValue = "NO"
            };
            Assert.Empty(Apply(rule, Row(("Type", "IMPORTER"))));
        }

        [Fact]
        public void Incomplete_rule_is_not_complete()
        {
            Assert.False(new TransformRule { WhenColumn = "Type", Op = RuleOp.Equals, WhenValue = "X" }.IsComplete); // no action
            Assert.False(new TransformRule { ThenField = "f", ThenValue = "v" }.IsComplete);                          // no condition
            Assert.True(new TransformRule { WhenColumn = "Type", WhenValue = "X", ThenField = "f", ThenValue = "v" }.IsComplete);
        }

        [Fact]
        public void Describe_joins_conditions_and_actions()
        {
            var rule = new TransformRule
            {
                Logic = RuleLogic.And,
                WhenColumn = "Type", Op = RuleOp.Equals, WhenValue = "IMP",
                Conditions = { new RuleCondition { Column = "Country", Op = RuleOp.Contains, Value = "AU" } },
                ThenField = "f1", ThenValue = "v1",
                Actions = { new RuleAction { Field = "f2", Value = "v2" } }
            };
            string d = rule.Describe();
            Assert.Contains("\"Type\" is \"IMP\" AND \"Country\" contains \"AU\"", d);
            Assert.Contains("f1 = \"v1\", f2 = \"v2\"", d);
        }
    }
}
