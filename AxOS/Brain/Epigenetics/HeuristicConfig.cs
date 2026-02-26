// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using AxOS.Core;
using AxOS.Brain;
using AxOS.Kernel;
using AxOS.Hardware;
using AxOS.Storage;
using AxOS.Diagnostics;

namespace AxOS.Brain
{
    public sealed class HeuristicConfig
    {
        public float System1Base = 0.90f;
        public float System1EntropyWeight = 0.10f;
        public float System1SparsityWeight = 0.05f;
        public float System1Min = 0.85f;
        public float System1Max = 0.98f;

        public float CriticBase = 0.85f;
        public float CriticEntropyWeight = 0.05f;
        public float CriticSkewnessWeight = 0.02f;
        public float CriticMin = 0.80f;
        public float CriticMax = 0.95f;

        public float DeepThinkCostBase = 0.80f;
        public float DeepThinkEntropyWeight = 0.90f;
        public float DeepThinkSparsityWeight = 0.50f;
        public float DeepThinkCostMin = 0.70f;
        public float DeepThinkCostMax = 2.30f;

        public float ConsolidationMinFitness = 0.95f;
        public float ConsolidationMaxNormalizedBurn = 0.20f;
        public int ConsolidationTopLimit = 64;

        // Metabolism is configured as percentages of the active budget.
        public float FatigueRemainingRatio = 0.28f;
        public float ZombieActivationRatio = 0.20f;
        public float ZombieCriticThreshold = 0.95f;

        public HeuristicConfig Copy()
        {
            return new HeuristicConfig
            {
                System1Base = System1Base,
                System1EntropyWeight = System1EntropyWeight,
                System1SparsityWeight = System1SparsityWeight,
                System1Min = System1Min,
                System1Max = System1Max,
                CriticBase = CriticBase,
                CriticEntropyWeight = CriticEntropyWeight,
                CriticSkewnessWeight = CriticSkewnessWeight,
                CriticMin = CriticMin,
                CriticMax = CriticMax,
                DeepThinkCostBase = DeepThinkCostBase,
                DeepThinkEntropyWeight = DeepThinkEntropyWeight,
                DeepThinkSparsityWeight = DeepThinkSparsityWeight,
                DeepThinkCostMin = DeepThinkCostMin,
                DeepThinkCostMax = DeepThinkCostMax,
                ConsolidationMinFitness = ConsolidationMinFitness,
                ConsolidationMaxNormalizedBurn = ConsolidationMaxNormalizedBurn,
                ConsolidationTopLimit = ConsolidationTopLimit,
                FatigueRemainingRatio = FatigueRemainingRatio,
                ZombieActivationRatio = ZombieActivationRatio,
                ZombieCriticThreshold = ZombieCriticThreshold
            };
        }
    }
}

