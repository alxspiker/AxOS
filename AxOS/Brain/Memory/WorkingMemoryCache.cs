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

namespace AxOS.Brain
{
    public sealed class WorkingMemoryCache
    {
        public sealed class CacheEntry
        {
            public string Key = string.Empty;
            public string DatasetType = string.Empty;
            public string DatasetId = string.Empty;
            public Tensor Value = new Tensor();
            public float Fitness;
            public float DecayScore = 1.0f;
            public float LastMetabolicBurn = -1.0f;
            public float AverageMetabolicBurn = -1.0f;
            public int BurnSamples;
            public int Hits;
            public long LastTouch;
            public bool IsAnomaly;
            public Tensor DeducedConstraint;
        }

        private readonly int _capacity;
        private readonly Dictionary<string, CacheEntry> _entries;
        private readonly List<string> _lruOrder;
        private long _touchClock;

        public WorkingMemoryCache(int capacity = 128)
        {
            _capacity = Math.Max(8, capacity);
            _entries = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
            _lruOrder = new List<string>(_capacity);
        }

        public int Count => _entries.Count;
        public int Capacity => _capacity;

        public void PromoteToCache(
            string keyRaw,
            Tensor vector,
            float fitness,
            string datasetType,
            string datasetId,
            float normalizedMetabolicBurn = -1.0f)
        {
            string key = (keyRaw ?? string.Empty).Trim();
            if (key.Length == 0 || vector == null || vector.IsEmpty)
            {
                return;
            }

            Tensor normalized = TensorOps.NormalizeL2(vector.Flatten());
            if (normalized.IsEmpty)
            {
                return;
            }

            _touchClock++;

            if (_entries.TryGetValue(key, out CacheEntry existing))
            {
                existing.Value = normalized;
                existing.Fitness = Clamp01(fitness);
                existing.DatasetType = datasetType ?? string.Empty;
                existing.DatasetId = datasetId ?? string.Empty;
                UpdateBurn(existing, normalizedMetabolicBurn);
                existing.DecayScore = Math.Min(1.0f, existing.DecayScore + 0.05f);
                existing.LastTouch = _touchClock;
                TouchLru(key);
                return;
            }

            if (_entries.Count >= _capacity && _lruOrder.Count > 0)
            {
                string evict = _lruOrder[0];
                _lruOrder.RemoveAt(0);
                _entries.Remove(evict);
            }

            CacheEntry entry = new CacheEntry
            {
                Key = key,
                DatasetType = datasetType ?? string.Empty,
                DatasetId = datasetId ?? string.Empty,
                Value = normalized,
                Fitness = Clamp01(fitness),
                DecayScore = 1.0f,
                LastMetabolicBurn = -1.0f,
                AverageMetabolicBurn = -1.0f,
                BurnSamples = 0,
                Hits = 0,
                LastTouch = _touchClock
            };
            UpdateBurn(entry, normalizedMetabolicBurn);

            _entries[key] = entry;
            _lruOrder.Add(key);
        }

        public void FlagAnomaly(string keyRaw, Tensor deducedConstraint)
        {
            string key = (keyRaw ?? string.Empty).Trim();
            if (_entries.TryGetValue(key, out CacheEntry entry))
            {
                entry.IsAnomaly = true;
                entry.DeducedConstraint = deducedConstraint?.Copy();
            }
        }

        public List<CacheEntry> GetAnomalies()
        {
            List<CacheEntry> anomalies = new List<CacheEntry>();
            foreach (var entry in _entries.Values)
            {
                if (entry.IsAnomaly)
                {
                    anomalies.Add(entry);
                }
            }
            return anomalies;
        }

        public void ClearAnomalies()
        {
            foreach (var entry in _entries.Values)
            {
                entry.IsAnomaly = false;
                entry.DeducedConstraint = null;
            }
        }

        public bool CosineSimilarityHit(Tensor query, float threshold, out CacheEntry hit, out float similarity)
        {
            hit = null;
            similarity = -1.0f;
            if (query == null || query.IsEmpty || _entries.Count == 0)
            {
                return false;
            }

            Tensor normalizedQuery = TensorOps.NormalizeL2(query.Flatten());
            if (normalizedQuery.IsEmpty)
            {
                return false;
            }

            float bestScore = -1.0f;
            CacheEntry bestEntry = null;

            foreach (KeyValuePair<string, CacheEntry> kv in _entries)
            {
                CacheEntry entry = kv.Value;
                if (entry.Value.Total != normalizedQuery.Total)
                {
                    continue;
                }

                float score = (float)TensorOps.CosineSimilarity(normalizedQuery, entry.Value);
                score *= entry.DecayScore;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEntry = entry;
                }
            }

            if (bestEntry == null)
            {
                return false;
            }

