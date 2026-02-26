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
using System.IO;
using System.Text;

namespace AxOS.Brain
{
    public sealed class HdcSystem
    {
        private static readonly byte[] MapperMagic = Encoding.ASCII.GetBytes("BCMAPBIN");
        private const uint MapperVersionV3 = 3;

        public EpisodicMemory Memory { get; } = new EpisodicMemory();
        public SymbolSpace Symbols { get; } = new SymbolSpace();
        public ReflexStore Reflexes { get; } = new ReflexStore();
        public SequenceEncoder Sequence { get; } = new SequenceEncoder();
        public SignalPhaseAligner SignalPhase { get; } = new SignalPhaseAligner();

        public int MaxHdcDim { get; set; } = 32768;
        public string MapperStorePath { get; private set; } = string.Empty;

        public bool Remember(Tensor thought, out string error)
        {
            error = string.Empty;
            if (thought == null || thought.IsEmpty)
            {
                error = "empty_tensor";
                return false;
            }

            Tensor flat = thought.Flatten();
            if (MaxHdcDim > 0 && flat.Total > MaxHdcDim)
            {
                error = "hdc_dim_limit_exceeded";
                return false;
            }

            return Memory.Store(flat, out error);
        }

        public EpisodicMemory.RecallResult RecallSimilar(Tensor query)
        {
            return Memory.RecallSimilar(query);
        }

        public EpisodicMemory.RecallResult RecallStepsAgo(long stepsAgo)
        {
            return Memory.RecallStepsAgo(stepsAgo);
        }

        public void ClearAll()
        {
            Memory.Clear();
            Symbols.Clear();
            Reflexes.Clear();
            SignalPhase.Reset();
        }

        public bool SaveMapper(string filepath, out string error)
        {
            error = string.Empty;
            string outputPath = string.IsNullOrWhiteSpace(filepath) ? MapperStorePath : filepath;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                error = "missing_filepath";
                return false;
            }

            Dictionary<string, Tensor> symbols = Symbols.SnapshotSymbolTable();
            Dictionary<string, ReflexStore.ReflexEntry> reflexes = Reflexes.Snapshot();
            int dim = Symbols.SymbolDim;

            List<string> symbolKeys = new List<string>(symbols.Keys);
            SortStringsOrdinal(symbolKeys);
            List<string> reflexKeys = new List<string>(reflexes.Keys);
            SortStringsOrdinal(reflexKeys);

            try
            {
                using FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using BinaryWriter bw = new BinaryWriter(fs, Encoding.UTF8, false);

                bw.Write(MapperMagic);
                bw.Write(MapperVersionV3);
                bw.Write((uint)Math.Max(0, dim));
                bw.Write((ulong)symbolKeys.Count);
                bw.Write((ulong)reflexKeys.Count);

                for (int i = 0; i < symbolKeys.Count; i++)
                {
                    string token = symbolKeys[i];
                    Tensor vec = symbols[token].Flatten();
                    if (vec.Total != dim)
                    {
                        error = "symbol_dim_mismatch";
                        return false;
                    }

                    WriteString(bw, token);
                    for (int d = 0; d < dim; d++)
                    {
                        bw.Write(vec.Data[d]);
                    }
                }

                for (int i = 0; i < reflexKeys.Count; i++)
                {
                    string reflexId = reflexKeys[i];
                    ReflexStore.ReflexEntry entry = reflexes[reflexId];
                    WriteString(bw, reflexId);

                    uint flags = 0;
                    bool hasVector = entry.Vector != null && !entry.Vector.IsEmpty;
                    bool hasSymbolId = entry.SymbolId != uint.MaxValue;
                    if (hasVector)
                    {
                        flags |= 0x1u;
                    }
                    if (hasSymbolId)
                    {
                        flags |= 0x2u;
                    }
                    bw.Write(flags);

                    if (hasVector)
                    {
                        Tensor vec = entry.Vector.Flatten();
                        if (vec.Total != dim)
                        {
                            error = "reflex_dim_mismatch";
                            return false;
                        }
                        for (int d = 0; d < dim; d++)
                        {
                            bw.Write(vec.Data[d]);
                        }
                    }

                    if (hasSymbolId)
                    {
                        bw.Write(entry.SymbolId);
                    }

                    Dictionary<string, string> meta = entry.Meta ?? new Dictionary<string, string>();
                    bw.Write((uint)meta.Count);
                    foreach (KeyValuePair<string, string> kv in meta)
                    {
                        WriteString(bw, kv.Key ?? string.Empty);
                        WriteString(bw, kv.Value ?? string.Empty);
                    }
                }

                bw.Flush();
                fs.Flush();
            }
            catch
            {
                error = "write_failed";
                return false;
            }

            MapperStorePath = outputPath;
            return true;
        }

        public bool LoadMapper(string filepath, int requestedDim, out string error)
        {
            error = string.Empty;
            string inputPath = string.IsNullOrWhiteSpace(filepath) ? MapperStorePath : filepath;
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                error = "missing_filepath";
                return false;
            }

            if (!File.Exists(inputPath))
            {
                error = "open_failed";
                return false;
            }

