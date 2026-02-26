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
using Cosmos.Core;
using Cosmos.Core.IOGroup;
using Cosmos.System.FileSystem;
using Cosmos.System.FileSystem.VFS;
using Sys = Cosmos.System;

namespace AxOS
{
    public class Exoskeleton : Sys.Kernel
    {
        private readonly HdcSystem _semanticHdc = new HdcSystem();
        private readonly HdcSystem _bioCoreHdc = new HdcSystem();
        private readonly HolographicFileSystem _hfs;
        private readonly KernelLoop _axKernelLoop;
        private readonly BatchController _axBatchController = new BatchController();
        private HardwareSynapse _hardwareSynapse;
        private readonly PeripheralNerve _peripheralNerve = new PeripheralNerve();
        private CosmosVFS _vfs;
        private bool _serialMode;
        private bool _vfsReady;
        private bool _hfsReady;
        private bool _activeCommandFromSerial;

        private DateTime _bootUtc;
        private const int DefaultDim = 1024;
        private const int PreviewValues = 8;
        private static readonly bool AutoRunStartupTestSequence = false;
        private HdcSystem _hdc => _semanticHdc;

        public Exoskeleton()
        {
            _hfs = new HolographicFileSystem(_semanticHdc);
            _axKernelLoop = new KernelLoop(_bioCoreHdc);
        }

        protected override void BeforeRun()
        {
            _bootUtc = DateTime.UtcNow;
            _peripheralNerve.Initialize();
            InitializeVfs();
            InitializeHfs(string.Empty, out _);
            _axKernelLoop.BootSequence();

            Console.WriteLine("AxOS booted.");
            Console.WriteLine("Type 'help' to list commands.");
            if (_hfsReady)
            {
                Console.WriteLine("HFS ready at " + _hfs.RootPath);
            }
            if (_peripheralNerve.IsReady)
            {
                _peripheralNerve.WriteLine("AxOS serial shell ready. Send commands line-by-line.");
            }

            if (AutoRunStartupTestSequence)
            {
                RunStartupTestSequence();
                return;
            }
        }

        protected override void Run()
        {
            TryInitializeHfsIfNeeded();

            if (_peripheralNerve.IsReady)
            {
                if (_peripheralNerve.TryReadLine(out string serialLine))
                {
                    _serialMode = true;
                    ExecuteCommandLine(serialLine, true);
                    return;
                }

                if (_serialMode)
                {
                    return;
                }

                // Give serial clients a short chance to claim the shell before blocking on VGA input.
                if ((DateTime.UtcNow - _bootUtc).TotalSeconds < 3.0)
                {
                    return;
                }
            }

            string line = ReadAutonomicLine("ax> ");
            ExecuteCommandLine(line, false);
        }

