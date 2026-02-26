// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using System;
using System.Collections.Generic;
using System.Globalization;
using AxOS.Core;
using AxOS.Brain;

namespace AxOS.Kernel
{
    public static class RulesetParser
    {
        public static bool Parse(string payload, out Ruleset ruleset, out string error)
        {
            ruleset = new Ruleset();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(payload))
            {
                error = "empty_payload";
                return false;
            }

            string[] lines = payload.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string currentSection = string.Empty;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                {
                    continue;
                }

                if (line.EndsWith(":"))
                {
                    currentSection = line.Substring(0, line.Length - 1).Trim().ToLowerInvariant();
                    continue;
                }

                if (currentSection == "symbols")
                {
                    ParseSymbolLine(line, ruleset);
                }
                else if (currentSection == "reflex_triggers")
                {
                    ParseReflexLine(line, ruleset);
                }
                else
                {
                    ParseRootProperty(line, ruleset);
                }
            }

            return true;
        }

        private static void ParseRootProperty(string line, Ruleset ruleset)
        {
            int colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
            {
                return;
            }

            string key = line.Substring(0, colonIndex).Trim().ToLowerInvariant();
            string value = line.Substring(colonIndex + 1).Trim();

            if (key == "constraint_mode")
            {
                ruleset.ConstraintMode = value;
            }
            else if (key == "entropy_tolerance")
            {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float e))
                {
                    ruleset.Heuristics.CriticEntropyWeight = e;
                }
            }
        }

        private static void ParseSymbolLine(string line, Ruleset ruleset)
        {
            int eqIndex = line.IndexOf('=');
            if (eqIndex <= 0)
            {
                return;
            }

            string symName = line.Substring(0, eqIndex).Trim();
            string vecData = line.Substring(eqIndex + 1).Trim();
            string[] parts = vecData.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            
            Tensor t = new Tensor(new Shape(parts.Length), 0.0f);
            for (int j = 0; j < parts.Length; j++)
            {
                if (float.TryParse(parts[j].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                {
                    t.Data[j] = val;
                }
            }

            if (!t.IsEmpty && t.Total > 0)
            {
                ruleset.SymbolDefinitions[symName] = TensorOps.NormalizeL2(t);
            }
        }

        private static void ParseReflexLine(string line, Ruleset ruleset)
        {
            int arrow = line.IndexOf("->");
            if (arrow <= 0)
            {
                return;
            }

            string condition = line.Substring(0, arrow).Trim();
            string intent = line.Substring(arrow + 2).Trim();

            float thresh = 0.85f;
            int gt = condition.IndexOf('>');
            if (gt > 0)
            {
                string threshStr = condition.Substring(gt + 1).Trim();
                int paren = threshStr.IndexOf(')');
                if (paren >= 0)
                {
                    threshStr = threshStr.Substring(0, paren).Trim();
                }
                float.TryParse(threshStr, NumberStyles.Float, CultureInfo.InvariantCulture, out thresh);
            }

            string symbol = string.Empty;
            int comma = condition.IndexOf(',');
            int rp = condition.IndexOf(')');
            if (comma > 0 && rp > comma)
            {
                symbol = condition.Substring(comma + 1, rp - comma - 1).Trim();
            }

            if (!string.IsNullOrEmpty(symbol) && !string.IsNullOrEmpty(intent))
            {
                ruleset.ReflexTriggers.Add(new ReflexTrigger
                {
                    SimilarityThreshold = thresh,
                    TargetSymbol = symbol,
                    ActionIntent = intent
                });
            }
        }
    }
}
