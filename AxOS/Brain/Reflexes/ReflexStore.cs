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
    public sealed class ReflexStore
    {
        public sealed class ReflexEntry
        {
            public Tensor Vector = new Tensor();
            public Dictionary<string, string> Meta = new Dictionary<string, string>();
            public uint SymbolId = uint.MaxValue;
        }

        public sealed class QueryResult
        {
            public string Scope = "label";
            public int Dim;
            public List<string> ReflexIds = new List<string>();
            public List<Tensor> ReflexVectors = new List<Tensor>();
            public List<float> Stabilities = new List<float>();
            public List<Dictionary<string, string>> Metas = new List<Dictionary<string, string>>();
        }

        public sealed class ReflexStats
        {
            public int Count;
            public long ApproxBytes;
            public long ApproxVectorBytes;
            public long ApproxSymbolIdBytes;
            public int Dim;
        }

        private readonly Dictionary<string, ReflexEntry> _reflexes = new Dictionary<string, ReflexEntry>();
        private readonly Dictionary<string, List<string>> _indexByTarget = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _indexByLabel = new Dictionary<string, List<string>>();
        private readonly List<string> _indexAll = new List<string>();
        private bool _indexValid;
        private readonly object _gate = new object();

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _reflexes.Count;
                }
            }
        }

        public string NormalizeToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return string.Empty;
            }
            return token.Trim().ToLowerInvariant();
        }

        public string Promote(
            string reflexIdRaw,
            Tensor vector,
            Dictionary<string, string> metaInput,
            bool overwrite,
            out string resolvedReflexId,
            Dictionary<string, string> seqShaIndex = null)
        {
            resolvedReflexId = string.Empty;
            string reflexId = NormalizeToken(reflexIdRaw);
            if (string.IsNullOrEmpty(reflexId))
            {
                return "missing_reflex_id";
            }

            Dictionary<string, string> meta = metaInput == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(metaInput, StringComparer.OrdinalIgnoreCase);

            string seqSha = string.Empty;
            if (meta.TryGetValue("sequence_sha1", out string rawSha))
            {
                seqSha = NormalizeToken(rawSha);
                meta["sequence_sha1"] = seqSha;
            }

            uint incomingSymbolId = ParseSymbolId(meta);
            double incomingStability = MetaStability(meta);

            bool hasVector = vector != null && !vector.IsEmpty;
            Tensor vec = hasVector ? TensorOps.NormalizeL2(vector.Flatten()) : new Tensor();

            lock (_gate)
            {
                if (!string.IsNullOrEmpty(seqSha))
                {
                    if (seqShaIndex != null)
                    {
                        if (seqShaIndex.TryGetValue(seqSha, out string existingReflex))
                        {
                            if (_reflexes.TryGetValue(existingReflex, out ReflexEntry existingEntry))
                            {
                                resolvedReflexId = existingReflex;
                                string hitResult = HandleExistingSequenceHit(
                                    existingEntry,
                                    existingReflex,
                                    incomingStability,
                                    incomingSymbolId,
                                    hasVector,
                                    vec,
                                    meta,
                                    seqSha,
                                    seqShaIndex);
                                return hitResult;
                            }
                            seqShaIndex.Remove(seqSha);
                        }
                    }
                    else
                    {
                        foreach (KeyValuePair<string, ReflexEntry> kv in _reflexes)
                        {
                            if (!kv.Value.Meta.TryGetValue("sequence_sha1", out string existingRaw))
                            {
                                continue;
                            }
                            string existingSha = NormalizeToken(existingRaw);
                            if (existingSha != seqSha)
                            {
                                continue;
                            }

                            resolvedReflexId = kv.Key;
                            string hitResult = HandleExistingSequenceHit(
                                kv.Value,
                                kv.Key,
                                incomingStability,
                                incomingSymbolId,
                                hasVector,
                                vec,
                                meta,
                                seqSha,
                                seqShaIndex);
                            return hitResult;
                        }
                    }
                }

                bool hadExisting = _reflexes.TryGetValue(reflexId, out ReflexEntry existing);
                if (hadExisting && !overwrite)
                {
                    double existingStability = MetaStability(existing.Meta);
                    if (incomingStability > existingStability)
                    {
                        MergeMeta(existing.Meta, meta);
                        if (incomingSymbolId != uint.MaxValue)
                        {
                            existing.SymbolId = incomingSymbolId;
                        }
                        _indexValid = false;
                        resolvedReflexId = reflexId;
                        return "updated_meta";
                    }

                    resolvedReflexId = reflexId;
                    return "exists";
                }

                if (hadExisting && seqShaIndex != null && existing.Meta.TryGetValue("sequence_sha1", out string oldShaRaw))
                {
                    string oldSha = NormalizeToken(oldShaRaw);
                    if (!string.IsNullOrEmpty(oldSha) && oldSha != seqSha &&
                        seqShaIndex.TryGetValue(oldSha, out string oldMapped) && oldMapped == reflexId)
                    {
                        seqShaIndex.Remove(oldSha);
                    }
                }

                ReflexEntry entry = new ReflexEntry
                {
                    Vector = hasVector ? vec : new Tensor(),
                    Meta = meta,
                    SymbolId = incomingSymbolId
                };
                _reflexes[reflexId] = entry;
                _indexValid = false;
                if (!string.IsNullOrEmpty(seqSha) && seqShaIndex != null)
                {
                    seqShaIndex[seqSha] = reflexId;
                }

                resolvedReflexId = reflexId;
                return hadExisting ? "overwritten" : "inserted";
            }
        }

        public QueryResult Query(
            string scopeRaw,
            string targetIdRaw,
            string labelRaw,
            double minStability,
            int limit,
            bool includeVectors,
            SymbolSpace symbols)
        {
            string scope = NormalizeToken(scopeRaw);
            if (scope != "label" && scope != "target" && scope != "global")
            {
                scope = "label";
            }

            string targetId = (targetIdRaw ?? string.Empty).Trim().ToUpperInvariant();
            string label = NormalizeToken(labelRaw);

            QueryResult result = new QueryResult
            {
                Scope = scope,
                Dim = symbols.SymbolDim
            };

            lock (_gate)
            {
                EnsureQueryIndexLocked();
                List<string> candidateIds;
                if (scope == "target")
                {
                    if (string.IsNullOrEmpty(targetId) || !_indexByTarget.TryGetValue(targetId, out candidateIds))
                    {
                        candidateIds = new List<string>();
                    }
                }
                else if (scope == "label")
                {
                    if (string.IsNullOrEmpty(label) || !_indexByLabel.TryGetValue(label, out candidateIds))
                    {
                        candidateIds = new List<string>();
                    }
                }
                else
                {
                    candidateIds = new List<string>(_indexAll);
                }

                List<Hit> hits = new List<Hit>();
                for (int i = 0; i < candidateIds.Count; i++)
                {
                    if (!_reflexes.TryGetValue(candidateIds[i], out ReflexEntry entry))
                    {
                        continue;
                    }

                    double stability = MetaStability(entry.Meta);
                    if (stability < minStability)
                    {
                        continue;
                    }

                    Hit hit = new Hit
                    {
                        ReflexId = candidateIds[i],
                        Meta = new Dictionary<string, string>(entry.Meta),
                        Stability = stability,
                        Edits = MetaInt(entry.Meta, "edits", 999999),
                        Vector = ResolveHitVector(entry, symbols, result.Dim)
                    };
                    hits.Add(hit);
                }

                SortHitsInPlace(hits);
                if (limit > 0 && hits.Count > limit)
                {
                    hits.RemoveRange(limit, hits.Count - limit);
                }

                result.ReflexIds.Capacity = hits.Count;
                result.Stabilities.Capacity = hits.Count;
                result.Metas.Capacity = hits.Count;
                if (includeVectors)
                {
                    result.ReflexVectors.Capacity = hits.Count;
                }

                for (int i = 0; i < hits.Count; i++)
                {
                    result.ReflexIds.Add(hits[i].ReflexId);
                    result.Stabilities.Add((float)hits[i].Stability);
                    result.Metas.Add(hits[i].Meta);
                    if (includeVectors)
                    {
                        result.ReflexVectors.Add(hits[i].Vector.Copy());
                    }
                }
            }

            return result;
        }

        public ReflexStats GetStats(int dim)
        {
            lock (_gate)
            {
                long vectorBytes = 0;
                long symbolIdBytes = 0;
                foreach (KeyValuePair<string, ReflexEntry> kv in _reflexes)
                {
                    ReflexEntry entry = kv.Value;
                    if (entry.Vector != null && !entry.Vector.IsEmpty)
                    {
                        vectorBytes += entry.Vector.Total * sizeof(float);
                    }
                    else if (entry.SymbolId != uint.MaxValue)
                    {
                        symbolIdBytes += sizeof(uint);
                    }
                }

                return new ReflexStats
                {
                    Count = _reflexes.Count,
                    ApproxVectorBytes = vectorBytes,
                    ApproxSymbolIdBytes = symbolIdBytes,
                    ApproxBytes = vectorBytes + symbolIdBytes,
                    Dim = dim
                };
            }
        }

        public Dictionary<string, ReflexEntry> Snapshot()
        {
            lock (_gate)
            {
                Dictionary<string, ReflexEntry> snapshot = new Dictionary<string, ReflexEntry>(_reflexes.Count);
                foreach (KeyValuePair<string, ReflexEntry> kv in _reflexes)
                {
                    snapshot[kv.Key] = new ReflexEntry
                    {
                        Vector = kv.Value.Vector == null ? new Tensor() : kv.Value.Vector.Copy(),
                        Meta = new Dictionary<string, string>(kv.Value.Meta),
                        SymbolId = kv.Value.SymbolId
                    };
                }
                return snapshot;
            }
        }

        public void ReplaceAll(Dictionary<string, ReflexEntry> reflexes)
        {
            lock (_gate)
            {
                _reflexes.Clear();
                if (reflexes != null)
                {
                    foreach (KeyValuePair<string, ReflexEntry> kv in reflexes)
                    {
                        string reflexId = NormalizeToken(kv.Key);
                        if (string.IsNullOrEmpty(reflexId))
                        {
                            continue;
                        }

                        ReflexEntry src = kv.Value ?? new ReflexEntry();
                        ReflexEntry copy = new ReflexEntry
                        {
                            Vector = src.Vector == null ? new Tensor() : src.Vector.Copy(),
                            Meta = src.Meta == null ? new Dictionary<string, string>() : new Dictionary<string, string>(src.Meta),
                            SymbolId = src.SymbolId
                        };
                        if (!copy.Meta.ContainsKey("stability"))
                        {
                            copy.Meta["stability"] = "0";
                        }
                        _reflexes[reflexId] = copy;
                    }
                }
                _indexValid = false;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _reflexes.Clear();
                _indexByTarget.Clear();
                _indexByLabel.Clear();
                _indexAll.Clear();
                _indexValid = false;
            }
        }

        private sealed class Hit
        {
            public string ReflexId = string.Empty;
            public Dictionary<string, string> Meta = new Dictionary<string, string>();
            public double Stability;
            public int Edits = 999999;
            public Tensor Vector = new Tensor();
        }

        private static int HitComparer(Hit a, Hit b)
        {
            int stabilityCmp = b.Stability.CompareTo(a.Stability);
            if (stabilityCmp != 0)
            {
                return stabilityCmp;
            }

            int editsCmp = a.Edits.CompareTo(b.Edits);
            if (editsCmp != 0)
            {
                return editsCmp;
            }

            return string.CompareOrdinal(a.ReflexId, b.ReflexId);
        }

        private static void SortHitsInPlace(List<Hit> hits)
        {
            for (int i = 1; i < hits.Count; i++)
            {
                Hit key = hits[i];
                int j = i - 1;
                while (j >= 0 && HitComparer(hits[j], key) > 0)
                {
                    hits[j + 1] = hits[j];
                    j--;
                }
                hits[j + 1] = key;
            }
        }

        private Tensor ResolveHitVector(ReflexEntry entry, SymbolSpace symbols, int dim)
        {
            if (entry.Vector != null && !entry.Vector.IsEmpty)
            {
                return entry.Vector.Copy();
            }

            if (entry.SymbolId != uint.MaxValue && symbols.TryGetVectorById(entry.SymbolId, out Tensor byId))
            {
                return byId;
            }

            if (entry.Meta.TryGetValue("next_token", out string nextToken) &&
                symbols.TryGetVectorByToken(nextToken, out Tensor byToken))
            {
                return byToken;
            }

            return dim > 0 ? new Tensor(new Shape(dim), 0.0f) : new Tensor();
        }

        private string HandleExistingSequenceHit(
            ReflexEntry existingEntry,
            string existingReflexId,
            double incomingStability,
            uint incomingSymbolId,
            bool hasVector,
            Tensor incomingVector,
            Dictionary<string, string> incomingMeta,
            string seqSha,
            Dictionary<string, string> seqShaIndex)
        {
            double existingStability = MetaStability(existingEntry.Meta);
            if (incomingStability > existingStability)
            {
                MergeMeta(existingEntry.Meta, incomingMeta);
                if (incomingSymbolId != uint.MaxValue)
                {
                    existingEntry.SymbolId = incomingSymbolId;
                }
                _indexValid = false;
                if (seqShaIndex != null && !string.IsNullOrEmpty(seqSha))
                {
                    seqShaIndex[seqSha] = existingReflexId;
                }
                return "updated_meta";
            }

            if (hasVector &&
                existingEntry.Vector != null &&
                !existingEntry.Vector.IsEmpty &&
                ApproxTensorEqual(existingEntry.Vector, incomingVector, 1e-6f))
            {
                return "duplicate_exact";
            }

            return "duplicate_sequence";
        }

        private void EnsureQueryIndexLocked()
        {
            if (_indexValid)
            {
                return;
            }

            _indexByTarget.Clear();
            _indexByLabel.Clear();
            _indexAll.Clear();
            _indexAll.Capacity = _reflexes.Count;

            foreach (KeyValuePair<string, ReflexEntry> kv in _reflexes)
            {
                string reflexId = kv.Key;
                ReflexEntry entry = kv.Value;
                _indexAll.Add(reflexId);

                if (entry.Meta.TryGetValue("target_id", out string targetIdRaw))
                {
                    string targetId = (targetIdRaw ?? string.Empty).ToUpperInvariant();
                    if (!string.IsNullOrEmpty(targetId))
                    {
                        if (!_indexByTarget.TryGetValue(targetId, out List<string> ids))
                        {
                            ids = new List<string>();
                            _indexByTarget[targetId] = ids;
                        }
                        ids.Add(reflexId);
                    }
                }

                if (entry.Meta.TryGetValue("label", out string labelRaw))
                {
                    string label = NormalizeToken(labelRaw);
                    if (!string.IsNullOrEmpty(label))
                    {
                        if (!_indexByLabel.TryGetValue(label, out List<string> ids))
                        {
                            ids = new List<string>();
                            _indexByLabel[label] = ids;
                        }
                        ids.Add(reflexId);
                    }
                }
            }

            _indexValid = true;
        }

        private static void MergeMeta(Dictionary<string, string> dst, Dictionary<string, string> src)
        {
            if (src == null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> kv in src)
            {
                dst[kv.Key] = kv.Value;
            }
        }

        private static uint ParseSymbolId(Dictionary<string, string> meta)
        {
            if (meta == null || !meta.TryGetValue("symbol_id", out string raw))
            {
                return uint.MaxValue;
            }

            if (uint.TryParse(raw, out uint parsed))
            {
                return parsed;
            }

            return uint.MaxValue;
        }

        private static double MetaStability(Dictionary<string, string> meta)
        {
            if (meta == null || !meta.TryGetValue("stability", out string raw))
            {
                return 0.0;
            }
            if (double.TryParse(raw, out double value))
            {
                return value;
            }
            return 0.0;
        }

        private static int MetaInt(Dictionary<string, string> meta, string key, int defValue)
        {
            if (meta == null || !meta.TryGetValue(key, out string raw))
            {
                return defValue;
            }
            if (int.TryParse(raw, out int value))
            {
                return value;
            }
            return defValue;
        }

        private static bool ApproxTensorEqual(Tensor a, Tensor b, float tol)
        {
            if (a == null || b == null || a.Total != b.Total)
            {
                return false;
            }

            for (int i = 0; i < a.Total; i++)
            {
                if (Math.Abs(a.Data[i] - b.Data[i]) > tol)
                {
                    return false;
                }
            }
            return true;
        }
    }
}

