// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using System;
using AxOS.Core;
using AxOS.Brain;
using AxOS.Kernel;
using AxOS.Hardware;
using AxOS.Storage;
using AxOS.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AxOS.Brain
{
    public sealed class CognitiveAdapter
    {
        private readonly HdcSystem _hdc;
        private readonly HeuristicConfig _config;

        public CognitiveAdapter(HdcSystem hdc, HeuristicConfig config = null)
        {
            _hdc = hdc ?? throw new ArgumentNullException(nameof(hdc));
            _config = (config ?? new HeuristicConfig()).Copy();
        }

        public SignalProfile AnalyzeHeuristics(DataStream rawInput)
        {
            SignalProfile profile = new SignalProfile();
            List<float> values = GetProfileValues(rawInput);
            if (values.Count == 0)
            {
                values.Add(0.0f);
            }

            profile.Length = values.Count;
            float min = values[0];
            float max = values[0];
            double sum = 0.0;
            int zeros = 0;

            Dictionary<int, int> counts = new Dictionary<int, int>();
            for (int i = 0; i < values.Count; i++)
            {
                float v = values[i];
                if (v < min)
                {
                    min = v;
                }
                if (v > max)
                {
                    max = v;
                }
                if (Math.Abs(v) < 1e-6f)
                {
                    zeros++;
                }

                sum += v;
                int bucket = v >= 0.0f
                    ? (int)(v + 0.5f)
                    : (int)(v - 0.5f);
                if (!counts.ContainsKey(bucket))
                {
                    counts[bucket] = 0;
                }
                counts[bucket]++;
            }

            double mean = sum / values.Count;
            double variance = 0.0;
            double skew = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                double diff = values[i] - mean;
                variance += diff * diff;
            }
            variance /= values.Count;
            double std = Math.Sqrt(variance);
            if (std > 1e-9)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    double diff = values[i] - mean;
                    skew += diff * diff * diff;
                }
                skew /= values.Count;
                skew /= (std * std * std);
            }

            double entropy = 0.0;
            foreach (KeyValuePair<int, int> kv in counts)
            {
                double p = (double)kv.Value / values.Count;
                if (p > 0.0)
                {
                    entropy -= p * Math.Log(p, 2.0);
                }
            }
            double maxEntropy = Math.Log(Math.Max(1, counts.Count), 2.0);
            double entropyNorm = maxEntropy > 0.0 ? entropy / maxEntropy : 0.0;

            profile.Mean = (float)mean;
            profile.StandardDeviation = (float)std;
            profile.Skewness = (float)skew;
            profile.Sparsity = (float)zeros / Math.Max(1, values.Count);
            profile.Entropy = (float)entropyNorm;
            profile.UniqueRatio = (float)counts.Count / Math.Max(1, values.Count);
            profile.Range = max - min;

            profile.System1SimilarityThreshold = Clamp(
                _config.System1Base - (profile.Entropy * _config.System1EntropyWeight) + (profile.Sparsity * _config.System1SparsityWeight),
                _config.System1Min,
                _config.System1Max);

            profile.CriticAcceptanceThreshold = Clamp(
                _config.CriticBase + (profile.Entropy * _config.CriticEntropyWeight) + (Math.Abs(profile.Skewness) * _config.CriticSkewnessWeight),
                _config.CriticMin,
                _config.CriticMax);

            profile.DeepThinkCostBias = Clamp(
                _config.DeepThinkCostBase + (profile.Entropy * _config.DeepThinkEntropyWeight) + (profile.Sparsity * _config.DeepThinkSparsityWeight),
                _config.DeepThinkCostMin,
                _config.DeepThinkCostMax);

            if (profile.Sparsity > 0.75f)
            {
                profile.Label = "sparse";
            }
            else if (profile.Entropy > 0.70f)
            {
                profile.Label = "high_entropy";
            }
            else if (Math.Abs(profile.Skewness) > 1.0f)
            {
                profile.Label = "skewed";
            }
            else
            {
                profile.Label = "balanced";
            }

            return profile;
        }

        public bool L2NormalizeAndFlatten(DataStream rawInput, SignalProfile profile, out Tensor output, out string error)
        {
            output = new Tensor();
            error = string.Empty;

            int dim = ResolveDim(rawInput.DimHint);
            string mode = (rawInput.DatasetType ?? string.Empty).Trim().ToLowerInvariant();

            if (mode == "tensor" || mode == "numeric")
            {
                List<float> values = ParseNumericPayload(rawInput.Payload);
                if (values.Count == 0)
                {
                    error = "empty_payload";
                    return false;
                }

                output = FoldToDim(values, dim);
                return true;
            }
            else
            {
                List<string> tokens = TokenizeText(rawInput.Payload);
                if (tokens.Count == 0)
                {
                    tokens.Add("empty");
                }

                List<int> positions = new List<int>(tokens.Count);
                for (int i = 0; i < tokens.Count; i++)
                {
                    positions.Add(i % Math.Max(1, dim));
                }

                if (!_hdc.Sequence.EncodeTokens(_hdc.Symbols, tokens, positions, dim, out output, out error, out string errorToken))
                {
                    if (!string.IsNullOrEmpty(errorToken))
                    {
                        error = error + " (" + errorToken + ")";
                    }
                    return false;
                }
                return true;
            }
        }

        public TensorOpCandidate RouteDynamicConnectome(
            Tensor targetTensor,
            SignalProfile profile,
            IReadOnlyList<WorkingMemoryCache.CacheEntry> memoryCandidates,
            int iteration)
        {
            TensorOpCandidate candidate = new TensorOpCandidate();
            Tensor target = TensorOps.NormalizeL2(targetTensor.Flatten());
            if (target.IsEmpty)
            {
                return candidate;
            }

            WorkingMemoryCache.CacheEntry bestMemory = null;
            float bestMemorySimilarity = -1.0f;
            if (memoryCandidates != null)
            {
                for (int i = 0; i < memoryCandidates.Count; i++)
                {
                    WorkingMemoryCache.CacheEntry entry = memoryCandidates[i];
                    if (entry.Value == null || entry.Value.IsEmpty || entry.Value.Total != target.Total)
                    {
                        continue;
                    }

                    float similarity = (float)TensorOps.CosineSimilarity(target, entry.Value);
                    if (similarity > bestMemorySimilarity)
                    {
                        bestMemorySimilarity = similarity;
                        bestMemory = entry;
                    }
                }
            }

            if (bestMemory != null)
            {
                Tensor blended = new Tensor(new Shape(target.Total), 0.0f);
                float memoryWeight = Clamp(0.30f + (1.0f - profile.Entropy) * 0.50f, 0.20f, 0.80f);
                float targetWeight = 1.0f - memoryWeight;
                for (int d = 0; d < target.Total; d++)
                {
                    blended.Data[d] = (target.Data[d] * targetWeight) + (bestMemory.Value.Data[d] * memoryWeight);
                }
                blended = TensorOps.NormalizeL2(blended);

                candidate.Candidate = blended;
                candidate.Similarity = bestMemorySimilarity;
                candidate.Fitness = Clamp(
                    ((bestMemorySimilarity + 1.0f) * 0.5f) + ((1.0f - profile.Entropy) * 0.15f),
                    0.0f,
                    1.0f);
                candidate.Strategy = "cache_bundle";
            }
            else
            {
                int shift = (iteration % Math.Max(1, target.Total - 1)) + 1;
                Tensor permuted = TensorOps.Permute(target, shift);
                Tensor mixed = TensorOps.Bundle(target, permuted, true);
                float similarity = (float)TensorOps.CosineSimilarity(target, mixed);

                candidate.Candidate = mixed;
                candidate.Similarity = similarity;
                candidate.Fitness = Clamp(
                    ((similarity + 1.0f) * 0.5f) - (profile.Entropy * 0.22f) + ((1.0f - profile.Sparsity) * 0.08f),
                    0.0f,
                    1.0f);
                candidate.Strategy = "self_permute";
            }

            // If entropy is extremely high and fitness is low, flag for discovery induction
            if (profile.Entropy > 0.85f && candidate.Similarity < 0.20f && iteration > 32)
            {
                candidate.Strategy = "discovery_induction";
                candidate.Fitness = profile.CriticAcceptanceThreshold + 0.05f; // Safe override
            }

            candidate.Cost = CalculateThermodynamicCost(candidate, profile);
            return candidate;
        }

        public Tensor DeduceGeometricGap(Tensor currentState, Tensor requiredNextState)
        {
            if (currentState == null || requiredNextState == null || currentState.IsEmpty || requiredNextState.IsEmpty)
            {
                return new Tensor();
            }

            // High-dimensional differential math: Target - Source
            Tensor spatialGap = TensorOps.Subtract(requiredNextState, currentState);
            return TensorOps.NormalizeL2(spatialGap);
        }

        public float CalculateThermodynamicCost(TensorOpCandidate candidate, SignalProfile profile)
        {
            float baseCost = 4.0f + (12.0f * profile.DeepThinkCostBias);
            float criticPenalty = (1.0f - Clamp(candidate.Fitness, 0.0f, 1.0f)) * 8.0f;
            float strategyBias = string.CompareOrdinal(candidate.Strategy, "cache_bundle") == 0 ? 0.85f : 1.0f;
            return Math.Max(0.5f, (baseCost + criticPenalty) * strategyBias);
        }

        public bool PassesCriticThreshold(TensorOpCandidate candidate, SignalProfile profile, SystemMetabolism metabolism)
        {
            float baseThreshold = Clamp(profile.CriticAcceptanceThreshold, 0.0f, 1.0f);
            float activeThreshold = metabolism != null && metabolism.ZombieModeActive
                ? metabolism.ZombieThreshold
                : baseThreshold;
            return candidate.Fitness >= activeThreshold;
        }

        public void ConsolidateMemory(WorkingMemoryCache workingMemory)
        {
            if (workingMemory == null)
            {
                return;
            }

            float minFitness = Clamp(_config.ConsolidationMinFitness, 0.0f, 1.0f);
            float maxNormalizedBurn = Clamp(_config.ConsolidationMaxNormalizedBurn, 0.0f, 1.0f);
            int topLimit = _config.ConsolidationTopLimit <= 0 ? 64 : _config.ConsolidationTopLimit;
            List<WorkingMemoryCache.CacheEntry> top = workingMemory.SnapshotByPriority(topLimit);
            for (int i = 0; i < top.Count; i++)
            {
                WorkingMemoryCache.CacheEntry entry = top[i];
                if (entry.Value == null || entry.Value.IsEmpty)
                {
                    continue;
                }

                 if (entry.Fitness < minFitness)
                {
                    continue;
                }

                float burn = entry.BurnSamples > 0 ? entry.AverageMetabolicBurn : 1.0f;
                if (burn > maxNormalizedBurn)
                {
                    continue;
                }

                Dictionary<string, string> meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "label", string.IsNullOrWhiteSpace(entry.DatasetType) ? "dataset" : entry.DatasetType.ToLowerInvariant() },
                    { "dataset_id", entry.DatasetId ?? string.Empty },
                    { "stability", entry.Fitness.ToString("0.000", CultureInfo.InvariantCulture) },
                    { "source", "sleep_consolidation" },
                    { "cache_hits", entry.Hits.ToString(CultureInfo.InvariantCulture) },
                    { "metabolic_burn", burn.ToString("0.000", CultureInfo.InvariantCulture) }
                };

                string reflexId = "sys1_" + SanitizeReflexId(entry.Key);
                _hdc.Reflexes.Promote(reflexId, entry.Value, meta, true, out _);
                _hdc.Remember(entry.Value, out _);
            }
        }

        public string NormalizeType(string rawType)
        {
            return (rawType ?? string.Empty).Trim().ToLowerInvariant();
        }

        private int ResolveDim(int requested)
        {
            if (requested > 0)
            {
                return requested;
            }
            if (_hdc.Symbols.SymbolDim > 0)
            {
                return _hdc.Symbols.SymbolDim;
            }
            if (_hdc.Memory.Dimension > 0)
            {
                return _hdc.Memory.Dimension;
            }
            return TensorOps.DefaultHypervectorDim;
        }

        private static List<float> GetProfileValues(DataStream rawInput)
        {
            List<float> values = new List<float>();
            string mode = (rawInput.DatasetType ?? string.Empty).Trim().ToLowerInvariant();
            
            if (mode == "tensor" || mode == "numeric")
            {
                values = ParseNumericPayload(rawInput.Payload);
                if (values.Count > 0)
                {
                    return values;
                }
            }

            string payload = rawInput.Payload ?? string.Empty;
            for (int i = 0; i < payload.Length; i++)
            {
                values.Add(payload[i]);
            }
            return values;
        }

        private static Tensor FoldToDim(List<float> values, int dim)
        {
            int safeDim = Math.Max(1, dim);
            Tensor tensor = new Tensor(new Shape(safeDim), 0.0f);
            
            for (int i = 0; i < values.Count; i++)
            {
                // Strict 32-bit integer math (bare-metal safe) to avoid IL2CPU Math.Abs(long) hangs
                int p1 = i * 1315423;
                int p2 = i * 2654435;
                int p3 = i * 805459;

                int s1 = (p1 % safeDim + safeDim) % safeDim;
                int s2 = (p2 % safeDim + safeDim) % safeDim;
                int s3 = (p3 % safeDim + safeDim) % safeDim;

                tensor.Data[s1] += values[i];
                tensor.Data[s2] -= values[i] * 0.5f;
                tensor.Data[s3] += values[i] * 0.5f;
            }
            
            return TensorOps.NormalizeL2(tensor);
        }

        private static List<float> ParseNumericPayload(string payload)
        {
            List<float> values = new List<float>();
            if (string.IsNullOrWhiteSpace(payload))
            {
                return values;
            }

            StringBuilder token = new StringBuilder();
            for (int i = 0; i < payload.Length; i++)
            {
                char c = payload[i];
                if (char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                {
                    token.Append(c);
                }
                else if (token.Length > 0)
                {
                    values.Add(ManualParseFloat(token.ToString()));
                    token.Clear();
                }
            }

            if (token.Length > 0)
            {
                values.Add(ManualParseFloat(token.ToString()));
            }

            return values;
        }

        private static float ManualParseFloat(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0.0f;
            float sign = 1.0f;
            int start = 0;
            if (text[0] == '-')
            {
                sign = -1.0f;
                start = 1;
            }
            else if (text[0] == '+')
            {
                start = 1;
            }

            float result = 0.0f;
            bool fractional = false;
            float fractionDivisor = 1.0f;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '.')
                {
                    fractional = true;
                }
                else if (c >= '0' && c <= '9')
                {
                    int val = c - '0';
                    if (!fractional)
                    {
                        result = result * 10 + val;
                    }
                    else
                    {
                        fractionDivisor *= 10.0f;
                        result += val / fractionDivisor;
                    }
                }
                else if (c == 'e' || c == 'E')
                {
                    // Not supporting exponents fully for simplicity in bare-metal, it's enough for "0.000" formats
                    break;
                }
            }

            return sign * result;
        }

        private static List<string> TokenizeText(string text)
        {
            List<string> tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return tokens;
            }

            StringBuilder sb = new StringBuilder();
            string lower = text.ToLowerInvariant();
            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
            }

            if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
            }
            return tokens;
        }



        private static string SanitizeReflexId(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "reflex";
            }

            StringBuilder sb = new StringBuilder(key.Length);
            for (int i = 0; i < key.Length; i++)
            {
                char c = char.ToLowerInvariant(key[i]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }

            return sb.ToString();
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }
}