            try
            {
                FileInfo fi = new FileInfo(inputPath);
                if (fi.Length == 0)
                {
                    Symbols.ReplaceAll(new Dictionary<string, Tensor>(), Math.Max(0, requestedDim));
                    Reflexes.ReplaceAll(new Dictionary<string, ReflexStore.ReflexEntry>());
                    MapperStorePath = inputPath;
                    return true;
                }
            }
            catch
            {
                error = "open_failed";
                return false;
            }

            Dictionary<string, Tensor> loadedSymbols = new Dictionary<string, Tensor>();
            Dictionary<string, ReflexStore.ReflexEntry> loadedReflexes = new Dictionary<string, ReflexStore.ReflexEntry>();
            int dim = 0;

            try
            {
                using FileStream fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using BinaryReader br = new BinaryReader(fs, Encoding.UTF8, false);

                byte[] magic = br.ReadBytes(MapperMagic.Length);
                if (magic.Length != MapperMagic.Length || !BytesEqual(magic, MapperMagic))
                {
                    error = "invalid_mapper_magic";
                    return false;
                }

                uint version = br.ReadUInt32();
                if (version != 2 && version != 3)
                {
                    error = "mapper_version_unsupported";
                    return false;
                }

                uint dimRaw = br.ReadUInt32();
                ulong symbolCount = br.ReadUInt64();
                ulong reflexCount = br.ReadUInt64();

                dim = dimRaw == 0 ? Math.Max(0, requestedDim) : (int)dimRaw;
                if (MaxHdcDim > 0 && dim > MaxHdcDim)
                {
                    error = "hdc_dim_limit_exceeded";
                    return false;
                }
                if (dim == 0 && (symbolCount > 0 || reflexCount > 0))
                {
                    error = "invalid_mapper_dim";
                    return false;
                }
                if (symbolCount > 100000000UL || reflexCount > 100000000UL)
                {
                    error = "mapper_count_too_large";
                    return false;
                }

                for (ulong i = 0; i < symbolCount; i++)
                {
                    string tokenRaw = ReadString(br, 4 * 1024 * 1024);
                    string token = Symbols.NormalizeToken(tokenRaw);
                    float[] raw = new float[dim];
                    for (int d = 0; d < dim; d++)
                    {
                        raw[d] = br.ReadSingle();
                    }
                    if (string.IsNullOrEmpty(token))
                    {
                        continue;
                    }
                    loadedSymbols[token] = TensorOps.NormalizeL2(new Tensor(raw));
                }

                for (ulong i = 0; i < reflexCount; i++)
                {
                    string reflexIdRaw = ReadString(br, 4 * 1024 * 1024);
                    string reflexId = Reflexes.NormalizeToken(reflexIdRaw);
                    if (string.IsNullOrEmpty(reflexId))
                    {
                        reflexId = string.Empty;
                    }

                    bool hasVector = true;
                    bool hasSymbolId = false;
                    uint symbolId = uint.MaxValue;
                    if (version >= 3)
                    {
                        uint flags = br.ReadUInt32();
                        hasVector = (flags & 0x1u) != 0u;
                        hasSymbolId = (flags & 0x2u) != 0u;
                    }

                    Tensor vec = new Tensor();
                    if (hasVector)
                    {
                        float[] raw = new float[dim];
                        for (int d = 0; d < dim; d++)
                        {
                            raw[d] = br.ReadSingle();
                        }
                        vec = TensorOps.NormalizeL2(new Tensor(raw));
                    }

                    if (hasSymbolId)
                    {
                        symbolId = br.ReadUInt32();
                    }

                    uint metaCount = br.ReadUInt32();
                    if (metaCount > 1000000u)
                    {
                        error = "mapper_meta_count_too_large";
                        return false;
                    }

                    Dictionary<string, string> meta = new Dictionary<string, string>();
                    for (uint m = 0; m < metaCount; m++)
                    {
                        string keyRaw = ReadString(br, 1024 * 1024);
                        string value = ReadString(br, 16 * 1024 * 1024);
                        string key = Reflexes.NormalizeToken(keyRaw);
                        if (string.IsNullOrEmpty(key))
                        {
                            continue;
                        }
                        meta[key] = value;
                    }

                    if (!meta.ContainsKey("stability"))
                    {
                        meta["stability"] = "0";
                    }

                    if (!string.IsNullOrEmpty(reflexId))
                    {
                        loadedReflexes[reflexId] = new ReflexStore.ReflexEntry
                        {
                            Vector = vec,
                            Meta = meta,
                            SymbolId = symbolId
                        };
                    }
                }
            }
            catch (EndOfStreamException)
            {
                error = "mapper_read_failed";
                return false;
            }
            catch
            {
                error = "mapper_read_failed";
                return false;
            }

            Symbols.ReplaceAll(loadedSymbols, dim);
            Reflexes.ReplaceAll(loadedReflexes);
            MapperStorePath = inputPath;
            return true;
        }

        private static void WriteString(BinaryWriter bw, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            bw.Write((uint)bytes.Length);
            bw.Write(bytes);
        }

        private static string ReadString(BinaryReader br, int maxLen)
        {
            uint len = br.ReadUInt32();
            if (len > maxLen)
            {
                throw new InvalidDataException("string_length_exceeded");
            }
            if (len == 0)
            {
                return string.Empty;
            }

            byte[] bytes = br.ReadBytes((int)len);
            if (bytes.Length != (int)len)
            {
                throw new EndOfStreamException();
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
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