        private void ExecuteCommandLine(string line, bool fromSerial)
        {
            if (line == null)
            {
                return;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                return;
            }

            TryInitializeHfsIfNeeded();

            if (fromSerial)
            {
                _peripheralNerve.WriteLine("ax> " + line);
            }

            List<string> args = SplitArgs(line);
            if (args.Count == 0)
            {
                return;
            }

            try
            {
                _activeCommandFromSerial = fromSerial;
                Dispatch(args);
                if (fromSerial)
                {
                    _peripheralNerve.WriteLine("ok");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("error: " + ex.Message);
                if (fromSerial)
                {
                    _peripheralNerve.WriteLine("error: " + ex.Message);
                }
            }
            finally
            {
                _activeCommandFromSerial = false;
            }
        }

        private void Dispatch(List<string> args)
        {
            string cmd = args[0].ToLowerInvariant();
            switch (cmd)
            {
                case "help":
                    PrintHelp();
                    break;
                case "cls":
                case "clear":
                    Console.Clear();
                    break;
                case "echo":
                {
                    StringBuilder echoSb = new StringBuilder();
                    for (int i = 1; i < args.Count; i++)
                    {
                        if (i > 1) echoSb.Append(' ');
                        echoSb.Append(args[i]);
                    }
                    Console.WriteLine(echoSb.ToString());
                    break;
                }
                case "hdc":
                    HandleHdc(args);
                    break;
                case "hfs":
                    HandleHfs(args);
                    break;
                case "save":
                    HandleSaveAlias(args);
                    break;
                case "find":
                    HandleFindAlias(args);
                    break;
                case "run":
                    HandleRun(args);
                    break;
                case "holopad":
                    HandleHoloPad(args);
                    break;
                case "mapper":
                    HandleMapper(args);
                    break;
                case "algo":
                    HandleAlgo(args);
                    break;
                case "kernel":
                    HandleKernelLoop(args);
                    break;
                case "synapse":
                    HandleSynapse(args);
                    break;
                case "appdemo":
                    AxOS.Diagnostics.AppDemo.RunIsolatedManifoldDemo(_axKernelLoop, WriteInteractiveLine);
                    break;
                case "kerneltest":
                    AxOS.Diagnostics.KernelTest.RunBiologicalStressTest(_axKernelLoop, _axBatchController, GetHardwareSynapse(), WriteInteractiveLine, null);
                    break;
                case "reboot":
                    Sys.Power.Reboot();
                    break;
                case "shutdown":
                case "halt":
                    ShutdownSystem();
                    break;
                default:
                    Console.WriteLine("Unknown command. Type 'help'.");
                    break;
            }
        }

        private readonly List<ProgramManifold> _activeManifolds = new List<ProgramManifold>();

        private void HandleRun(List<string> args)
        {
            if (args.Count < 2)
            {
                WriteInteractiveLine("Usage: run \"intent\" [dim]");
                return;
            }

            string intent = args[1];
            int dim = args.Count > 2 ? ParseInt(args[2], ResolveDim()) : ResolveDim();

            // 1. Query HFS
            if (!_hfs.Search(intent, dim, 1, out List<HolographicFileSystem.QueryHit> searchResults, out string searchErr))
            {
                WriteInteractiveLine("run_failed_search: " + searchErr);
                return;
            }

            if (searchResults.Count == 0)
            {
                WriteInteractiveLine("run_failed: no_manifold_found_for_intent");
                return;
            }

            var bestMatch = searchResults[0];
            if (bestMatch.Similarity < 0.60) // Arbitrary threshold
            {
                WriteInteractiveLine("run_failed: intent_match_too_weak (" + bestMatch.Similarity.ToString("0.00") + ")");
                return;
            }

            // 2. Parse .ax Payload
            if (!RulesetParser.Parse(bestMatch.Content, out Ruleset ruleset, out string parseErr))
            {
                WriteInteractiveLine("run_parse_failed: " + parseErr);
                return;
            }

            // 3. Instantiate Manifold
            var manifold = new ProgramManifold(_axKernelLoop, ruleset, new ProgramManifold.Configuration { Name = bestMatch.Id });
            
            // 4. Register Manifold
            _activeManifolds.Add(manifold);

            WriteInteractiveLine("run_success: manifold_spawned (" + manifold.Name + ")");
            WriteInteractiveLine("  mode=" + ruleset.ConstraintMode);
            WriteInteractiveLine("  budget=" + manifold.AllocatedEnergyBudget.ToString("0.00"));
            WriteInteractiveLine("  symbols=" + ruleset.SymbolDefinitions.Count);
            WriteInteractiveLine("  reflexes=" + ruleset.ReflexTriggers.Count);
        }

        private void HandleHdc(List<string> args)
        {
            if (args.Count < 2)
            {
                PrintHdcHelp();
                return;
            }

            string sub = args[1].ToLowerInvariant();
            switch (sub)
            {
                case "help":
                    PrintHdcHelp();
                    break;

                case "stats":
                    PrintHdcStats();
                    break;

                case "clear":
                    _hdc.ClearAll();
                    Console.WriteLine("HDC state cleared.");
                    break;

                case "remember":
                {
                    if (args.Count < 3)
                    {
                        Console.WriteLine("Usage: hdc remember \"text\" [dim]");
                        return;
                    }

                    int dim = args.Count > 3 ? ParseInt(args[3], ResolveDim()) : ResolveDim();
                    if (!TryEncodeText(args[2], dim, out Tensor encoded, out List<string> tokens, out string error))
                    {
                        Console.WriteLine("encode_failed: " + error);
                        return;
                    }

                    if (!_hdc.Remember(encoded, out error))
                    {
                        Console.WriteLine("remember_failed: " + error);
                        return;
                    }

                    Console.WriteLine("stored: tokens=" + tokens.Count + ", dim=" + encoded.Total + ", step=" + _hdc.Memory.CurrentStep);
                    Console.WriteLine("vector=" + TensorPreview(encoded));
                    break;
                }

                case "recall":
                {
                    if (args.Count < 3)
                    {
                        Console.WriteLine("Usage: hdc recall \"query text\" [dim]");
                        return;
                    }

                    int dim = args.Count > 3 ? ParseInt(args[3], ResolveDim()) : ResolveDim();
                    if (!TryEncodeText(args[2], dim, out Tensor query, out _, out string error))
                    {
                        Console.WriteLine("encode_failed: " + error);
                        return;
                    }

                    EpisodicMemory.RecallResult recall = _hdc.RecallSimilar(query);
                    PrintRecallResult(recall);
                    break;
                }

                case "ago":
                {
                    if (args.Count < 3 || !long.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long stepsAgo))
                    {
                        Console.WriteLine("Usage: hdc ago <steps>");
                        return;
                    }

                    EpisodicMemory.RecallResult recall = _hdc.RecallStepsAgo(stepsAgo);
                    PrintRecallResult(recall);
                    break;
                }

                case "symbol":
                {
                    if (args.Count < 3)
                    {
                        Console.WriteLine("Usage: hdc symbol <token> [dim]");
                        return;
                    }

                    int dim = args.Count > 3 ? ParseInt(args[3], ResolveDim()) : ResolveDim();
                    if (!_hdc.Symbols.ResolveSymbol(args[2], dim, out Tensor symbol, out string error, out string token))
                    {
                        Console.WriteLine("symbol_failed: " + error);
                        return;
                    }

                    uint symbolId = uint.MaxValue;
                    _hdc.Symbols.ResolveSymbolIds(new[] { token }, dim, out List<uint> ids, out _, out _);
                    if (ids.Count > 0)
                    {
                        symbolId = ids[0];
                    }

                    Console.WriteLine("token=" + token + ", id=" + symbolId + ", dim=" + symbol.Total);
                    Console.WriteLine("vector=" + TensorPreview(symbol));
                    break;
                }

                case "encode":
                {
                    if (args.Count < 3)
                    {
                        Console.WriteLine("Usage: hdc encode \"text\" [dim]");
                        return;
                    }

                    int dim = args.Count > 3 ? ParseInt(args[3], ResolveDim()) : ResolveDim();
                    if (!TryEncodeText(args[2], dim, out Tensor encoded, out List<string> tokens, out string error))
                    {
                        Console.WriteLine("encode_failed: " + error);
                        return;
                    }

                    Console.WriteLine("encoded: tokens=" + tokens.Count + ", dim=" + encoded.Total);
                    Console.WriteLine("vector=" + TensorPreview(encoded));
                    break;
                }

                case "sim":
                {
                    if (args.Count < 4)
                    {
                        Console.WriteLine("Usage: hdc sim \"text_a\" \"text_b\" [dim]");
                        return;
                    }

                    int dim = args.Count > 4 ? ParseInt(args[4], ResolveDim()) : ResolveDim();
                    if (!TryEncodeText(args[2], dim, out Tensor a, out _, out string errorA))
                    {
                        Console.WriteLine("encode_a_failed: " + errorA);
                        return;
                    }
                    if (!TryEncodeText(args[3], dim, out Tensor b, out _, out string errorB))
                    {
                        Console.WriteLine("encode_b_failed: " + errorB);
                        return;
                    }

                    double sim = TensorOps.CosineSimilarity(a, b);
                    Console.WriteLine("similarity=" + sim.ToString("0.000000", CultureInfo.InvariantCulture));
                    break;
                }

                case "seq":
                {
                    if (args.Count < 3)
                    {
                        Console.WriteLine("Usage: hdc seq <sequence> [kmer] [stride] [max_kmers] [dim]");
                        return;
                    }

                    string seq = args[2];
                    int kmer = args.Count > 3 ? ParseInt(args[3], 5) : 5;
                    int stride = args.Count > 4 ? ParseInt(args[4], 1) : 1;
                    int maxKmers = args.Count > 5 ? ParseInt(args[5], 256) : 256;
                    int dim = args.Count > 6 ? ParseInt(args[6], ResolveDim()) : ResolveDim();

                    _hdc.Sequence.TokenizeSequenceForHdc(seq, kmer, stride, maxKmers, dim, out List<string> tokens, out List<int> positions);
                    if (!_hdc.Sequence.EncodeTokens(_hdc.Symbols, tokens, positions, dim, out Tensor encoded, out string error, out string errorToken))
                    {
                        Console.WriteLine("seq_encode_failed: " + error + (string.IsNullOrEmpty(errorToken) ? string.Empty : " (" + errorToken + ")"));
                        return;
                    }

                    Console.WriteLine("seq_encoded: tokens=" + tokens.Count + ", dim=" + encoded.Total);
                    Console.WriteLine("vector=" + TensorPreview(encoded));
                    break;
                }

                case "promote":
                {
                    if (args.Count < 4)
                    {
                        Console.WriteLine("Usage: hdc promote <reflex_id> <label> [stability] [dim]");
                        return;
                    }

                    string reflexId = args[2];
                    string label = args[3];
                    double stability = args.Count > 4 ? ParseDouble(args[4], 0.5) : 0.5;
                    int dim = args.Count > 5 ? ParseInt(args[5], ResolveDim()) : ResolveDim();

                    if (!_hdc.Symbols.ResolveSymbol(label, dim, out Tensor vector, out string error, out string resolvedLabel))
                    {
                        Console.WriteLine("promote_failed: " + error);
                        return;
                    }

                    Dictionary<string, string> meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "label", resolvedLabel },
                        { "stability", stability.ToString("0.000", CultureInfo.InvariantCulture) }
                    };

                    string outcome = _hdc.Reflexes.Promote(reflexId, vector, meta, true, out string resolvedReflexId);
                    Console.WriteLine("promote=" + outcome + ", reflex_id=" + resolvedReflexId + ", label=" + resolvedLabel);
                    break;
                }

                case "query":
                {
                    if (args.Count < 3)
                    {
                        Console.WriteLine("Usage: hdc query <label> [limit] [min_stability]");
                        return;
                    }

                    string label = args[2];
                    int limit = args.Count > 3 ? ParseInt(args[3], 5) : 5;
                    double minStability = args.Count > 4 ? ParseDouble(args[4], 0.0) : 0.0;
                    ReflexStore.QueryResult query = _hdc.Reflexes.Query("label", string.Empty, label, minStability, limit, true, _hdc.Symbols);

                    Console.WriteLine("hits=" + query.ReflexIds.Count + ", scope=" + query.Scope + ", dim=" + query.Dim);
                    for (int i = 0; i < query.ReflexIds.Count; i++)
                    {
                        Console.WriteLine(
                            "#" + (i + 1) + " reflex_id=" + query.ReflexIds[i] +
                            ", stability=" + query.Stabilities[i].ToString("0.000", CultureInfo.InvariantCulture));
                    }
                    break;
                }

