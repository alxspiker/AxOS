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

namespace AxOS.Hardware
{
    public sealed class HardwareSynapse
    {
        public sealed class SeedResult
        {
            public int Requested;
            public int Trained;
            public int Failed;
        }

        public sealed class TrainResult
        {
            public bool Success;
            public string Error = string.Empty;
            public string Intent = string.Empty;
            public string ReflexId = string.Empty;
            public string Outcome = string.Empty;
            public int PulseLength;
            public int Dim;
            public float SimilarityThreshold;
        }

        public sealed class PulseResult
        {
            public bool Recognized;
            public string Error = string.Empty;
            public string Outcome = string.Empty;
            public string Intent = string.Empty;
            public string ReflexId = string.Empty;
            public float Similarity;
            public float SimilarityThreshold;
            public int ComparedReflexes;
        }

        private readonly HdcSystem _hdc;
        private readonly CognitiveAdapter _adapter;
        private readonly float _recognitionThreshold;
        private readonly int _dim;
        private readonly Dictionary<string, Tensor> _hardwareVectors = new Dictionary<string, Tensor>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _hardwareIntents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _pulseHashToReflexId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _cacheInitialized;

        public HardwareSynapse(HdcSystem hdc, int dim = 8192, float recognitionThreshold = 0.90f)
        {
            _hdc = hdc ?? throw new ArgumentNullException(nameof(hdc));
            _adapter = new CognitiveAdapter(_hdc);
            _dim = dim <= 0 ? 8192 : dim;
            _recognitionThreshold = Clamp(recognitionThreshold, 0.0f, 1.0f);
        }

        public SeedResult TrainStandardKeyboardProfile()
        {
            SeedResult seed = new SeedResult();
            List<KeyValuePair<byte, string>> bindings = GetUsbHidKeyboardBindings();
            for (int i = 0; i < bindings.Count; i++)
            {
                seed.Requested++;
                KeyValuePair<byte, string> binding = bindings[i];
                TrainResult trained = TrainSignal(new[] { binding.Key }, binding.Value);
                if (trained.Success)
                {
                    seed.Trained++;
                }
                else
                {
                    seed.Failed++;
                }
            }

            return seed;
        }

        public TrainResult TrainSignal(byte[] rawPulse, string intent)
        {
            TrainResult result = new TrainResult
            {
                SimilarityThreshold = _recognitionThreshold,
                Dim = _dim
            };

            if (rawPulse == null || rawPulse.Length == 0)
            {
                result.Error = "empty_signal";
                return result;
            }

            string normalizedIntent = NormalizeIntent(intent);
            if (normalizedIntent.Length == 0)
            {
                result.Error = "missing_intent";
                return result;
            }

            DataStream signal = BuildSignalDataStream(rawPulse, "hw_train");
            SignalProfile profile = _adapter.AnalyzeHeuristics(signal);
            if (!_adapter.L2NormalizeAndFlatten(signal, profile, out Tensor encoded, out string encodeError))
            {
                result.Error = string.IsNullOrWhiteSpace(encodeError) ? "encode_failed" : encodeError;
                return result;
            }

            string pulseHash = HashPulse(rawPulse);
            string reflexId = "hw_" + NormalizeReflexToken(normalizedIntent) + "_" + pulseHash;
            Dictionary<string, string> meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "label", normalizedIntent.ToLowerInvariant() },
                { "intent", normalizedIntent },
                { "stability", "1.000" },
                { "source", "hardware_synapse" },
                { "dataset_type", "signal" },
                { "pulse_len", rawPulse.Length.ToString(CultureInfo.InvariantCulture) },
                { "pulse_hex", ToHexPulse(rawPulse) }
            };

            string outcome = _hdc.Reflexes.Promote(reflexId, encoded, meta, true, out string resolvedReflexId);

            string resolved = string.IsNullOrWhiteSpace(resolvedReflexId) ? reflexId : resolvedReflexId;
            _hardwareVectors[resolved] = encoded.Copy();
            _hardwareIntents[resolved] = normalizedIntent;
            _pulseHashToReflexId[pulseHash] = resolved;
            _cacheInitialized = true;

