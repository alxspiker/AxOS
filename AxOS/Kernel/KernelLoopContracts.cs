// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using AxOS.Core;
using AxOS.Brain;
using AxOS.Kernel;
using AxOS.Hardware;
using AxOS.Storage;
using AxOS.Diagnostics;

namespace AxOS.Kernel
{
    public sealed class DataStream
    {
        public string DatasetType = "text";
        public string DatasetId = string.Empty;
        public string Payload = string.Empty;
        public int DimHint;
    }

    public sealed class SignalProfile
    {
        public int Length;
        public float Mean;
        public float StandardDeviation;
        public float Skewness;
        public float Sparsity;
        public float Entropy;
        public float UniqueRatio;
        public float Range;
        public float System1SimilarityThreshold;
        public float CriticAcceptanceThreshold;
        public float DeepThinkCostBias;
        public string Label = string.Empty;
    }

    public sealed class TensorOpCandidate
    {
        public Tensor Candidate = new Tensor();
        public float Fitness;
        public float Similarity;
        public float Cost;
        public string Strategy = string.Empty;
    }

    public sealed class IngestResult
    {
        public bool Success;
        public bool ReflexHit;
        public bool DeepThinkPath;
        public bool ZombieTriggered;
        public bool SleepTriggered;
        public bool DiscoveryTriggered;
        public int Iterations;
        public string Outcome = string.Empty;
        public string Error = string.Empty;
        public string SleepReason = string.Empty;
        public string CacheKey = string.Empty;
        public float Similarity;
        public float EnergyRemaining;
        public SignalProfile Profile = new SignalProfile();
    }
}

