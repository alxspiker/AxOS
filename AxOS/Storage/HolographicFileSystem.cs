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

namespace AxOS.Storage
{
    public sealed class HolographicFileSystem
    {
        public sealed class Entry
        {
            public string Id = string.Empty;
            public string Intent = string.Empty;
            public string Content = string.Empty;
            public string FilePath = string.Empty;
            public long UtcTicks;
            public Tensor IntentVector = new Tensor();
            public Tensor PayloadVector = new Tensor();

            public int Dim => IntentVector.Total;

            public Entry Copy()
            {
                return new Entry
                {
                    Id = Id,
                    Intent = Intent,
                    Content = Content,
                    FilePath = FilePath,
                    UtcTicks = UtcTicks,
                    IntentVector = IntentVector == null ? new Tensor() : IntentVector.Copy(),
                    PayloadVector = PayloadVector == null ? new Tensor() : PayloadVector.Copy()
                };
            }
        }

        public sealed class QueryHit
        {
            public string Id = string.Empty;
            public string Intent = string.Empty;
            public string Content = string.Empty;
            public string FilePath = string.Empty;
            public long UtcTicks;
            public double Similarity;
        }

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AXHFS1");
        private static readonly byte[] IndexMagic = Encoding.ASCII.GetBytes("AXIDX1");
        private const uint Version = 1;
        private const uint IndexVersion = 1;
        private const string EntryExtension = ".hfs";
        private const string IndexFileName = "index.axidx";

        private readonly HdcSystem _hdc;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly object _gate = new object();

        private string _rootPath = string.Empty;
        private string _indexPath = string.Empty;
        private bool _initialized;

        public HolographicFileSystem(HdcSystem hdc)
        {
            _hdc = hdc ?? throw new ArgumentNullException(nameof(hdc));
        }

        public bool IsInitialized
        {
            get
            {
                lock (_gate)
                {
                    return _initialized;
                }
            }
        }