            result.Success = true;
            result.Intent = normalizedIntent;
            result.ReflexId = resolved;
            result.Outcome = string.IsNullOrWhiteSpace(outcome) ? "inserted" : outcome;
            result.PulseLength = rawPulse.Length;
            return result;
        }

        public PulseResult ProcessSignal(byte[] rawPulse)
        {
            PulseResult result = new PulseResult
            {
                SimilarityThreshold = _recognitionThreshold,
                Outcome = "unknown_hardware_noise"
            };

            if (rawPulse == null || rawPulse.Length == 0)
            {
                result.Error = "empty_signal";
                return result;
            }

            DataStream signal = BuildSignalDataStream(rawPulse, "hw_pulse");
            SignalProfile profile = _adapter.AnalyzeHeuristics(signal);
            if (!_adapter.L2NormalizeAndFlatten(signal, profile, out Tensor encoded, out string encodeError))
            {
                result.Error = string.IsNullOrWhiteSpace(encodeError) ? "encode_failed" : encodeError;
                return result;
            }

            EnsureHardwareCacheInitialized();

            string pulseHash = HashPulse(rawPulse);
            if (_pulseHashToReflexId.TryGetValue(pulseHash, out string exactReflexId))
            {
                result.Recognized = true;
                result.Outcome = "recognized_exact";
                result.ReflexId = exactReflexId;
                result.Intent = ResolveIntent(exactReflexId);
                result.Similarity = 1.0f;
                result.ComparedReflexes = 1;
                return result;
            }

            float bestSimilarity = -1.0f;
            string bestIntent = string.Empty;
            string bestReflexId = string.Empty;
            int compared = 0;

            foreach (KeyValuePair<string, Tensor> kv in _hardwareVectors)
            {
                string reflexId = kv.Key;
                Tensor candidate = kv.Value;
                if (candidate == null || candidate.IsEmpty)
                {
                    continue;
                }

                Tensor aligned = AlignToDim(candidate, encoded.Total);
                if (aligned.IsEmpty || aligned.Total != encoded.Total)
                {
                    continue;
                }

                compared++;
                float similarity = (float)TensorOps.CosineSimilarity(encoded, aligned);
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestReflexId = reflexId;
                    bestIntent = ResolveIntent(reflexId);
                }
            }

            result.ComparedReflexes = compared;
            result.Similarity = bestSimilarity < 0.0f ? 0.0f : bestSimilarity;
            result.Intent = bestIntent;
            result.ReflexId = bestReflexId;

            if (bestSimilarity >= _recognitionThreshold && bestIntent.Length > 0)
            {
                result.Recognized = true;
                result.Outcome = "recognized";
            }

            return result;
        }

        private void EnsureHardwareCacheInitialized()
        {
            if (_cacheInitialized)
            {
                return;
            }

            Dictionary<string, ReflexStore.ReflexEntry> snapshot = _hdc.Reflexes.Snapshot();
            foreach (KeyValuePair<string, ReflexStore.ReflexEntry> kv in snapshot)
            {
                ReflexStore.ReflexEntry entry = kv.Value;
                Dictionary<string, string> meta = entry == null ? null : entry.Meta;
                if (!IsHardwareSynapseEntry(meta))
                {
                    continue;
                }

                Tensor vector = entry.Vector;
                if (vector == null || vector.IsEmpty)
                {
                    continue;
                }

                string reflexId = kv.Key ?? string.Empty;
                if (reflexId.Length == 0)
                {
                    continue;
                }

                _hardwareVectors[reflexId] = vector.Copy();
                _hardwareIntents[reflexId] = ResolveIntent(meta, reflexId);
                if (meta != null && meta.TryGetValue("pulse_hex", out string pulseHex) && !string.IsNullOrWhiteSpace(pulseHex))
                {
                    if (TryParsePulseHex(pulseHex, out byte[] parsed))
                    {
                        _pulseHashToReflexId[HashPulse(parsed)] = reflexId;
                    }
                }
            }

            _cacheInitialized = true;
        }

        private DataStream BuildSignalDataStream(byte[] rawPulse, string datasetId)
        {
            return new DataStream
            {
                DatasetType = "numeric",
                DatasetId = datasetId,
                Payload = BuildNumericPayload(rawPulse),
                DimHint = _dim
            };
        }

        private static bool IsHardwareSynapseEntry(Dictionary<string, string> meta)
        {
            if (meta == null)
            {
                return false;
            }

            if (!meta.TryGetValue("source", out string source))
            {
                return false;
            }

            return string.Equals(source, "hardware_synapse", StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveIntent(string reflexId)
        {
            if (!string.IsNullOrWhiteSpace(reflexId) && _hardwareIntents.TryGetValue(reflexId, out string mapped) && !string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }

            return reflexId ?? string.Empty;
        }

        private static string ResolveIntent(Dictionary<string, string> meta, string fallbackReflexId)
        {
            if (meta != null)
            {
                if (meta.TryGetValue("intent", out string intent) && !string.IsNullOrWhiteSpace(intent))
                {
                    return intent.Trim();
                }
                if (meta.TryGetValue("label", out string label) && !string.IsNullOrWhiteSpace(label))
                {
                    return label.Trim();
                }
            }

            return fallbackReflexId ?? string.Empty;
        }

        private static string BuildNumericPayload(byte[] rawPulse)
        {
            StringBuilder sb = new StringBuilder(rawPulse.Length * 4);
            for (int i = 0; i < rawPulse.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(rawPulse[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string ToHexPulse(byte[] rawPulse)
        {
            StringBuilder sb = new StringBuilder(rawPulse.Length * 3);
            for (int i = 0; i < rawPulse.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(rawPulse[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string NormalizeIntent(string intent)
        {
            return string.IsNullOrWhiteSpace(intent) ? string.Empty : intent.Trim();
        }

        private static string NormalizeReflexToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return "unknown";
            }

            StringBuilder sb = new StringBuilder(token.Length);
            string lower = token.ToLowerInvariant();
            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];
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

        private static string HashPulse(byte[] rawPulse)
        {
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < rawPulse.Length; i++)
            {
                hash ^= rawPulse[i];
                hash *= 1099511628211UL;
            }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
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

        private static bool TryParsePulseHex(string pulseHex, out byte[] rawPulse)
        {
            rawPulse = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(pulseHex))
            {
                return false;
            }

            string[] tokens = pulseHex.Split(new[] { ',', ';', '|', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
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

                if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                {
                    return false;
                }

                parsed.Add(value);
            }

            rawPulse = parsed.ToArray();
            return rawPulse.Length > 0;
        }

        private static Tensor AlignToDim(Tensor source, int targetDim)
        {
            if (source == null || source.IsEmpty || targetDim <= 0)
            {
                return new Tensor();
            }

            if (source.Total == targetDim)
            {
                return source;
            }

            Tensor folded = new Tensor(new Shape(targetDim), 0.0f);
            for (int i = 0; i < source.Total; i++)
            {
                int slot = i % targetDim;
                folded.Data[slot] += source.Data[i];
            }

            return TensorOps.NormalizeL2(folded);
        }

        // USB HID Keyboard/Keypad Usage IDs from HID Usage Tables (Keyboard/Keypad Page 0x07).
        private static List<KeyValuePair<byte, string>> GetUsbHidKeyboardBindings()
        {
            return new List<KeyValuePair<byte, string>>
            {
                new KeyValuePair<byte, string>(0x04, "KEY_A"),
                new KeyValuePair<byte, string>(0x05, "KEY_B"),
                new KeyValuePair<byte, string>(0x06, "KEY_C"),
                new KeyValuePair<byte, string>(0x07, "KEY_D"),
                new KeyValuePair<byte, string>(0x08, "KEY_E"),
                new KeyValuePair<byte, string>(0x09, "KEY_F"),
                new KeyValuePair<byte, string>(0x0A, "KEY_G"),
                new KeyValuePair<byte, string>(0x0B, "KEY_H"),
                new KeyValuePair<byte, string>(0x0C, "KEY_I"),
                new KeyValuePair<byte, string>(0x0D, "KEY_J"),
                new KeyValuePair<byte, string>(0x0E, "KEY_K"),
                new KeyValuePair<byte, string>(0x0F, "KEY_L"),
                new KeyValuePair<byte, string>(0x10, "KEY_M"),
                new KeyValuePair<byte, string>(0x11, "KEY_N"),
                new KeyValuePair<byte, string>(0x12, "KEY_O"),
                new KeyValuePair<byte, string>(0x13, "KEY_P"),
                new KeyValuePair<byte, string>(0x14, "KEY_Q"),
                new KeyValuePair<byte, string>(0x15, "KEY_R"),
                new KeyValuePair<byte, string>(0x16, "KEY_S"),
                new KeyValuePair<byte, string>(0x17, "KEY_T"),
                new KeyValuePair<byte, string>(0x18, "KEY_U"),
                new KeyValuePair<byte, string>(0x19, "KEY_V"),
                new KeyValuePair<byte, string>(0x1A, "KEY_W"),
                new KeyValuePair<byte, string>(0x1B, "KEY_X"),
                new KeyValuePair<byte, string>(0x1C, "KEY_Y"),
                new KeyValuePair<byte, string>(0x1D, "KEY_Z"),
                new KeyValuePair<byte, string>(0x1E, "KEY_1"),
                new KeyValuePair<byte, string>(0x1F, "KEY_2"),
                new KeyValuePair<byte, string>(0x20, "KEY_3"),
                new KeyValuePair<byte, string>(0x21, "KEY_4"),
                new KeyValuePair<byte, string>(0x22, "KEY_5"),
                new KeyValuePair<byte, string>(0x23, "KEY_6"),
                new KeyValuePair<byte, string>(0x24, "KEY_7"),
                new KeyValuePair<byte, string>(0x25, "KEY_8"),
                new KeyValuePair<byte, string>(0x26, "KEY_9"),
                new KeyValuePair<byte, string>(0x27, "KEY_0"),
                new KeyValuePair<byte, string>(0x28, "KEY_ENTER"),
                new KeyValuePair<byte, string>(0x29, "KEY_ESCAPE"),
                new KeyValuePair<byte, string>(0x2A, "KEY_BACKSPACE"),
                new KeyValuePair<byte, string>(0x2B, "KEY_TAB"),
                new KeyValuePair<byte, string>(0x2C, "KEY_SPACE"),
                new KeyValuePair<byte, string>(0x2D, "KEY_MINUS"),
                new KeyValuePair<byte, string>(0x2E, "KEY_EQUAL"),
                new KeyValuePair<byte, string>(0x2F, "KEY_LEFT_BRACKET"),
                new KeyValuePair<byte, string>(0x30, "KEY_RIGHT_BRACKET"),
                new KeyValuePair<byte, string>(0x31, "KEY_BACKSLASH"),
                new KeyValuePair<byte, string>(0x32, "KEY_NON_US_HASH"),
                new KeyValuePair<byte, string>(0x33, "KEY_SEMICOLON"),
                new KeyValuePair<byte, string>(0x34, "KEY_APOSTROPHE"),
                new KeyValuePair<byte, string>(0x35, "KEY_GRAVE"),
                new KeyValuePair<byte, string>(0x36, "KEY_COMMA"),
                new KeyValuePair<byte, string>(0x37, "KEY_PERIOD"),
                new KeyValuePair<byte, string>(0x38, "KEY_SLASH"),
                new KeyValuePair<byte, string>(0x39, "KEY_CAPS_LOCK"),
                new KeyValuePair<byte, string>(0x3A, "KEY_F1"),
                new KeyValuePair<byte, string>(0x3B, "KEY_F2"),
                new KeyValuePair<byte, string>(0x3C, "KEY_F3"),
                new KeyValuePair<byte, string>(0x3D, "KEY_F4"),
                new KeyValuePair<byte, string>(0x3E, "KEY_F5"),
                new KeyValuePair<byte, string>(0x3F, "KEY_F6"),
                new KeyValuePair<byte, string>(0x40, "KEY_F7"),
                new KeyValuePair<byte, string>(0x41, "KEY_F8"),
                new KeyValuePair<byte, string>(0x42, "KEY_F9"),
                new KeyValuePair<byte, string>(0x43, "KEY_F10"),
                new KeyValuePair<byte, string>(0x44, "KEY_F11"),
                new KeyValuePair<byte, string>(0x45, "KEY_F12"),
                new KeyValuePair<byte, string>(0x46, "KEY_PRINT_SCREEN"),
                new KeyValuePair<byte, string>(0x47, "KEY_SCROLL_LOCK"),
                new KeyValuePair<byte, string>(0x48, "KEY_PAUSE"),
                new KeyValuePair<byte, string>(0x49, "KEY_INSERT"),
                new KeyValuePair<byte, string>(0x4A, "KEY_HOME"),
                new KeyValuePair<byte, string>(0x4B, "KEY_PAGE_UP"),
                new KeyValuePair<byte, string>(0x4C, "KEY_DELETE"),
                new KeyValuePair<byte, string>(0x4D, "KEY_END"),
                new KeyValuePair<byte, string>(0x4E, "KEY_PAGE_DOWN"),
                new KeyValuePair<byte, string>(0x4F, "KEY_RIGHT_ARROW"),
                new KeyValuePair<byte, string>(0x50, "KEY_LEFT_ARROW"),
                new KeyValuePair<byte, string>(0x51, "KEY_DOWN_ARROW"),
                new KeyValuePair<byte, string>(0x52, "KEY_UP_ARROW"),
                new KeyValuePair<byte, string>(0x53, "KEY_NUM_LOCK"),
                new KeyValuePair<byte, string>(0x54, "KEY_KP_DIVIDE"),
                new KeyValuePair<byte, string>(0x55, "KEY_KP_MULTIPLY"),
                new KeyValuePair<byte, string>(0x56, "KEY_KP_SUBTRACT"),
                new KeyValuePair<byte, string>(0x57, "KEY_KP_ADD"),
                new KeyValuePair<byte, string>(0x58, "KEY_KP_ENTER"),
                new KeyValuePair<byte, string>(0x59, "KEY_KP_1"),
                new KeyValuePair<byte, string>(0x5A, "KEY_KP_2"),
                new KeyValuePair<byte, string>(0x5B, "KEY_KP_3"),
                new KeyValuePair<byte, string>(0x5C, "KEY_KP_4"),
                new KeyValuePair<byte, string>(0x5D, "KEY_KP_5"),
                new KeyValuePair<byte, string>(0x5E, "KEY_KP_6"),
                new KeyValuePair<byte, string>(0x5F, "KEY_KP_7"),
                new KeyValuePair<byte, string>(0x60, "KEY_KP_8"),
                new KeyValuePair<byte, string>(0x61, "KEY_KP_9"),
                new KeyValuePair<byte, string>(0x62, "KEY_KP_0"),
                new KeyValuePair<byte, string>(0x63, "KEY_KP_DECIMAL"),
                new KeyValuePair<byte, string>(0x64, "KEY_NON_US_BACKSLASH"),
                new KeyValuePair<byte, string>(0x65, "KEY_APPLICATION"),
                new KeyValuePair<byte, string>(0x66, "KEY_POWER"),
                new KeyValuePair<byte, string>(0x67, "KEY_KP_EQUAL"),
                new KeyValuePair<byte, string>(0xE0, "KEY_LEFT_CTRL"),
                new KeyValuePair<byte, string>(0xE1, "KEY_LEFT_SHIFT"),
                new KeyValuePair<byte, string>(0xE2, "KEY_LEFT_ALT"),
                new KeyValuePair<byte, string>(0xE3, "KEY_LEFT_GUI"),
                new KeyValuePair<byte, string>(0xE4, "KEY_RIGHT_CTRL"),
                new KeyValuePair<byte, string>(0xE5, "KEY_RIGHT_SHIFT"),
                new KeyValuePair<byte, string>(0xE6, "KEY_RIGHT_ALT"),
                new KeyValuePair<byte, string>(0xE7, "KEY_RIGHT_GUI")
            };
        }
    }
}