                default:
                    Console.WriteLine("Unknown hdc command. Type 'hdc help'.");
                    break;
            }
        }

        private void HandleHfs(List<string> args)
        {
            if (args.Count < 2)
            {
                PrintHfsHelp();
                return;
            }

            string sub = args[1].ToLowerInvariant();
            if (!_hfsReady && sub != "init")
            {
                WriteInteractiveLine("hfs_not_ready");
                return;
            }

            switch (sub)
            {
                case "help":
                    PrintHfsHelp();
                    break;

                case "init":
                {
                    string root = args.Count > 2 ? args[2] : string.Empty;
                    if (!InitializeHfs(root, out string initError))
                    {
                        WriteInteractiveLine("hfs_init_failed: " + initError);
                        return;
                    }

                    WriteInteractiveLine("hfs_ready: root=" + _hfs.RootPath + ", entries=" + _hfs.Count);
                    break;
                }

                case "stats":
                    WriteInteractiveLine("hfs: root=" + _hfs.RootPath + ", entries=" + _hfs.Count);
                    break;

                case "write":
                {
                    if (args.Count < 4)
                    {
                        WriteInteractiveLine("Usage: hfs write \"intent\" \"content\" [dim]");
                        return;
                    }

                    int dim = args.Count > 4 ? ParseInt(args[4], ResolveDim()) : ResolveDim();
                    if (!_hfs.Write(args[2], args[3], dim, out HolographicFileSystem.Entry entry, out string error))
                    {
                        WriteInteractiveLine("hfs_write_failed: " + error);
                        return;
                    }

                    WriteInteractiveLine("hfs_saved: id=" + entry.Id + ", dim=" + entry.Dim);
                    WriteInteractiveLine("path=" + entry.FilePath);
                    break;
                }

                case "read":
                {
                    if (args.Count < 3)
                    {
                        WriteInteractiveLine("Usage: hfs read \"intent\" [dim]");
                        return;
                    }

                    int dim = args.Count > 3 ? ParseInt(args[3], ResolveDim()) : ResolveDim();
                    if (!_hfs.ReadBest(args[2], dim, out HolographicFileSystem.QueryHit hit, out string error))
                    {
                        WriteInteractiveLine("hfs_read_failed: " + error);
                        return;
                    }

                    WriteInteractiveLine(
                        "hfs_hit: id=" + hit.Id +
                        ", similarity=" + hit.Similarity.ToString("0.000000", CultureInfo.InvariantCulture));
                    WriteInteractiveLine("intent=" + hit.Intent);
                    WriteInteractiveLine("content=" + hit.Content);
                    break;
                }

                case "search":
                {
                    if (args.Count < 3)
                    {
                        WriteInteractiveLine("Usage: hfs search \"intent\" [top_k] [dim]");
                        return;
                    }

                    int topK = args.Count > 3 ? ParseInt(args[3], 5) : 5;
                    int dim = args.Count > 4 ? ParseInt(args[4], ResolveDim()) : ResolveDim();
                    if (!_hfs.Search(args[2], dim, topK, out List<HolographicFileSystem.QueryHit> hits, out string error))
                    {
                        WriteInteractiveLine("hfs_search_failed: " + error);
                        return;
                    }

                    WriteInteractiveLine("hits=" + hits.Count);
                    for (int i = 0; i < hits.Count; i++)
                    {
                        HolographicFileSystem.QueryHit hit = hits[i];
                        WriteInteractiveLine(
                            "#" + (i + 1) +
                            " id=" + hit.Id +
                            ", sim=" + hit.Similarity.ToString("0.000000", CultureInfo.InvariantCulture) +
                            ", intent=" + hit.Intent);
                    }
                    break;
                }

                case "list":
                {
                    int limit = args.Count > 2 ? ParseInt(args[2], 10) : 10;
                    List<HolographicFileSystem.Entry> entries = _hfs.ListRecent(limit);
                    WriteInteractiveLine("entries=" + entries.Count);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        HolographicFileSystem.Entry e = entries[i];
                        WriteInteractiveLine("#" + (i + 1) + " id=" + e.Id + ", intent=" + e.Intent + ", dim=" + e.Dim);
                    }
                    break;
                }

                default:
                    WriteInteractiveLine("Unknown hfs command. Type 'hfs help'.");
                    break;
            }
        }

        private void HandleSaveAlias(List<string> args)
        {
            if (!_hfsReady)
            {
                WriteInteractiveLine("hfs_not_ready");
                return;
            }

            if (args.Count < 3)
            {
                WriteInteractiveLine("Usage: save \"intent\" \"content\" [dim]");
                return;
            }

            int dim = args.Count > 3 ? ParseInt(args[3], ResolveDim()) : ResolveDim();
            if (!_hfs.Write(args[1], args[2], dim, out HolographicFileSystem.Entry entry, out string error))
            {
                WriteInteractiveLine("save_failed: " + error);
                return;
            }

            WriteInteractiveLine("saved: id=" + entry.Id + ", path=" + entry.FilePath);
        }

        private void HandleFindAlias(List<string> args)
        {
            if (!_hfsReady)
            {
                WriteInteractiveLine("hfs_not_ready");
                return;
            }

            if (args.Count < 2)
            {
                WriteInteractiveLine("Usage: find \"intent\" [dim]");
                return;
            }

            int dim = args.Count > 2 ? ParseInt(args[2], ResolveDim()) : ResolveDim();
            if (!_hfs.ReadBest(args[1], dim, out HolographicFileSystem.QueryHit hit, out string error))
            {
                WriteInteractiveLine("find_failed: " + error);
                return;
            }

            WriteInteractiveLine("found: id=" + hit.Id + ", similarity=" + hit.Similarity.ToString("0.000000", CultureInfo.InvariantCulture));
            WriteInteractiveLine(hit.Content);
        }

        private void HandleHoloPad(List<string> args)
        {
            if (!_hfsReady)
            {
                WriteInteractiveLine("hfs_not_ready");
                return;
            }

            int dim = args.Count > 1 ? ParseInt(args[1], ResolveDim()) : ResolveDim();
            double threshold = args.Count > 2 ? ParseDouble(args[2], 0.20) : 0.20;
            if (threshold < 0.0)
            {
                threshold = 0.0;
            }
            else if (threshold > 1.0)
            {
                threshold = 1.0;
            }

            WriteInteractiveLine("HoloPad (Associative Memory Editor)");
            WriteInteractiveLine("Commands: :write, :recall <thought>, :help, :exit");
            WriteInteractiveLine("Session: dim=" + dim + ", threshold=" + threshold.ToString("0.000", CultureInfo.InvariantCulture));

            while (true)
            {
                string line = ReadAutonomicLine("holopad> ");
                if (line == null)
                {
                    WriteInteractiveLine("holopad_closed");
                    return;
                }

                line = line.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.Equals(":exit", StringComparison.OrdinalIgnoreCase))
                {
                    WriteInteractiveLine("Leaving HoloPad.");
                    return;
                }

                if (line.Equals(":help", StringComparison.OrdinalIgnoreCase))
                {
                    WriteInteractiveLine("Commands: :write, :recall <thought>, :help, :exit");
                    continue;
                }

                if (line.Equals(":write", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CaptureHoloPadBuffer(out string buffer))
                    {
                        WriteInteractiveLine("write_cancelled");
                        continue;
                    }

                    string intent = ExtractAutoIntent(buffer);
                    if (!_hfs.Write(intent, buffer, dim, out HolographicFileSystem.Entry entry, out string error))
                    {
                        WriteInteractiveLine("holopad_write_failed: " + error);
                        continue;
                    }

                    WriteInteractiveLine("thought_superposed: id=" + entry.Id);
                    WriteInteractiveLine("intent=\"" + intent + "\"");
                    continue;
                }

                if (line.StartsWith(":recall", StringComparison.OrdinalIgnoreCase))
                {
                    string query = line.Length > 7 ? line.Substring(7).Trim() : string.Empty;
                    if (query.Length == 0)
                    {
                        WriteInteractiveLine("Usage: :recall <thought>");
                        continue;
                    }

                    if (!_hfs.ReadBest(query, dim, out HolographicFileSystem.QueryHit hit, out string error))
                    {
                        WriteInteractiveLine("holopad_recall_failed: " + error);
                        continue;
                    }

                    if (hit.Similarity < threshold)
                    {
                        WriteInteractiveLine(
                            "Thought dissipated. No resonance found. similarity=" +
                            hit.Similarity.ToString("0.000000", CultureInfo.InvariantCulture));
                        continue;
                    }

                    WriteInteractiveLine(
                        "--- Holographic Retrieval (Resonance: " +
                        hit.Similarity.ToString("0.000000", CultureInfo.InvariantCulture) +
                        ") ---");
                    WriteInteractiveLine(hit.Content);
                    continue;
                }

                WriteInteractiveLine("Unknown HoloPad command. Type :help");
            }
        }

        private bool CaptureHoloPadBuffer(out string buffer)
        {
            buffer = string.Empty;
            StringBuilder sb = new StringBuilder();
            int maxChars = 64 * 1024;

            WriteInteractiveLine("Enter note lines, then ':done' on its own line.");
            while (true)
            {
                string line = ReadAutonomicLine(".. ");
                if (line == null)
                {
                    return false;
                }

                if (line.Equals(":done", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }
                sb.Append(line);
                if (sb.Length > maxChars)
                {
                    WriteInteractiveLine("note_too_large");
                    return false;
                }
            }

            buffer = sb.ToString();
            if (string.IsNullOrWhiteSpace(buffer))
            {
                WriteInteractiveLine("empty_note");
                return false;
            }

            return true;
        }

        private static string ExtractAutoIntent(string content)
        {
            List<string> tokens = TokenizeText(content);
            if (tokens.Count == 0)
            {
                return "empty thought";
            }

            int take = Math.Min(8, tokens.Count);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < take; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(tokens[i]);
            }
            return sb.ToString();
        }

        private string ReadAutonomicLine(string prompt)
        {
            string safePrompt = prompt ?? string.Empty;
            Console.Write(safePrompt);
            if (_activeCommandFromSerial)
            {
                _peripheralNerve.Write(safePrompt);
            }

            StringBuilder inputBuffer = new StringBuilder();
            int cursorPos = 0;

            while (true)
            {
                bool active = false;

                if (System.Console.KeyAvailable)
                {
                    ConsoleKeyInfo keyInfo = System.Console.ReadKey(intercept: true);
                    active = true;

                    if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine();
                        if (_activeCommandFromSerial) _peripheralNerve.WriteLine(string.Empty);
                        return inputBuffer.ToString();
                    }
                    else if (keyInfo.Key == ConsoleKey.LeftArrow)
                    {
                        if (cursorPos > 0)
                        {
                            cursorPos--;
                            if (Console.CursorLeft > 0)
                            {
                                Console.CursorLeft--;
                            }
                            else if (Console.CursorTop > 0)
                            {
                                Console.CursorTop--;
                                Console.CursorLeft = Console.WindowWidth - 1;
                            }
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.RightArrow)
                    {
                        if (cursorPos < inputBuffer.Length)
                        {
                            cursorPos++;
                            if (Console.CursorLeft < Console.WindowWidth - 1)
                            {
                                Console.CursorLeft++;
                            }
                            else
                            {
                                Console.CursorTop++;
                                Console.CursorLeft = 0;
                            }
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Backspace)
                    {
                        if (cursorPos > 0)
                        {
                            cursorPos--;
                            inputBuffer.Remove(cursorPos, 1);
                            
                            if (Console.CursorLeft > 0)
                            {
                                Console.CursorLeft--;
                            }
                            else if (Console.CursorTop > 0)
                            {
                                Console.CursorTop--;
                                Console.CursorLeft = Console.WindowWidth - 1;
                            }

                            int oldLeft = Console.CursorLeft;
                            int oldTop = Console.CursorTop;

                            Console.Write(inputBuffer.ToString().Substring(cursorPos) + " ");
                            
                            Console.CursorLeft = oldLeft;
                            Console.CursorTop = oldTop;
                            
                            if (_activeCommandFromSerial) _peripheralNerve.Write("\b \b");
                        }
                    }
                    else if (keyInfo.KeyChar >= ' ' && keyInfo.KeyChar <= '~' && keyInfo.KeyChar != '\b' && keyInfo.KeyChar != 127)
                    {
                        inputBuffer.Insert(cursorPos, keyInfo.KeyChar);
                        
                        int oldLeft = Console.CursorLeft;
                        int oldTop = Console.CursorTop;

                        Console.Write(inputBuffer.ToString().Substring(cursorPos));
                        
                        cursorPos++;
                        oldLeft++;
                        if (oldLeft >= Console.WindowWidth)
                        {
                            oldLeft = 0;
                            oldTop++;
                        }
                        Console.CursorLeft = oldLeft;
                        Console.CursorTop = oldTop;

                        if (_activeCommandFromSerial) _peripheralNerve.Write(keyInfo.KeyChar.ToString());
                    }
                }

                if (_peripheralNerve.IsReady && _peripheralNerve.TryReadLine(out string serialLine))
                {
                    active = true;
                    Console.WriteLine(serialLine);
                    if (_activeCommandFromSerial) _peripheralNerve.WriteLine(serialLine);
                    return serialLine;
                }

                if (!active)
                {
                    _axKernelLoop.PollSleepScheduler(true, out _);
                    foreach (ProgramManifold pm in _activeManifolds)
                    {
                        pm.Tick(true);
                    }
                }
            }
        }

        private void WriteInteractiveLine(string text)
        {
            Console.WriteLine(text ?? string.Empty);
            if (_activeCommandFromSerial)
            {
                _peripheralNerve.WriteLine(text ?? string.Empty);
            }
        }

        private void RunStartupTestSequence()
        {
            WriteBootAndSerialLine("startup: running kernel test");
            AxOS.Diagnostics.KernelTest.RunBiologicalStressTest(
                _axKernelLoop,
                _axBatchController,
                GetHardwareSynapse(),
                WriteBootAndSerialLine,
                null);

            WriteBootAndSerialLine("startup: running appdemo");
            AxOS.Diagnostics.AppDemo.RunIsolatedManifoldDemo(_axKernelLoop, WriteBootAndSerialLine);

            WriteBootAndSerialLine("startup: tests complete, shutting down");
            ShutdownSystem();
        }

        private void WriteBootAndSerialLine(string text)
        {
            string safe = text ?? string.Empty;
            Console.WriteLine(safe);
            if (_peripheralNerve.IsReady)
            {
                _peripheralNerve.WriteLine(safe);
            }
        }

        private HardwareSynapse GetHardwareSynapse()
        {
            if (_hardwareSynapse == null)
            {
                _hardwareSynapse = new HardwareSynapse(_bioCoreHdc);
            }
            return _hardwareSynapse;
        }



        private void HandleMapper(List<string> args)
        {
            if (args.Count < 2)
            {
                PrintMapperHelp();
                return;
            }

            string sub = args[1].ToLowerInvariant();
            switch (sub)
            {
                case "help":
                    PrintMapperHelp();
                    break;

                case "save":
                {
                    if (!TryResolveMapperPath(args.Count > 2 ? args[2] : string.Empty, out string path, out string pathError))
                    {
                        Console.WriteLine("mapper_save_failed: " + pathError);
                        return;
                    }

                    if (!_hdc.SaveMapper(path, out string error))
                    {
                        Console.WriteLine("mapper_save_failed: " + error);
                        return;
                    }
                    Console.WriteLine("mapper_saved: " + _hdc.MapperStorePath);
                    break;
                }

                case "load":
                {
                    if (!TryResolveMapperPath(args.Count > 2 ? args[2] : string.Empty, out string path, out string pathError))
                    {
                        Console.WriteLine("mapper_load_failed: " + pathError);
                        return;
                    }

                    int dim = args.Count > 3 ? ParseInt(args[3], ResolveDim()) : ResolveDim();
                    if (!_hdc.LoadMapper(path, dim, out string error))
                    {
                        Console.WriteLine("mapper_load_failed: " + error);
                        return;
                    }
                    Console.WriteLine("mapper_loaded: " + _hdc.MapperStorePath);
                    PrintHdcStats();
                    break;
                }

                default:
                    Console.WriteLine("Unknown mapper command. Type 'mapper help'.");
                    break;
            }
        }

        private void HandleAlgo(List<string> args)
        {
            if (args.Count < 2)
            {
                PrintAlgoHelp();
                return;
            }

            string sub = args[1].ToLowerInvariant();
            switch (sub)
            {
                case "help":
                    PrintAlgoHelp();
                    break;



                case "vector":
                {
                    if (args.Count < 5)
                    {
                        Console.WriteLine("Usage: algo vector \"base\" \"add|-\" \"sub|-\" [dim]");
                        return;
                    }

                    int dim = args.Count > 5 ? ParseInt(args[5], ResolveDim()) : ResolveDim();
                    if (!TryEncodeText(args[2], dim, out Tensor baseVec, out _, out string baseError))
                    {
                        Console.WriteLine("base_encode_failed: " + baseError);
                        return;
                    }

                    Tensor addVec = null;
                    if (args[3] != "-")
                    {
                        if (!TryEncodeText(args[3], dim, out addVec, out _, out string addError))
                        {
                            Console.WriteLine("add_encode_failed: " + addError);
                            return;
                        }
                    }

                    Tensor subVec = null;
                    if (args[4] != "-")
                    {
                        if (!TryEncodeText(args[4], dim, out subVec, out _, out string subError))
                        {
                            Console.WriteLine("sub_encode_failed: " + subError);
                            return;
                        }
                    }

                    Tensor output = HdcAlgorithms.VectorMath(baseVec, addVec, subVec, true);
                    Console.WriteLine("vector_math_dim=" + output.Total);
                    Console.WriteLine("vector=" + TensorPreview(output));
                    break;
                }

                case "cosine":
                {
                    if (args.Count < 4)
                    {
                        Console.WriteLine("Usage: algo cosine \"query\" \"cand_a|cand_b|...\" [dim] [top_k]");
                        return;
                    }

                    int dim = args.Count > 4 ? ParseInt(args[4], ResolveDim()) : ResolveDim();
                    int topK = args.Count > 5 ? ParseInt(args[5], 5) : 5;

                    List<string> candidates = SplitPipeList(args[3]);
                    if (candidates.Count == 0)
                    {
                        Console.WriteLine("missing_candidates");
                        return;
                    }

                    if (!TryEncodeText(args[2], dim, out Tensor query, out _, out string queryError))
                    {
                        Console.WriteLine("query_encode_failed: " + queryError);
                        return;
                    }

                    List<Tensor> candidateVectors = new List<Tensor>(candidates.Count);
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (!TryEncodeText(candidates[i], dim, out Tensor vec, out _, out string candError))
                        {
                            Console.WriteLine("candidate_encode_failed: " + candError);
                            return;
                        }
                        candidateVectors.Add(vec);
                    }

                    HdcAlgorithms.CosineSearchResult result = HdcAlgorithms.CosineSearch(query, candidateVectors, topK);
                    Console.WriteLine("cosine_topk=" + result.TopK + ", rows=" + result.Rows + ", dim=" + result.Dim);
                    for (int i = 0; i < result.Indices.Length; i++)
                    {
                        int idx = result.Indices[i];
                        Console.WriteLine(
                            "#" + (i + 1) + " idx=" + idx +
                            ", score=" + result.Scores[i].ToString("0.000000", CultureInfo.InvariantCulture) +
                            ", text=\"" + candidates[idx] + "\"");
                    }
                    break;
                }

                case "relax":
                {
                    if (args.Count < 3)
                    {
                        Console.WriteLine("Usage: algo relax \"item_a|item_b|...\" [dim] [iterations]");
                        return;
                    }

                    int dim = args.Count > 3 ? ParseInt(args[3], ResolveDim()) : ResolveDim();
                    int iterations = args.Count > 4 ? ParseInt(args[4], 5) : 5;

                    List<string> items = SplitPipeList(args[2]);
                    if (items.Count == 0)
                    {
                        Console.WriteLine("missing_items");
                        return;
                    }

                    List<Tensor> vectors = new List<Tensor>(items.Count);
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (!TryEncodeText(items[i], dim, out Tensor vec, out _, out string itemError))
                        {
                            Console.WriteLine("item_encode_failed: " + itemError);
                            return;
                        }
                        vectors.Add(vec);
                    }

                    HdcAlgorithms.HopfieldRelaxResult result = HdcAlgorithms.HopfieldRelax(vectors, iterations, 0.9, 2.0, true);
                    Console.WriteLine("relax_dim=" + result.Dim + ", rows=" + result.Rows + ", iterations=" + result.Iterations);
                    Console.WriteLine("attractor=" + TensorPreview(result.Value));
                    for (int i = 0; i < result.Alignments.Length; i++)
                    {
                        Console.WriteLine(
                            "#" + (i + 1) + " align=" + result.Alignments[i].ToString("0.000000", CultureInfo.InvariantCulture) +
                            ", text=\"" + items[i] + "\"");
                    }
                    break;
                }

                case "manifold":
                {
                    if (args.Count < 4)
                    {
                        Console.WriteLine("Usage: algo manifold \"train_a|train_b|...\" \"cand_a|cand_b|...\" [dim] [top_k] [iterations]");
                        return;
                    }

                    int dim = args.Count > 4 ? ParseInt(args[4], ResolveDim()) : ResolveDim();
                    int topK = args.Count > 5 ? ParseInt(args[5], 5) : 5;
                    int iterations = args.Count > 6 ? ParseInt(args[6], 10) : 10;

                    List<string> trainItems = SplitPipeList(args[2]);
                    List<string> candidateItems = SplitPipeList(args[3]);
                    if (trainItems.Count == 0 || candidateItems.Count == 0)
                    {
                        Console.WriteLine("missing_train_or_candidates");
                        return;
                    }

                    List<Tensor> trainVectors = new List<Tensor>(trainItems.Count);
                    for (int i = 0; i < trainItems.Count; i++)
                    {
                        if (!TryEncodeText(trainItems[i], dim, out Tensor vec, out _, out string trainError))
                        {
                            Console.WriteLine("train_encode_failed: " + trainError);
                            return;
                        }
                        trainVectors.Add(vec);
                    }

                    List<Tensor> candidateVectors = new List<Tensor>(candidateItems.Count);
                    for (int i = 0; i < candidateItems.Count; i++)
                    {
                        if (!TryEncodeText(candidateItems[i], dim, out Tensor vec, out _, out string candError))
                        {
                            Console.WriteLine("candidate_encode_failed: " + candError);
                            return;
                        }
                        candidateVectors.Add(vec);
                    }

                    HdcAlgorithms.HopfieldManifoldSearchResult result = HdcAlgorithms.HopfieldManifoldSearch(
                        trainVectors,
                        candidateVectors,
                        iterations,
                        0.9,
                        2.0,
                        0.1,
                        topK,
                        true);

                    Console.WriteLine(
                        "manifold_dim=" + result.Dim +
                        ", train_rows=" + result.TrainRows +
                        ", candidate_rows=" + result.CandidateRows +
                        ", topk=" + result.TopK);
                    for (int i = 0; i < result.Indices.Length; i++)
                    {
                        int idx = result.Indices[i];
                        Console.WriteLine(
                            "#" + (i + 1) + " idx=" + idx +
                            ", score=" + result.Scores[i].ToString("0.000000", CultureInfo.InvariantCulture) +
                            ", text=\"" + candidateItems[idx] + "\"");
                    }
                    break;
                }

                default:
                    Console.WriteLine("Unknown algo command. Type 'algo help'.");
                    break;
            }
        }

        private void HandleKernelLoop(List<string> args)
        {
            if (args.Count < 2)
            {
                PrintKernelLoopHelp();
                return;
            }

            string sub = args[1].ToLowerInvariant();
            switch (sub)
            {
                case "help":
                    PrintKernelLoopHelp();
                    break;

                case "status":
                {
                    KernelLoop.StatusSnapshot status = _axKernelLoop.GetStatus();
                    WriteInteractiveLine(
                        "kernel_status: energy=" +
                        status.EnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                        "/" +
                        status.MaxEnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", energy_pct=" +
                        (status.EnergyPercent * 100.0f).ToString("0.0", CultureInfo.InvariantCulture) +
                        "%, fatigue=" +
                        status.FatigueThreshold.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", fatigue_ratio=" +
                        status.FatigueRemainingRatio.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", zombie_at=" +
                        status.ZombieActivationThreshold.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", zombie_ratio=" +
                        status.ZombieActivationRatio.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", zombie_critic=" +
                        status.ZombieCriticThreshold.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", zombie=" +
                        status.ZombieModeActive +
                        ", working_memory=" +
                        status.WorkingMemoryCount +
                        "/" +
                        status.WorkingMemoryCapacity +
                        ", entropy=" +
                        status.CognitiveEntropyBuffer.ToString("0.000", CultureInfo.InvariantCulture) +
                        ", sleeps=" +
                        status.SleepCycles +
                        ", sleep_lock=" +
                        status.SleepInterruptsLocked +
                        ", last_sleep=" +
                        status.LastSleepTrigger +
                        ", since_sleep_s=" +
                        status.SecondsSinceLastSleep.ToString("0.0", CultureInfo.InvariantCulture) +
                        ", since_activity_s=" +
                        status.SecondsSinceLastActivity.ToString("0.0", CultureInfo.InvariantCulture) +
                        ", processed=" +
                        status.ProcessedInputs);
                    WriteInteractiveLine(
                        "kernel_substrate: physical_cap=" +
                        status.RecommendedPhysicalBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", ram_mb=" +
                        status.AvailableRamMb +
                        "/" +
                        status.TotalRamMb +
                        ", used_est=" +
                        status.UsedRamEstimate +
                        ", cpu_hz=" +
                        status.CpuCycleHz);
                    WriteInteractiveLine("kernel_batch_pending=" + _axBatchController.PendingCount);
                    break;
                }

                case "boot":
                {
                    float fatigueRatio = (float)(args.Count > 2 ? ParseDouble(args[2], 0.28) : 0.28);
                    float zombieRatio = (float)(args.Count > 3 ? ParseDouble(args[3], 0.20) : 0.20);
                    float zombieCritic = (float)(args.Count > 4 ? ParseDouble(args[4], 0.95) : 0.95);
                    float maxOverride = (float)(args.Count > 5 ? ParseDouble(args[5], 0.0) : 0.0);

                    if (maxOverride > 0.0f)
                    {
                        _axKernelLoop.BootSequenceRelative(maxOverride, fatigueRatio, zombieRatio, zombieCritic);
                    }
                    else
                    {
                        _axKernelLoop.BootSequenceFromSubstrate(fatigueRatio, zombieRatio, zombieCritic);
                    }

                    _axBatchController.Clear();
                    KernelLoop.StatusSnapshot bootStatus = _axKernelLoop.GetStatus();

                    WriteInteractiveLine(
                        "kernel_booted: energy=" +
                        bootStatus.MaxEnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", fatigue_ratio=" +
                        fatigueRatio.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", zombie_ratio=" +
                        zombieRatio.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", zombie_critic=" +
                        zombieCritic.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", source=" +
                        (maxOverride > 0.0f ? "manual_override" : "substrate"));
                    break;
                }

                case "ingest":
                {
                    if (args.Count < 5)
                    {
                        WriteInteractiveLine("Usage: kernel ingest <mode> <dataset_id> \"payload\" [dim]");
                        return;
                    }

                    DataStream input = new DataStream
                    {
                        DatasetType = args[2],
                        DatasetId = args[3],
                        Payload = args[4],
                        DimHint = args.Count > 5 ? ParseInt(args[5], ResolveBioCoreDim()) : ResolveBioCoreDim()
                    };

                    IngestResult result = _axKernelLoop.ProcessIngestPipeline(input);
                    PrintKernelIngestResult(input, result);
                    break;
                }

                case "sleep":
                    _axKernelLoop.TriggerSleepCycle(SleepCycleScheduler.SleepTriggerReason.Manual);
                    WriteInteractiveLine("kernel_sleep_cycle_complete: reason=manual");
                    break;

                case "tick":
                {
                    bool idle = true;
                    if (args.Count > 2)
                    {
                        idle = string.Equals(args[2], "idle", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(args[2], "true", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(args[2], "1", StringComparison.OrdinalIgnoreCase);
                    }

                    if (_axKernelLoop.PollSleepScheduler(idle, out SleepCycleScheduler.SleepTriggerReason reason))
                    {
                        WriteInteractiveLine("kernel_tick_sleep_triggered: reason=" + SleepCycleScheduler.FormatReason(reason));
                    }
                    else
                    {
                        WriteInteractiveLine("kernel_tick_no_sleep_trigger");
                    }
                    break;
                }

                case "clearwm":
                    _axKernelLoop.ClearWorkingMemory();
                    WriteInteractiveLine("kernel_working_memory_cleared");
                    break;

                case "batch":
                    HandleKernelBatch(args);
                    break;

                default:
                    WriteInteractiveLine("Unknown kernel command. Type 'kernel help'.");
                    break;
            }
        }

        private void HandleKernelBatch(List<string> args)
        {
            if (args.Count < 3)
            {
                PrintKernelBatchHelp();
                return;
            }

            string sub = args[2].ToLowerInvariant();
            switch (sub)
            {
                case "help":
                    PrintKernelBatchHelp();
                    break;

                case "status":
                    WriteInteractiveLine("kernel_batch_pending=" + _axBatchController.PendingCount);
                    break;

                case "clear":
                    _axBatchController.Clear();
                    WriteInteractiveLine("kernel_batch_cleared");
                    break;

                case "add":
                {
                    if (args.Count < 6)
                    {
                        WriteInteractiveLine("Usage: kernel batch add <mode> <dataset_id> \"payload\" [dim]");
                        return;
                    }

                    DataStream item = new DataStream
                    {
                        DatasetType = args[3],
                        DatasetId = args[4],
                        Payload = args[5],
                        DimHint = args.Count > 6 ? ParseInt(args[6], ResolveBioCoreDim()) : ResolveBioCoreDim()
                    };

                    _axBatchController.Enqueue(item);
                    WriteInteractiveLine(
                        "kernel_batch_enqueued: id=" +
                        item.DatasetId +
                        ", type=" +
                        item.DatasetType +
                        ", pending=" +
                        _axBatchController.PendingCount);
                    break;
                }



                case "run":
                {
                    int maxItems = args.Count > 3 ? ParseInt(args[3], 32) : 32;
                    BatchController.BatchRunResult summary = _axBatchController.Run(_axKernelLoop, maxItems);
                    WriteInteractiveLine(
                        "kernel_batch_run: processed=" +
                        summary.Processed +
                        ", succeeded=" +
                        summary.Succeeded +
                        ", reflex=" +
                        summary.ReflexHits +
                        ", deep=" +
                        summary.DeepThinkHits +
                        ", zombie=" +
                        summary.ZombieEvents +
                        ", sleeps=" +
                        summary.SleepCycles +
                        ", failed=" +
                        summary.Failures +
                        ", pending=" +
                        _axBatchController.PendingCount);
                    break;
                }

                default:
                    WriteInteractiveLine("Unknown kernel batch command. Type 'kernel batch help'.");
                    break;
            }
        }

        private void HandleSynapse(List<string> args)
        {
            if (args.Count < 2)
            {
                PrintSynapseHelp();
                return;
            }

            string sub = args[1].ToLowerInvariant();
            switch (sub)
            {
                case "help":
                    PrintSynapseHelp();
                    break;

                case "train":
                {
                    if (args.Count < 4)
                    {
                        WriteInteractiveLine("Usage: synapse train <hex_string> <intent>");
                        return;
                    }

                    if (!TryParseHexPulse(args[2], out byte[] rawPulse, out string parseError))
                    {
                        WriteInteractiveLine("synapse_train_failed: " + parseError);
                        return;
                    }

                    string intent = args.Count == 4
                        ? args[3]
                        : string.Join(" ", args.GetRange(3, args.Count - 3));

                    HardwareSynapse synapse = GetHardwareSynapse();
                    HardwareSynapse.TrainResult train = synapse.TrainSignal(rawPulse, intent);
                    if (!train.Success)
                    {
                        WriteInteractiveLine("synapse_train_failed: " + (train.Error ?? "unknown_error"));
                        return;
                    }

                    WriteInteractiveLine(
                        "synapse_trained: intent=" +
                        train.Intent +
                        ", reflex_id=" +
                        train.ReflexId +
                        ", bytes=" +
                        train.PulseLength +
                        ", dim=" +
                        train.Dim +
                        ", threshold=" +
                        train.SimilarityThreshold.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", outcome=" +
                        train.Outcome);
                    break;
                }

                case "pulse":
                {
                    if (args.Count < 3)
                    {
                        WriteInteractiveLine("Usage: synapse pulse <hex_string>");
                        return;
                    }

                    if (!TryParseHexPulse(args[2], out byte[] rawPulse, out string parseError))
                    {
                        WriteInteractiveLine("synapse_pulse_failed: " + parseError);
                        return;
                    }

                    HardwareSynapse synapse = GetHardwareSynapse();
                    HardwareSynapse.PulseResult pulse = synapse.ProcessSignal(rawPulse);
                    if (!string.IsNullOrWhiteSpace(pulse.Error))
                    {
                        WriteInteractiveLine("synapse_pulse_failed: " + pulse.Error);
                        return;
                    }

                    if (pulse.Recognized)
                    {
                        WriteInteractiveLine(
                            "synapse_match: intent=" +
                            pulse.Intent +
                            ", reflex_id=" +
                            pulse.ReflexId +
                            ", similarity=" +
                            pulse.Similarity.ToString("0.000", CultureInfo.InvariantCulture) +
                            ", threshold=" +
                            pulse.SimilarityThreshold.ToString("0.000", CultureInfo.InvariantCulture) +
                            ", compared=" +
                            pulse.ComparedReflexes);
                    }
                    else
                    {
                        WriteInteractiveLine(
                            "synapse_unknown: outcome=" +
                            pulse.Outcome +
                            ", best_intent=" +
                            (string.IsNullOrWhiteSpace(pulse.Intent) ? "none" : pulse.Intent) +
                            ", similarity=" +
                            pulse.Similarity.ToString("0.000", CultureInfo.InvariantCulture) +
                            ", threshold=" +
                            pulse.SimilarityThreshold.ToString("0.000", CultureInfo.InvariantCulture) +
                            ", compared=" +
                            pulse.ComparedReflexes);
                    }

                    break;
                }

                case "seed":
                {
                    if (args.Count < 3)
                    {
                        WriteInteractiveLine("Usage: synapse seed keyboard");
                        return;
                    }

                    string profile = args[2].ToLowerInvariant();
                    if (profile != "keyboard")
                    {
                        WriteInteractiveLine("synapse_seed_failed: unknown_profile");
                        return;
                    }

                    HardwareSynapse synapse = GetHardwareSynapse();
                    HardwareSynapse.SeedResult seeded = synapse.TrainStandardKeyboardProfile();
                    WriteInteractiveLine(
                        "synapse_seed_keyboard: requested=" +
                        seeded.Requested +
                        ", trained=" +
                        seeded.Trained +
                        ", failed=" +
                        seeded.Failed);
                    break;
                }

                default:
                    WriteInteractiveLine("Unknown synapse command. Type 'synapse help'.");
                    break;
            }
        }


        private void PrintKernelIngestResult(DataStream input, IngestResult result)
        {
            if (result == null)
            {
                WriteInteractiveLine("kernel_ingest_failed: unknown_error");
                return;
            }

            WriteInteractiveLine(
                "kernel_ingest: id=" +
                (input?.DatasetId ?? string.Empty) +
                ", type=" +
                (input?.DatasetType ?? string.Empty) +
                ", outcome=" +
                result.Outcome +
                ", success=" +
                result.Success +
                ", reflex=" +
                result.ReflexHit +
                ", deep=" +
                result.DeepThinkPath +
                ", zombie=" +
                result.ZombieTriggered +
                ", sleep=" +
                result.SleepTriggered +
                ", iter=" +
                result.Iterations +
                ", sim=" +
                result.Similarity.ToString("0.000", CultureInfo.InvariantCulture));

            WriteInteractiveLine(
                "kernel_profile: label=" +
                result.Profile.Label +
                ", len=" +
                result.Profile.Length +
                ", entropy=" +
                result.Profile.Entropy.ToString("0.000", CultureInfo.InvariantCulture) +
                ", sparsity=" +
                result.Profile.Sparsity.ToString("0.000", CultureInfo.InvariantCulture) +
                ", skew=" +
                result.Profile.Skewness.ToString("0.000", CultureInfo.InvariantCulture) +
                ", s1_thresh=" +
                result.Profile.System1SimilarityThreshold.ToString("0.000", CultureInfo.InvariantCulture) +
                ", critic=" +
                result.Profile.CriticAcceptanceThreshold.ToString("0.000", CultureInfo.InvariantCulture));

            WriteInteractiveLine(
                "kernel_energy_remaining=" +
                result.EnergyRemaining.ToString("0.00", CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                WriteInteractiveLine("kernel_error=" + result.Error);
            }
            if (result.SleepTriggered)
            {
                WriteInteractiveLine("kernel_sleep_trigger_reason=" + (result.SleepReason ?? string.Empty));
            }
        }

        private void PrintKernelLoopHelp()
        {
            WriteInteractiveLine("kernel commands:");
            WriteInteractiveLine("  kernel status");
            WriteInteractiveLine("  kernel boot [fatigue_ratio] [zombie_ratio] [zombie_critic] [max_energy_override]");
            WriteInteractiveLine("  kernel ingest <mode> <dataset_id> \"payload\" [dim]");
            WriteInteractiveLine("  kernel sleep");
            WriteInteractiveLine("  kernel tick [idle|true|false]");
            WriteInteractiveLine("  kernel clearwm");
            WriteInteractiveLine("  kernel batch <subcommand>   (type 'kernel batch help')");
        }

        private void PrintKernelBatchHelp()
        {
            WriteInteractiveLine("kernel batch commands:");
            WriteInteractiveLine("  kernel batch status");
            WriteInteractiveLine("  kernel batch clear");
            WriteInteractiveLine("  kernel batch add <mode> <dataset_id> \"payload\" [dim]");
            WriteInteractiveLine("  kernel batch run [max_items]");
        }

        private void PrintSynapseHelp()
        {
            WriteInteractiveLine("synapse commands:");
            WriteInteractiveLine("  synapse train <hex_string> <intent>");
            WriteInteractiveLine("  synapse pulse <hex_string>");
            WriteInteractiveLine("  synapse seed keyboard");
        }

        private bool TryEncodeText(string text, int requestedDim, out Tensor encoded, out List<string> tokens, out string error)
        {
            encoded = new Tensor();
            error = string.Empty;

            tokens = TokenizeText(text);
            if (tokens.Count == 0)
            {
                tokens.Add("empty");
            }

            int dim = Math.Max(1, requestedDim);
            List<int> positions = new List<int>(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                positions.Add(i % dim);
            }

            bool ok = _hdc.Sequence.EncodeTokens(_hdc.Symbols, tokens, positions, dim, out encoded, out error, out string errorToken);
            if (!ok && !string.IsNullOrEmpty(errorToken))
            {
                error += " (" + errorToken + ")";
            }
            return ok;
        }

        private int ResolveDim()
        {
            int symbolDim = _hdc.Symbols.SymbolDim;
            if (symbolDim > 0)
            {
                return symbolDim;
            }

            int memoryDim = _hdc.Memory.Dimension;
            if (memoryDim > 0)
            {
                return memoryDim;
            }

            return DefaultDim;
        }

        private int ResolveBioCoreDim()
        {
            int symbolDim = _bioCoreHdc.Symbols.SymbolDim;
            if (symbolDim > 0)
            {
                return symbolDim;
            }

            int memoryDim = _bioCoreHdc.Memory.Dimension;
            if (memoryDim > 0)
            {
                return memoryDim;
            }

            return DefaultDim;
        }

        private void PrintHdcStats()
        {
            SymbolSpace.SymbolStats symbolStats = _hdc.Symbols.GetStats();
            ReflexStore.ReflexStats reflexStats = _hdc.Reflexes.GetStats(symbolStats.SymbolDim);

            Console.WriteLine(
                "memory: dim=" + _hdc.Memory.Dimension +
                ", total=" + _hdc.Memory.TotalStored +
                ", step=" + _hdc.Memory.CurrentStep +
                ", levels=" + _hdc.Memory.ActiveLevels + "/" + _hdc.Memory.MaxLevels);
            Console.WriteLine("symbols: count=" + symbolStats.SymbolCount + ", dim=" + symbolStats.SymbolDim);
            Console.WriteLine("reflexes: count=" + reflexStats.Count + ", approx_bytes=" + reflexStats.ApproxBytes);
            if (!string.IsNullOrWhiteSpace(_hdc.MapperStorePath))
            {
                Console.WriteLine("mapper_path: " + _hdc.MapperStorePath);
            }
        }

        private static void PrintRecallResult(EpisodicMemory.RecallResult recall)
        {
            if (!recall.Found)
            {
                Console.WriteLine("recall: no match");
                return;
            }

            Console.WriteLine(
                "recall: source=" + recall.Source +
                ", similarity=" + recall.Similarity.ToString("0.000000", CultureInfo.InvariantCulture) +
                ", stored_step=" + recall.StoredStep +
                ", age_steps=" + recall.AgeSteps +
                ", level=" + recall.Level +
                ", span=" + recall.Span);
            Console.WriteLine("vector=" + TensorPreview(recall.Value));
        }

        private static int ParseInt(string raw, int fallback)
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }
            return fallback;
        }

        private static bool TryParseHexPulse(string raw, out byte[] bytes, out string error)
        {
            bytes = Array.Empty<byte>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "missing_hex_string";
                return false;
            }

            string[] tokens = raw.Split(new[] { ',', ';', '|', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                error = "missing_hex_tokens";
                return false;
            }

            List<byte> parsed = new List<byte>(tokens.Length);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    token = token.Substring(2);
                }

                if (token.Length == 0)
                {
                    continue;
                }

                if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                {
                    error = "invalid_hex_byte:" + tokens[i];
                    return false;
                }

                parsed.Add(value);
            }

            if (parsed.Count == 0)
            {
                error = "no_bytes_parsed";
                return false;
            }

            bytes = parsed.ToArray();
            return true;
        }

        private static double ParseDouble(string raw, double fallback)
        {
            if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }
            return fallback;
        }

        private void InitializeVfs()
        {
            try
            {
                _vfs = new CosmosVFS();
                VFSManager.RegisterVFS(_vfs, true, true);
                _vfsReady = true;
            }
            catch
            {
                _vfsReady = false;
            }
        }

        private bool InitializeHfs(string requestedRoot, out string error)
        {
            error = string.Empty;
            _hfsReady = false;
            if (!_vfsReady)
            {
                error = "vfs_not_ready";
                return false;
            }

            List<string> drives;
            try
            {
                drives = VFSManager.GetLogicalDrives();
            }
            catch
            {
                error = "vfs_drives_unavailable";
                return false;
            }

            if (drives == null || drives.Count == 0)
            {
                error = "no_logical_drives";
                return false;
            }

            string root;
            if (string.IsNullOrWhiteSpace(requestedRoot))
            {
                string lastInitError = "no_writable_drive";
                for (int i = 0; i < drives.Count; i++)
                {
                    string drive = drives[i] ?? string.Empty;
                    if (drive.Length == 0)
                    {
                        continue;
                    }

                    root = CombineDrivePath(drive, "hfs");
                    if (_hfs.Initialize(root, out string tryError))
                    {
                        _hfsReady = true;
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(tryError))
                    {
                        lastInitError = tryError;
                    }
                }

                error = lastInitError;
                return false;
            }

            root = requestedRoot.Trim().Replace('/', '\\');
            if (!IsPathOnKnownDrive(root, drives))
            {
                error = "invalid_drive";
                return false;
            }

            if (!_hfs.Initialize(root, out string initError))
            {
                error = string.IsNullOrWhiteSpace(initError) ? "hfs_init_failed" : initError;
                return false;
            }

            _hfsReady = true;
            return true;
        }

        private void TryInitializeHfsIfNeeded()
        {
            if (_hfsReady || !_vfsReady)
            {
                return;
            }

            InitializeHfs(string.Empty, out _);
        }

        private bool TryResolveMapperPath(string inputPath, out string resolvedPath, out string error)
        {
            resolvedPath = string.Empty;
            error = string.Empty;
            if (!_vfsReady)
            {
                error = "vfs_not_ready";
                return false;
            }

            List<string> drives;
            try
            {
                drives = VFSManager.GetLogicalDrives();
            }
            catch
            {
                error = "vfs_drives_unavailable";
                return false;
            }

            if (drives == null || drives.Count == 0)
            {
                error = "no_logical_drives";
                return false;
            }

            string defaultDrive = drives[0];
            string path = string.IsNullOrWhiteSpace(inputPath) ? string.Empty : inputPath.Trim();
            if (path.Length == 0)
            {
                resolvedPath = CombineDrivePath(defaultDrive, "axos_mapper.bcmap");
                return true;
            }

            if (path.Length >= 2 && path[1] == ':')
            {
                string requestedDrive = path.Substring(0, 2).ToUpperInvariant();
                for (int i = 0; i < drives.Count; i++)
                {
                    string drive = drives[i] ?? string.Empty;
                    if (drive.Length >= 2 && string.CompareOrdinal(drive.Substring(0, 2).ToUpperInvariant(), requestedDrive) == 0)
                    {
                        resolvedPath = path.Replace('/', '\\');
                        return true;
                    }
                }

                error = "invalid_drive";
                return false;
            }

            resolvedPath = CombineDrivePath(defaultDrive, path);
            return true;
        }

        private static string CombineDrivePath(string driveRoot, string relativePath)
        {
            string drive = (driveRoot ?? string.Empty).Replace('/', '\\');
            if (drive.Length == 0)
            {
                drive = "0:\\";
            }
            if (!drive.EndsWith("\\"))
            {
                drive += "\\";
            }

            string rel = (relativePath ?? string.Empty).Replace('/', '\\').TrimStart('\\');
            return rel.Length == 0 ? drive : drive + rel;
        }

        private static bool IsPathOnKnownDrive(string path, List<string> drives)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length < 2 || path[1] != ':')
            {
                return false;
            }

            string requestedDrive = path.Substring(0, 2).ToUpperInvariant();
            for (int i = 0; i < drives.Count; i++)
            {
                string drive = drives[i] ?? string.Empty;
                if (drive.Length >= 2 && string.CompareOrdinal(drive.Substring(0, 2).ToUpperInvariant(), requestedDrive) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ShutdownSystem()
        {
            IOPortWrite qemuDebugExit = new IOPortWrite(0xF4);
            qemuDebugExit.Byte = 0x00;

            while (true)
            {
                CPU.Halt();
            }
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

        private static string TensorPreview(Tensor tensor)
        {
            if (tensor == null || tensor.IsEmpty)
            {
                return "[]";
            }

            int take = Math.Min(PreviewValues, tensor.Total);
            StringBuilder sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < take; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(tensor.Data[i].ToString("0.000", CultureInfo.InvariantCulture));
            }
            if (take < tensor.Total)
            {
                sb.Append(", ...");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static List<string> SplitPipeList(string raw)
        {
            List<string> items = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return items;
            }

            string[] split = raw.Split('|');
            for (int i = 0; i < split.Length; i++)
            {
                string part = split[i].Trim();
                if (part.Length > 0)
                {
                    items.Add(part);
                }
            }
            return items;
        }

        private static List<string> SplitArgs(string line)
        {
            List<string> args = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
            {
                args.Add(current.ToString());
            }

            return args;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("AxOS commands:");
            Console.WriteLine("  help");
            Console.WriteLine("  cls | clear");
            Console.WriteLine("  echo <text>");
            Console.WriteLine("  hdc <subcommand>      (type 'hdc help')");
            Console.WriteLine("  hfs <subcommand>      (type 'hfs help')");
            Console.WriteLine("  save \"intent\" \"content\" [dim]");
            Console.WriteLine("  find \"intent\" [dim]");
            Console.WriteLine("  run \"intent\" [dim]");
            Console.WriteLine("  holopad [dim] [threshold]");
            Console.WriteLine("  mapper <subcommand>   (type 'mapper help')");
            Console.WriteLine("  algo <subcommand>     (type 'algo help')");
            Console.WriteLine("  kernel <subcommand>   (type 'kernel help')");
            Console.WriteLine("  synapse <subcommand>  (type 'synapse help')");
            Console.WriteLine("  appdemo");
            Console.WriteLine("  kerneltest");
            Console.WriteLine("  reboot | shutdown");
        }

        private static void PrintHdcHelp()
        {
            Console.WriteLine("hdc commands:");
            Console.WriteLine("  hdc stats");
            Console.WriteLine("  hdc clear");
            Console.WriteLine("  hdc remember \"text\" [dim]");
            Console.WriteLine("  hdc recall \"text\" [dim]");
            Console.WriteLine("  hdc ago <steps>");
            Console.WriteLine("  hdc symbol <token> [dim]");
            Console.WriteLine("  hdc encode \"text\" [dim]");
            Console.WriteLine("  hdc sim \"text_a\" \"text_b\" [dim]");
            Console.WriteLine("  hdc seq <sequence> [kmer] [stride] [max_kmers] [dim]");
            Console.WriteLine("  hdc promote <reflex_id> <label> [stability] [dim]");
            Console.WriteLine("  hdc query <label> [limit] [min_stability]");
        }

        private static void PrintMapperHelp()
        {
            Console.WriteLine("mapper commands:");
            Console.WriteLine("  mapper save <path>");
            Console.WriteLine("  mapper load <path> [dim]");
        }

        private static void PrintHfsHelp()
        {
            Console.WriteLine("hfs commands:");
            Console.WriteLine("  hfs init [drive_path]");
            Console.WriteLine("  hfs stats");
            Console.WriteLine("  hfs write \"intent\" \"content\" [dim]");
            Console.WriteLine("  hfs read \"intent\" [dim]");
            Console.WriteLine("  hfs search \"intent\" [top_k] [dim]");
            Console.WriteLine("  hfs list [limit]");
        }

        private static void PrintAlgoHelp()
        {
            Console.WriteLine("algo commands:");
            Console.WriteLine("  algo fractal [dim] [offset]");
            Console.WriteLine("  algo vector \"base\" \"add|-\" \"sub|-\" [dim]");
            Console.WriteLine("  algo cosine \"query\" \"cand_a|cand_b|...\" [dim] [top_k]");
            Console.WriteLine("  algo relax \"item_a|item_b|...\" [dim] [iterations]");
            Console.WriteLine("  algo manifold \"train_a|train_b|...\" \"cand_a|cand_b|...\" [dim] [top_k] [iterations]");
        }
    }
}