            similarity = bestScore;
            float safeThreshold = threshold < -1.0f ? -1.0f : (threshold > 1.0f ? 1.0f : threshold);
            if (bestScore < safeThreshold)
            {
                return false;
            }

            bestEntry.Hits++;
            bestEntry.DecayScore = Math.Min(1.0f, bestEntry.DecayScore + 0.02f);
            _touchClock++;
            bestEntry.LastTouch = _touchClock;
            TouchLru(bestEntry.Key);
            hit = bestEntry;
            return true;
        }

        public void ApplyTimeDecay(float factor = 0.97f, float floor = 0.35f)
        {
            float safeFactor = factor <= 0.0f ? 0.97f : (factor > 1.0f ? 1.0f : factor);
            float safeFloor = floor < 0.0f ? 0.0f : (floor > 1.0f ? 1.0f : floor);
            foreach (KeyValuePair<string, CacheEntry> kv in _entries)
            {
                CacheEntry entry = kv.Value;
                entry.DecayScore = Math.Max(safeFloor, entry.DecayScore * safeFactor);
            }
        }

        public List<CacheEntry> SnapshotByPriority(int limit = 0)
        {
            List<CacheEntry> snapshot = new List<CacheEntry>(_entries.Count);
            foreach (KeyValuePair<string, CacheEntry> kv in _entries)
            {
                CacheEntry entry = kv.Value;
                snapshot.Add(new CacheEntry
                {
                    Key = entry.Key,
                    DatasetType = entry.DatasetType,
                    DatasetId = entry.DatasetId,
                    Value = entry.Value.Copy(),
                    Fitness = entry.Fitness,
                    DecayScore = entry.DecayScore,
                    LastMetabolicBurn = entry.LastMetabolicBurn,
                    AverageMetabolicBurn = entry.AverageMetabolicBurn,
                    BurnSamples = entry.BurnSamples,
                    Hits = entry.Hits,
                    LastTouch = entry.LastTouch
                });
            }

            SortEntriesByPriority(snapshot);
            if (limit > 0 && snapshot.Count > limit)
            {
                snapshot.RemoveRange(limit, snapshot.Count - limit);
            }
            return snapshot;
        }

        public void Clear()
        {
            _entries.Clear();
            _lruOrder.Clear();
            _touchClock = 0;
        }

        private void TouchLru(string key)
        {
            for (int i = 0; i < _lruOrder.Count; i++)
            {
                if (string.CompareOrdinal(_lruOrder[i], key) == 0)
                {
                    _lruOrder.RemoveAt(i);
                    break;
                }
            }
            _lruOrder.Add(key);
        }

        private static float Clamp01(float value)
        {
            if (value < 0.0f)
            {
                return 0.0f;
            }
            if (value > 1.0f)
            {
                return 1.0f;
            }
            return value;
        }

        private static void SortEntriesByPriority(List<CacheEntry> entries)
        {
            for (int i = 1; i < entries.Count; i++)
            {
                CacheEntry key = entries[i];
                int j = i - 1;
                while (j >= 0 && ComparePriority(entries[j], key) > 0)
                {
                    entries[j + 1] = entries[j];
                    j--;
                }
                entries[j + 1] = key;
            }
        }

        private static int ComparePriority(CacheEntry left, CacheEntry right)
        {
            float leftEfficiency = left.BurnSamples > 0
                ? Clamp01(1.0f - left.AverageMetabolicBurn)
                : 0.5f;
            float rightEfficiency = right.BurnSamples > 0
                ? Clamp01(1.0f - right.AverageMetabolicBurn)
                : 0.5f;

            float leftScore = (left.Fitness * left.DecayScore * (0.6f + (0.4f * leftEfficiency))) + (left.Hits * 0.02f);
            float rightScore = (right.Fitness * right.DecayScore * (0.6f + (0.4f * rightEfficiency))) + (right.Hits * 0.02f);
            int scoreCmp = rightScore.CompareTo(leftScore);
            if (scoreCmp != 0)
            {
                return scoreCmp;
            }
            return right.LastTouch.CompareTo(left.LastTouch);
        }

        private static void UpdateBurn(CacheEntry entry, float normalizedMetabolicBurn)
        {
            if (entry == null || normalizedMetabolicBurn < 0.0f)
            {
                return;
            }

            float burn = Clamp01(normalizedMetabolicBurn);
            entry.LastMetabolicBurn = burn;
            if (entry.BurnSamples == 0)
            {
                entry.AverageMetabolicBurn = burn;
                entry.BurnSamples = 1;
                return;
            }

            float total = (entry.AverageMetabolicBurn * entry.BurnSamples) + burn;
            entry.BurnSamples++;
            entry.AverageMetabolicBurn = total / entry.BurnSamples;
        }
    }
}

