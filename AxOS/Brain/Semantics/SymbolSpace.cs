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
    public sealed class SymbolSpace
    {
        public sealed class SymbolStats
        {
            public int SymbolCount;
            public int SymbolDim;
        }

        private readonly Dictionary<string, Tensor> _symbolTable = new Dictionary<string, Tensor>();
        private readonly Dictionary<string, uint> _symbolIds = new Dictionary<string, uint>();
        private readonly List<Tensor> _symbolVectorsById = new List<Tensor>();
        private readonly object _gate = new object();

        private int _symbolDim;

        public int SymbolDim
        {
            get
            {
                lock (_gate)
                {
                    return _symbolDim;
                }
            }
        }

        public int SymbolCount
        {
            get
            {
                lock (_gate)
                {
                    return _symbolTable.Count;
                }
            }
        }

        public string NormalizeToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return string.Empty;
            }

            string trimmed = token.Trim().ToLowerInvariant();
            return trimmed;
        }

        public bool Register(string tokenRaw, Tensor vector, out string error)
        {
            error = string.Empty;
            string token = NormalizeToken(tokenRaw);
            if (string.IsNullOrEmpty(token))
            {
                error = "missing_token";
                return false;
            }

            Tensor normalized = TensorOps.NormalizeL2(vector.Flatten());
            if (normalized.IsEmpty)
            {
                error = "empty_symbol_vector";
                return false;
            }

            int dim = normalized.Total;
            lock (_gate)
            {
                if (_symbolDim == 0)
                {
                    _symbolDim = dim;
                }
                else if (_symbolDim != dim)
                {
                    error = "symbol_dim_mismatch";
                    return false;
                }

                _symbolTable[token] = normalized;
                InvalidateIdCacheLocked();
            }

            return true;
        }

        public bool RegisterMany(IReadOnlyList<string> tokens, IReadOnlyList<float> vectorsFlat, int dim, out int registered, out string error)
        {
            registered = 0;
            error = string.Empty;

            if (tokens == null || tokens.Count == 0)
            {
                error = "missing_tokens";
                return false;
            }

            if (vectorsFlat == null || vectorsFlat.Count == 0 || dim <= 0)
            {
                error = "missing_vectors";
                return false;
            }

            if (vectorsFlat.Count != tokens.Count * dim)
            {
                error = "vector_count_mismatch";
                return false;
            }

            lock (_gate)
            {
                if (_symbolDim == 0)
                {
                    _symbolDim = dim;
                }
                else if (_symbolDim != dim)
                {
                    error = "symbol_dim_mismatch";
                    return false;
                }

                for (int i = 0; i < tokens.Count; i++)
                {
                    string token = NormalizeToken(tokens[i]);
                    if (string.IsNullOrEmpty(token))
                    {
                        continue;
                    }

                    float[] row = new float[dim];
                    int offset = i * dim;
                    for (int d = 0; d < dim; d++)
                    {
                        row[d] = vectorsFlat[offset + d];
                    }

                    _symbolTable[token] = TensorOps.NormalizeL2(new Tensor(row));
                    registered++;
                }

                InvalidateIdCacheLocked();
            }

            return true;
        }

        public bool ResolveSymbol(string tokenRaw, int requestedDim, out Tensor vector, out string error, out string resolvedToken)
        {
            vector = new Tensor();
            error = string.Empty;
            resolvedToken = NormalizeToken(tokenRaw);
            if (string.IsNullOrEmpty(resolvedToken))
            {
                error = "empty_token";
                return false;
            }

            lock (_gate)
            {
                if (_symbolDim == 0)
                {
                    if (requestedDim <= 0)
                    {
                        error = "missing_dim";
                        return false;
                    }
                    _symbolDim = requestedDim;
                }
                else if (requestedDim > 0 && _symbolDim != requestedDim)
                {
                    error = "symbol_dim_mismatch";
                    return false;
                }

                if (!_symbolTable.TryGetValue(resolvedToken, out Tensor found))
                {
                    Tensor generated = GenerateSymbolVector(resolvedToken, _symbolDim);
                    _symbolTable[resolvedToken] = generated;
                    found = generated;
                    InvalidateIdCacheLocked();
                }

                vector = found.Copy();
                return true;
            }
        }

        public bool ResolveTokens(IReadOnlyList<string> tokensRaw, int requestedDim, out List<Tensor> vectors, out string error, out string errorToken)
        {
            vectors = new List<Tensor>();
            error = string.Empty;
            errorToken = string.Empty;
            if (tokensRaw == null)
            {
                error = "missing_tokens";
                return false;
            }

            vectors.Capacity = tokensRaw.Count;
            for (int i = 0; i < tokensRaw.Count; i++)
            {
                if (!ResolveSymbol(tokensRaw[i], requestedDim, out Tensor vec, out error, out string tokenNorm))
                {
                    errorToken = string.IsNullOrEmpty(tokenNorm) ? tokensRaw[i] : tokenNorm;
                    vectors.Clear();
                    return false;
                }
                vectors.Add(vec);
            }

            return true;
        }

        public bool ResolveSymbolIds(IReadOnlyList<string> tokensRaw, int requestedDim, out List<uint> ids, out string error, out string errorToken)
        {
            ids = new List<uint>();
            error = string.Empty;
            errorToken = string.Empty;

            if (!ResolveTokens(tokensRaw, requestedDim, out _, out error, out errorToken))
            {
                return false;
            }

            lock (_gate)
            {
                EnsureIdCacheLocked();
                ids.Capacity = tokensRaw.Count;
                for (int i = 0; i < tokensRaw.Count; i++)
                {
                    string token = NormalizeToken(tokensRaw[i]);
                    if (!_symbolIds.TryGetValue(token, out uint id))
                    {
                        error = "symbol_id_missing";
                        errorToken = token;
                        ids.Clear();
                        return false;
                    }
                    ids.Add(id);
                }
            }

            return true;
        }

        public bool TryGetVectorByToken(string tokenRaw, out Tensor vector)
        {
            string token = NormalizeToken(tokenRaw);
            lock (_gate)
            {
                if (_symbolTable.TryGetValue(token, out Tensor found))
                {
                    vector = found.Copy();
                    return true;
                }
            }
            vector = new Tensor();
            return false;
        }

        public bool TryGetVectorById(uint symbolId, out Tensor vector)
        {
            lock (_gate)
            {
                EnsureIdCacheLocked();
                if (symbolId < _symbolVectorsById.Count)
                {
                    vector = _symbolVectorsById[(int)symbolId].Copy();
                    return true;
                }
            }

            vector = new Tensor();
            return false;
        }

        public List<Tensor> SnapshotVectorsById()
        {
            lock (_gate)
            {
                EnsureIdCacheLocked();
                List<Tensor> outList = new List<Tensor>(_symbolVectorsById.Count);
                for (int i = 0; i < _symbolVectorsById.Count; i++)
                {
                    outList.Add(_symbolVectorsById[i].Copy());
                }
                return outList;
            }
        }

        public Dictionary<string, Tensor> SnapshotSymbolTable()
        {
            lock (_gate)
            {
                Dictionary<string, Tensor> outMap = new Dictionary<string, Tensor>(_symbolTable.Count);
                foreach (KeyValuePair<string, Tensor> kv in _symbolTable)
                {
                    outMap[kv.Key] = kv.Value.Copy();
                }
                return outMap;
            }
        }

        public void ReplaceAll(Dictionary<string, Tensor> symbols, int dim)
        {
            lock (_gate)
            {
                _symbolTable.Clear();
                _symbolDim = Math.Max(0, dim);
                if (symbols != null)
                {
                    foreach (KeyValuePair<string, Tensor> kv in symbols)
                    {
                        string token = NormalizeToken(kv.Key);
                        if (string.IsNullOrEmpty(token))
                        {
                            continue;
                        }
                        _symbolTable[token] = TensorOps.NormalizeL2(kv.Value.Flatten());
                    }
                }
                InvalidateIdCacheLocked();
            }
        }

        public SymbolStats GetStats()
        {
            lock (_gate)
            {
                return new SymbolStats
                {
                    SymbolCount = _symbolTable.Count,
                    SymbolDim = _symbolDim
                };
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _symbolTable.Clear();
                _symbolDim = 0;
                InvalidateIdCacheLocked();
            }
        }

        private Tensor GenerateSymbolVector(string token, int dim)
        {
            ulong seed = StableSymbolSeed(token);
            return TensorOps.RandomHypervector(dim, seed);
        }

        private ulong StableSymbolSeed(string tokenRaw)
        {
            string token = NormalizeToken(tokenRaw);
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < token.Length; i++)
            {
                hash ^= (byte)token[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private void EnsureIdCacheLocked()
        {
            if (_symbolIds.Count > 0 &&
                _symbolVectorsById.Count == _symbolIds.Count &&
                _symbolVectorsById.Count == _symbolTable.Count)
            {
                return;
            }

            List<string> keys = new List<string>(_symbolTable.Keys);
            SortStringsOrdinal(keys);
            _symbolIds.Clear();
            _symbolVectorsById.Clear();
            _symbolVectorsById.Capacity = keys.Count;
            for (int i = 0; i < keys.Count; i++)
            {
                string token = keys[i];
                _symbolIds[token] = (uint)i;
                _symbolVectorsById.Add(_symbolTable[token].Copy());
            }
        }

        private void InvalidateIdCacheLocked()
        {
            _symbolIds.Clear();
            _symbolVectorsById.Clear();
        }

        private static void SortStringsOrdinal(List<string> items)
        {
            for (int i = 1; i < items.Count; i++)
            {
                string key = items[i] ?? string.Empty;
                int j = i - 1;
                while (j >= 0 && string.CompareOrdinal(items[j] ?? string.Empty, key) > 0)
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = key;
            }
        }
    }
}