        public string RootPath
        {
            get
            {
                lock (_gate)
                {
                    return _rootPath;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        public bool Initialize(string rootPath, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                error = "missing_hfs_root";
                return false;
            }

            string normalizedRoot = NormalizePath(rootPath);
            try
            {
                if (!Directory.Exists(normalizedRoot))
                {
                    Directory.CreateDirectory(normalizedRoot);
                }
            }
            catch
            {
                error = "hfs_root_create_failed";
                return false;
            }

            string indexPath = NormalizePath(Path.Combine(normalizedRoot, IndexFileName));
            List<Entry> loaded = new List<Entry>();

            if (!LoadIndexIds(indexPath, out List<string> indexedIds, out error))
            {
                return false;
            }

            for (int i = 0; i < indexedIds.Count; i++)
            {
                string id = indexedIds[i];
                if (string.IsNullOrWhiteSpace(id))
                {
                    error = "hfs_index_invalid_id";
                    return false;
                }

                string filePath = NormalizePath(Path.Combine(normalizedRoot, id + EntryExtension));
                if (!File.Exists(filePath))
                {
                    error = "hfs_index_entry_missing";
                    return false;
                }

                if (!TryLoadEntryFile(filePath, out Entry entry))
                {
                    error = "hfs_entry_read_failed";
                    return false;
                }

                if (string.IsNullOrEmpty(entry.Id))
                {
                    entry.Id = id;
                }
                else if (!string.Equals(entry.Id, id, StringComparison.Ordinal))
                {
                    error = "hfs_entry_id_mismatch";
                    return false;
                }

                loaded.Add(entry);
            }

            if (!SaveIndexFile(indexPath, loaded, out error))
            {
                return false;
            }

            lock (_gate)
            {
                _rootPath = normalizedRoot;
                _indexPath = indexPath;
                _entries.Clear();
                _entries.Capacity = loaded.Count;
                for (int i = 0; i < loaded.Count; i++)
                {
                    _entries.Add(loaded[i]);
                    _hdc.Remember(loaded[i].PayloadVector, out _);
                }
                _initialized = true;
            }

            return true;
        }

        public bool Write(string intent, string content, int dim, out Entry entry, out string error)
        {
            entry = new Entry();
            error = string.Empty;

            string root;
            lock (_gate)
            {
                if (!_initialized)
                {
                    error = "hfs_not_initialized";
                    return false;
                }
                root = _rootPath;
            }

            if (string.IsNullOrWhiteSpace(intent))
            {
                error = "missing_intent";
                return false;
            }
            if (content == null)
            {
                content = string.Empty;
            }

            int safeDim = Math.Max(1, dim);
            if (_hdc.MaxHdcDim > 0 && safeDim > _hdc.MaxHdcDim)
            {
                error = "hdc_dim_limit_exceeded";
                return false;
            }

            if (!EncodeText(intent, safeDim, out Tensor intentVector, out error))
            {
                return false;
            }
            if (!EncodeText(content, safeDim, out Tensor contentVector, out error))
            {
                return false;
            }

            Tensor payload = TensorOps.NormalizeL2(TensorOps.Bind(intentVector, contentVector));
            if (!_hdc.Remember(payload, out error))
            {
                return false;
            }

            string id = NewEntryId(payload);
            string filePath = NormalizePath(Path.Combine(root, id + EntryExtension));
            Entry e = new Entry
            {
                Id = id,
                Intent = intent,
                Content = content,
                FilePath = filePath,
                UtcTicks = DateTime.UtcNow.Ticks,
                IntentVector = intentVector.Copy(),
                PayloadVector = payload.Copy()
            };

            if (!SaveEntryFile(e, out error))
            {
                return false;
            }

            lock (_gate)
            {
                List<Entry> nextEntries = new List<Entry>(_entries.Count + 1);
                for (int i = 0; i < _entries.Count; i++)
                {
                    nextEntries.Add(_entries[i]);
                }
                nextEntries.Add(e.Copy());

                if (!SaveIndexFile(_indexPath, nextEntries, out error))
                {
                    return false;
                }

                _entries.Add(e.Copy());
            }

            entry = e;
            return true;
        }

        public bool ReadBest(string intentQuery, int dim, out QueryHit hit, out string error)
        {
            hit = new QueryHit();
            error = string.Empty;
            if (!Search(intentQuery, dim, 1, out List<QueryHit> hits, out error))
            {
                return false;
            }

            if (hits.Count == 0)
            {
                error = "not_found";
                return false;
            }

            hit = hits[0];
            return true;
        }

        public bool Search(string intentQuery, int dim, int topK, out List<QueryHit> hits, out string error)
        {
            hits = new List<QueryHit>();
            error = string.Empty;

            List<Entry> snapshot;
            lock (_gate)
            {
                if (!_initialized)
                {
                    error = "hfs_not_initialized";
                    return false;
                }

                snapshot = new List<Entry>(_entries.Count);
                for (int i = 0; i < _entries.Count; i++)
                {
                    snapshot.Add(_entries[i].Copy());
                }
            }

            if (snapshot.Count == 0)
            {
                return true;
            }

            int safeDim = Math.Max(1, dim);
            if (!EncodeText(intentQuery, safeDim, out Tensor queryVector, out error))
            {
                return false;
            }

            List<QueryHit> all = new List<QueryHit>(snapshot.Count);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Entry e = snapshot[i];
                if (e.IntentVector == null || e.IntentVector.IsEmpty || e.IntentVector.Total != queryVector.Total)
                {
                    continue;
                }

                double intentSim = TensorOps.CosineSimilarity(queryVector, e.IntentVector);
                double payloadSim = 0.0;
                if (e.PayloadVector != null && !e.PayloadVector.IsEmpty && e.PayloadVector.Total == queryVector.Total)
                {
                    payloadSim = TensorOps.CosineSimilarity(queryVector, e.PayloadVector);
                }

                double blended = (intentSim * 0.75) + (payloadSim * 0.25);
                all.Add(new QueryHit
                {
                    Id = e.Id,
                    Intent = e.Intent,
                    Content = e.Content,
                    FilePath = e.FilePath,
                    UtcTicks = e.UtcTicks,
                    Similarity = blended
                });
            }

            SortHitsInPlace(all);
            int keep = topK <= 0 ? all.Count : Math.Min(topK, all.Count);
            if (keep < all.Count)
            {
                all.RemoveRange(keep, all.Count - keep);
            }

            hits = all;
            return true;
        }

        public List<Entry> ListRecent(int limit)
        {
            List<Entry> outList = new List<Entry>();
            lock (_gate)
            {
                outList.Capacity = _entries.Count;
                for (int i = 0; i < _entries.Count; i++)
                {
                    outList.Add(_entries[i].Copy());
                }
            }

            SortEntriesByTicksDesc(outList);
            if (limit > 0 && outList.Count > limit)
            {
                outList.RemoveRange(limit, outList.Count - limit);
            }
            return outList;
        }

        private bool EncodeText(string text, int dim, out Tensor encoded, out string error)
        {
            encoded = new Tensor();
            error = string.Empty;

            List<string> tokens = TokenizeText(text);
            if (tokens.Count == 0)
            {
                tokens.Add("empty");
            }

            List<int> positions = new List<int>(tokens.Count);
            int safeDim = Math.Max(1, dim);
            for (int i = 0; i < tokens.Count; i++)
            {
                positions.Add(i % safeDim);
            }

            bool ok = _hdc.Sequence.EncodeTokens(_hdc.Symbols, tokens, positions, safeDim, out encoded, out error, out string errorToken);
            if (!ok && !string.IsNullOrEmpty(errorToken))
            {
                error += " (" + errorToken + ")";
            }
            return ok;
        }

        private bool SaveEntryFile(Entry entry, out string error)
        {
            error = string.Empty;
            try
            {
                using FileStream fs = new FileStream(entry.FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                using BinaryWriter bw = new BinaryWriter(fs, Encoding.UTF8, false);

                bw.Write(Magic);
                bw.Write(Version);
                bw.Write(entry.UtcTicks);
                WriteString(bw, entry.Id);
                WriteString(bw, entry.Intent);
                WriteString(bw, entry.Content);

                int dim = entry.IntentVector.Total;
                bw.Write((uint)dim);
                WriteTensor(bw, entry.IntentVector, dim);
                WriteTensor(bw, entry.PayloadVector, dim);
                bw.Flush();
                fs.Flush();
                return true;
            }
            catch
            {
                error = "hfs_write_failed";
                return false;
            }
        }

        private bool LoadIndexIds(string indexPath, out List<string> ids, out string error)
        {
            ids = new List<string>();
            error = string.Empty;
            if (!File.Exists(indexPath))
            {
                return true;
            }

            try
            {
                using FileStream fs = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using BinaryReader br = new BinaryReader(fs, Encoding.UTF8, false);

                byte[] magic = br.ReadBytes(IndexMagic.Length);
                if (magic.Length != IndexMagic.Length || !BytesEqual(magic, IndexMagic))
                {
                    error = "hfs_index_magic_invalid";
                    return false;
                }

                uint version = br.ReadUInt32();
                if (version != IndexVersion)
                {
                    error = "hfs_index_version_unsupported";
                    return false;
                }

                uint count = br.ReadUInt32();
                if (count > 1000000u)
                {
                    error = "hfs_index_too_large";
                    return false;
                }

                ids.Capacity = (int)count;
                for (uint i = 0; i < count; i++)
                {
                    string id = ReadString(br, 1024);
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        error = "hfs_index_invalid_id";
                        return false;
                    }

                    for (int j = 0; j < ids.Count; j++)
                    {
                        if (string.Equals(ids[j], id, StringComparison.Ordinal))
                        {
                            error = "hfs_index_duplicate_id";
                            return false;
                        }
                    }

                    ids.Add(id);
                }
                return true;
            }
            catch (EndOfStreamException)
            {
                error = "hfs_index_read_failed";
                return false;
            }
            catch
            {
                error = "hfs_index_read_failed";
                return false;
            }
        }

        private bool SaveIndexFile(string indexPath, List<Entry> entries, out string error)
        {
            error = string.Empty;
            try
            {
                using FileStream fs = new FileStream(indexPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using BinaryWriter bw = new BinaryWriter(fs, Encoding.UTF8, false);

                bw.Write(IndexMagic);
                bw.Write(IndexVersion);
                bw.Write((uint)entries.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    WriteString(bw, entries[i].Id);
                }
                bw.Flush();
                fs.Flush();
                return true;
            }
            catch
            {
                error = "hfs_index_write_failed";
                return false;
            }
        }

        private bool TryLoadEntryFile(string filePath, out Entry entry)
        {
            entry = new Entry();
            try
            {
                using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using BinaryReader br = new BinaryReader(fs, Encoding.UTF8, false);

                byte[] magic = br.ReadBytes(Magic.Length);
                if (magic.Length != Magic.Length || !BytesEqual(magic, Magic))
                {
                    return false;
                }

                uint version = br.ReadUInt32();
                if (version != Version)
                {
                    return false;
                }

                long utcTicks = br.ReadInt64();
                string id = ReadString(br, 1024);
                string intent = ReadString(br, 8 * 1024 * 1024);
                string content = ReadString(br, 32 * 1024 * 1024);
                uint dimRaw = br.ReadUInt32();
                if (dimRaw == 0 || dimRaw > 262144)
                {
                    return false;
                }

                int dim = (int)dimRaw;
                Tensor intentVector = ReadTensor(br, dim);
                Tensor payloadVector = ReadTensor(br, dim);

                entry = new Entry
                {
                    Id = string.IsNullOrEmpty(id) ? Path.GetFileNameWithoutExtension(filePath) : id,
                    Intent = intent,
                    Content = content,
                    FilePath = NormalizePath(filePath),
                    UtcTicks = utcTicks,
                    IntentVector = intentVector,
                    PayloadVector = payloadVector
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteTensor(BinaryWriter bw, Tensor tensor, int dim)
        {
            Tensor flat = tensor.Flatten();
            for (int i = 0; i < dim; i++)
            {
                float value = i < flat.Total ? flat.Data[i] : 0.0f;
                bw.Write(value);
            }
        }

        private static Tensor ReadTensor(BinaryReader br, int dim)
        {
            float[] raw = new float[dim];
            for (int i = 0; i < dim; i++)
            {
                raw[i] = br.ReadSingle();
            }
            return TensorOps.NormalizeL2(new Tensor(raw));
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('/', '\\');
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

        private static string NewEntryId(Tensor payload)
        {
            ulong hash = HashTensor(payload);
            long ticks = DateTime.UtcNow.Ticks;
            return ticks.ToString("x16") + "_" + hash.ToString("x16");
        }

        private static ulong HashTensor(Tensor tensor)
        {
            Tensor flat = tensor.Flatten();
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < flat.Total; i++)
            {
                float value = flat.Data[i];
                int scaledInt = (int)(value * 1000000.0f);
                uint bits = unchecked((uint)scaledInt);

                hash ^= (byte)(bits & 0xFF);
                hash *= 1099511628211UL;
                hash ^= (byte)((bits >> 8) & 0xFF);
                hash *= 1099511628211UL;
                hash ^= (byte)((bits >> 16) & 0xFF);
                hash *= 1099511628211UL;
                hash ^= (byte)((bits >> 24) & 0xFF);
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static bool BytesEqual(byte[] lhs, byte[] rhs)
        {
            if (lhs.Length != rhs.Length)
            {
                return false;
            }
            for (int i = 0; i < lhs.Length; i++)
            {
                if (lhs[i] != rhs[i])
                {
                    return false;
                }
            }
            return true;
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

        private static void SortHitsInPlace(List<QueryHit> hits)
        {
            for (int i = 1; i < hits.Count; i++)
            {
                QueryHit key = hits[i];
                int j = i - 1;
                while (j >= 0 && CompareHits(hits[j], key) > 0)
                {
                    hits[j + 1] = hits[j];
                    j--;
                }
                hits[j + 1] = key;
            }
        }

        private static int CompareHits(QueryHit a, QueryHit b)
        {
            int scoreCmp = b.Similarity.CompareTo(a.Similarity);
            if (scoreCmp != 0)
            {
                return scoreCmp;
            }

            int ticksCmp = b.UtcTicks.CompareTo(a.UtcTicks);
            if (ticksCmp != 0)
            {
                return ticksCmp;
            }

            return string.CompareOrdinal(a.Id, b.Id);
        }

        private static void SortEntriesByTicksDesc(List<Entry> entries)
        {
            for (int i = 1; i < entries.Count; i++)
            {
                Entry key = entries[i];
                int j = i - 1;
                while (j >= 0 && CompareEntries(entries[j], key) > 0)
                {
                    entries[j + 1] = entries[j];
                    j--;
                }
                entries[j + 1] = key;
            }
        }

        private static int CompareEntries(Entry a, Entry b)
        {
            int ticksCmp = b.UtcTicks.CompareTo(a.UtcTicks);
            if (ticksCmp != 0)
            {
                return ticksCmp;
            }
            return string.CompareOrdinal(a.Id, b.Id);
        }
    }
}

