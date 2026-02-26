// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using System;
using System.Collections.Generic;
using AxOS.Core;
using AxOS.Brain;

namespace AxOS.Kernel
{
    public sealed class Ruleset
    {
        public string ConstraintMode { get; set; } = "generic";
        public HeuristicConfig Heuristics { get; set; } = new HeuristicConfig();
        public Dictionary<string, Tensor> SymbolDefinitions { get; set; } = new Dictionary<string, Tensor>();
        public List<ReflexTrigger> ReflexTriggers { get; set; } = new List<ReflexTrigger>();
    }

    public sealed class ReflexTrigger
    {
        public string TargetSymbol { get; set; } = string.Empty;
        public float SimilarityThreshold { get; set; } = 0.85f;
        public string ActionIntent { get; set; } = string.Empty;
    }
}
