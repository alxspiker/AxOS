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
    public sealed class EpisodicMemory
    {
        public sealed class RecallResult
        {
            public bool Found;
            public Tensor Value = new Tensor();
            public double Similarity = -1.0;
            public long StoredStep = -1;
            public long AgeSteps = -1;
            public int Level;
            public int Span;
            public string Source = string.Empty;
        }

        private sealed class TraceBlock
        {
            public bool Valid;
            public Tensor Summary = new Tensor();
            public long StartStep = -1;
            public long EndStep = -1;
            public int Span;
        }

        private sealed class RecentTrace
        {
            public Tensor Value = new Tensor();
            public long Step = -1;
        }

        private readonly int _maxLevels;
        private readonly int _recentLimit;
        private readonly List<TraceBlock> _levels;
        private readonly Queue<RecentTrace> _recent;

        private int _dim;
        private long _step;
        private long _totalStored;

        public EpisodicMemory(int maxLevels = 32, int recentLimit = 256)
        {
            _maxLevels = Math.Max(1, maxLevels);
            _recentLimit = Math.Max(1, recentLimit);
            _levels = new List<TraceBlock>(_maxLevels);
            for (int i = 0; i < _maxLevels; i++)
            {
                _levels.Add(new TraceBlock());
            }
            _recent = new Queue<RecentTrace>(_recentLimit);
        }

        public bool IsEmpty => _totalStored == 0;
        public int Dimension => _dim;
        public long CurrentStep => _step;
        public long TotalStored => _totalStored;
        public int MaxLevels => _maxLevels;

        public int ActiveLevels
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _levels.Count; i++)
                {
                    if (_levels[i].Valid)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public bool Store(Tensor thought, out string error)
        {
            error = string.Empty;
            Tensor current = FlattenAndNormalize(thought);
            if (!ValidateDimension(current, out error))
            {
                return false;
            }

            if (_dim == 0)
            {
                _dim = current.Total;
            }

            _step++;
            _totalStored++;
            PushRecent(current, _step);

            TraceBlock carry = new TraceBlock
            {
                Valid = true,
                Summary = current,
                StartStep = _step,
                EndStep = _step,
                Span = 1
            };

            bool placed = false;
            for (int level = 0; level < _levels.Count; level++)
            {
                TraceBlock slot = _levels[level];
                if (!slot.Valid)
                {
                    _levels[level] = carry;
                    placed = true;
                    break;
                }

                carry = MergeBlocks(slot, carry);
                _levels[level] = new TraceBlock();
            }

            if (!placed)
            {
                TraceBlock top = _levels[_levels.Count - 1];
                if (!top.Valid)
                {
                    _levels[_levels.Count - 1] = carry;
                }
                else
                {
                    _levels[_levels.Count - 1] = MergeBlocks(top, carry);
                }
            }

            return true;
        }

        public RecallResult RecallSimilar(Tensor query)
        {
            RecallResult best = new RecallResult();
            if (IsEmpty)
            {
                return best;
            }

            Tensor q = FlattenAndNormalize(query);
            if (q.IsEmpty || q.Total != _dim)
            {
                return best;
            }

            double bestScore = -2.0;

            foreach (RecentTrace recent in _recent)
            {
                double score = StableCosine(q, recent.Value);
                if (score > bestScore)
                {
                    bestScore = score;
                    best.Found = true;
                    best.Value = recent.Value;
                    best.Similarity = score;
                    best.StoredStep = recent.Step;
                    best.AgeSteps = _step - recent.Step;
                    best.Level = 0;
                    best.Span = 1;
                    best.Source = "recent";
                }
            }

            for (int level = 0; level < _levels.Count; level++)
            {
                TraceBlock slot = _levels[level];
                if (!slot.Valid)
                {
                    continue;
                }

                double score = StableCosine(q, slot.Summary);
                if (score > bestScore)
                {
                    bestScore = score;
                    best.Found = true;
                    best.Value = slot.Summary;
                    best.Similarity = score;
                    best.StoredStep = slot.EndStep;
                    best.AgeSteps = _step - slot.EndStep;
                    best.Level = level;
                    best.Span = slot.Span;
                    best.Source = "logtrace";
                }
            }

            return best;
        }

        public RecallResult RecallStepsAgo(long stepsAgo)
        {
            RecallResult best = new RecallResult();
            if (IsEmpty)
            {
                return best;
            }

            long clamped = Math.Max(0, stepsAgo);
            long targetStep = Math.Max(1, _step - clamped);

            bool foundCandidate = false;
            long bestDist = long.MaxValue;

            foreach (RecentTrace recent in _recent)
            {
                long dist = DistanceSteps(recent.Step, targetStep);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    foundCandidate = true;
                    best.Found = true;
                    best.Value = recent.Value;
                    best.Similarity = 1.0;
                    best.StoredStep = recent.Step;
                    best.AgeSteps = _step - recent.Step;
                    best.Level = 0;
                    best.Span = 1;
                    best.Source = "recent";
                    if (dist == 0)
                    {
                        return best;
                    }
                }
            }

            for (int level = 0; level < _levels.Count; level++)
            {
                TraceBlock slot = _levels[level];
                if (!slot.Valid)
                {
                    continue;
                }

                long repStep;
                if (targetStep >= slot.StartStep && targetStep <= slot.EndStep)
                {
                    repStep = targetStep;
                }
                else
                {
                    repStep = slot.StartStep + (slot.EndStep - slot.StartStep) / 2;
                }

                long dist = DistanceSteps(repStep, targetStep);
                if (!foundCandidate || dist < bestDist ||
                    (dist == bestDist && slot.Span < best.Span))
                {
                    bestDist = dist;
                    foundCandidate = true;
                    best.Found = true;
                    best.Value = slot.Summary;
                    best.Similarity = 0.0;
                    best.StoredStep = repStep;
                    best.AgeSteps = _step - repStep;
                    best.Level = level;
                    best.Span = slot.Span;
                    best.Source = "logtrace";
                }
            }

            return best;
        }

        public void Clear()
        {
            _dim = 0;
            _step = 0;
            _totalStored = 0;
            _recent.Clear();
            for (int i = 0; i < _levels.Count; i++)
            {
                _levels[i] = new TraceBlock();
            }
        }

        private Tensor FlattenAndNormalize(Tensor input)
        {
            Tensor outVec = input.Copy();
            if (outVec.IsEmpty)
            {
                return outVec;
            }
            outVec = outVec.Reshape(new Shape(outVec.Total));
            return TensorOps.NormalizeL2(outVec);
        }

        private static double StableCosine(Tensor a, Tensor b)
        {
            if (a.Total != b.Total || a.IsEmpty)
            {
                return -1.0;
            }
            return TensorOps.CosineSimilarity(a, b);
        }

        private static Tensor WeightedMerge(TraceBlock older, TraceBlock newer)
        {
            Tensor outVec = older.Summary.Copy();
            double olderWeight = Math.Max(1, older.Span);
            double newerWeight = Math.Max(1, newer.Span);
            for (int i = 0; i < outVec.Data.Length; i++)
            {
                double merged = older.Summary.Data[i] * olderWeight + newer.Summary.Data[i] * newerWeight;
                outVec.Data[i] = (float)merged;
            }
            return TensorOps.NormalizeL2(outVec);
        }

        private static long DistanceSteps(long a, long b)
        {
            return Math.Abs(a - b);
        }

        private bool ValidateDimension(Tensor tensor, out string error)
        {
            error = string.Empty;
            if (tensor.IsEmpty)
            {
                error = "empty tensor";
                return false;
            }
            if (_dim != 0 && tensor.Total != _dim)
            {
                error = "dimension mismatch";
                return false;
            }
            return true;
        }

        private TraceBlock MergeBlocks(TraceBlock older, TraceBlock newer)
        {
            return new TraceBlock
            {
                Valid = true,
                Summary = WeightedMerge(older, newer),
                StartStep = Math.Min(older.StartStep, newer.StartStep),
                EndStep = Math.Max(older.EndStep, newer.EndStep),
                Span = older.Span + newer.Span
            };
        }

        private void PushRecent(Tensor value, long step)
        {
            _recent.Enqueue(new RecentTrace { Value = value, Step = step });
            while (_recent.Count > _recentLimit)
            {
                _recent.Dequeue();
            }
        }
    }
}

