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
    public sealed class SequenceEncoder
    {
        public sealed class EncodedManyResult
        {
            public int Rows;
            public int Dim;
            public List<Tensor> Values = new List<Tensor>();
        }

        public sealed class MutateSearchResult
        {
            public int BestIndex = -1;
            public string BestCandidate = string.Empty;
            public double Objective;
            public double TargetScore;
            public double Stability;
            public string PredLabel = "<unknown>";
            public int? Edits;
            public int Dim;
            public int CandidateCount;
        }

        public string NormalizeSequenceText(string rawSequence)
        {
            if (string.IsNullOrEmpty(rawSequence))
            {
                return string.Empty;
            }

            char[] tmp = new char[rawSequence.Length];
            int cursor = 0;
            for (int i = 0; i < rawSequence.Length; i++)
            {
                char c = rawSequence[i];
                if (!char.IsWhiteSpace(c))
                {
                    tmp[cursor++] = char.ToUpperInvariant(c);
                }
            }
            return new string(tmp, 0, cursor);
        }

        public void TokenizeSequenceForHdc(
            string rawSequence,
            int kmerRaw,
            int strideRaw,
            int maxKmersRaw,
            int dim,
            out List<string> tokens,
            out List<int> positions)
        {
            int kmer = Math.Max(2, kmerRaw);
            int stride = Math.Max(1, strideRaw);
            int maxKmers = Math.Max(16, maxKmersRaw);
            int safeDim = Math.Max(1, dim);

            string seq = NormalizeSequenceText(rawSequence);
            tokens = new List<string>();
            positions = new List<int>();

            if (seq.Length < kmer)
            {
                string baseToken = string.IsNullOrEmpty(seq) ? "empty" : seq.ToLowerInvariant();
                tokens.Add("seq:" + baseToken);
                positions.Add(0);
                return;
            }

            int count = 0;
            for (int i = 0; i + kmer <= seq.Length; i += stride)
            {

                char[] chars = new char[kmer];
                for (int j = 0; j < kmer; j++)
                {
                    chars[j] = char.ToLowerInvariant(seq[i + j]);
                }
                tokens.Add("k" + kmer + ":" + new string(chars));
                positions.Add(i % safeDim);

                count++;
                if (count >= maxKmers)
                {
                    break;
                }
            }

            if (tokens.Count == 0)
            {
                tokens.Add("seq:ambiguous");
                positions.Add(0);
            }
        }

        public bool EncodeTokens(
            SymbolSpace symbols,
            IReadOnlyList<string> tokens,
            IReadOnlyList<int> positions,
            int requestedDim,
            out Tensor encoded,
            out string error,
            out string errorToken)
        {
            encoded = new Tensor();
            error = string.Empty;
            errorToken = string.Empty;

            if (tokens == null || tokens.Count == 0)
            {
                error = "missing_tokens";
                return false;
            }

            bool hasPositions = positions != null && positions.Count == tokens.Count;
            if (positions != null && positions.Count > 0 && !hasPositions)
            {
                error = "positions_size_mismatch";
                return false;
            }

            if (!symbols.ResolveTokens(tokens, requestedDim, out List<Tensor> symbolVecs, out error, out errorToken))
            {
                return false;
            }

            int dim = symbols.SymbolDim;
            Tensor acc = new Tensor(new Shape(dim), 0.0f);
            for (int i = 0; i < symbolVecs.Count; i++)
            {
                int steps = i % Math.Max(1, dim);
                if (hasPositions)
                {
                    steps = positions[i];
                }

                Tensor rolled = TensorOps.Permute(symbolVecs[i], steps);
                for (int d = 0; d < dim; d++)
                {
                    acc.Data[d] += rolled.Data[d];
                }
            }

            encoded = TensorOps.NormalizeL2(acc);
            return true;
        }

        public bool EncodeMany(
            SymbolSpace symbols,
            IReadOnlyList<IReadOnlyList<string>> tokenSequences,
            IReadOnlyList<IReadOnlyList<int>> positionRows,
            int requestedDim,
            out EncodedManyResult result,
            out string error,
            out int errorIndex,
            out string errorToken)
        {
            result = new EncodedManyResult();
            error = string.Empty;
            errorToken = string.Empty;
            errorIndex = -1;

            if (tokenSequences == null || tokenSequences.Count == 0)
            {
                error = "missing_token_sequences";
                return false;
            }

            result.Rows = tokenSequences.Count;
            result.Values.Capacity = tokenSequences.Count;

            for (int i = 0; i < tokenSequences.Count; i++)
            {
                IReadOnlyList<int> rowPositions = null;
                if (positionRows != null && i < positionRows.Count)
                {
                    rowPositions = positionRows[i];
                }

                if (!EncodeTokens(symbols, tokenSequences[i], rowPositions, requestedDim, out Tensor encoded, out error, out errorToken))
                {
                    errorIndex = i;
                    return false;
                }

                result.Values.Add(encoded);
            }

            result.Dim = symbols.SymbolDim;
            return true;
        }

        public bool EncodeStringSequences(
            SymbolSpace symbols,
            IReadOnlyList<string> sequences,
            int kmer,
            int stride,
            int maxKmers,
            int requestedDim,
            out EncodedManyResult result,
            out string error,
            out int errorIndex,
            out string errorToken)
        {
            result = new EncodedManyResult();
            error = string.Empty;
            errorToken = string.Empty;
            errorIndex = -1;

            if (sequences == null || sequences.Count == 0)
            {
                error = "missing_sequences";
                return false;
            }

            List<IReadOnlyList<string>> tokenRows = new List<IReadOnlyList<string>>(sequences.Count);
            List<IReadOnlyList<int>> positionRows = new List<IReadOnlyList<int>>(sequences.Count);

            int dim = requestedDim > 0 ? requestedDim : Math.Max(1, symbols.SymbolDim);
            for (int i = 0; i < sequences.Count; i++)
            {
                TokenizeSequenceForHdc(sequences[i], kmer, stride, maxKmers, dim, out List<string> tokens, out List<int> positions);
                tokenRows.Add(tokens);
                positionRows.Add(positions);
            }

            return EncodeMany(symbols, tokenRows, positionRows, requestedDim, out result, out error, out errorIndex, out errorToken);
        }

        public bool EncodeSimilarity(
            SymbolSpace symbols,
            IReadOnlyList<string> tokens,
            IReadOnlyList<int> positions,
            string targetTokenRaw,
            int requestedDim,
            out double similarity,
            out string targetToken,
            out string error,
            out string errorToken)
        {
            similarity = 0.0;
            targetToken = string.Empty;
            error = string.Empty;
            errorToken = string.Empty;

            if (string.IsNullOrWhiteSpace(targetTokenRaw))
            {
                error = "missing_target_token";
                return false;
            }

            if (!symbols.ResolveSymbol(targetTokenRaw, requestedDim, out Tensor targetVec, out error, out targetToken))
            {
                return false;
            }

            if (!EncodeTokens(symbols, tokens, positions, requestedDim, out Tensor encoded, out error, out errorToken))
            {
                return false;
            }

            similarity = TensorOps.CosineSimilarity(encoded, targetVec);
            return true;
        }

        public bool MutateSearch(
            SymbolSpace symbols,
            IReadOnlyList<string> candidates,
            IReadOnlyList<int> edits,
            int kmer,
            int stride,
            int maxKmers,
            Tensor targetProtoRaw,
            Tensor targetVectorRaw,
            double stabilityWeight,
            string targetLabel,
            int requestedDim,
            out MutateSearchResult result,
            out string error,
            out string errorToken)
        {
            result = new MutateSearchResult();
            error = string.Empty;
            errorToken = string.Empty;

            if (candidates == null || candidates.Count == 0)
            {
                error = "missing_candidates";
                return false;
            }

            if (candidates.Count > 20000)
            {
                error = "too_many_candidates";
                return false;
            }

            Tensor targetProto = TensorOps.NormalizeL2(targetProtoRaw.Flatten());
            if (targetProto.IsEmpty)
            {
                error = "empty_target_proto";
                return false;
            }

            bool hasTargetVector = targetVectorRaw != null && !targetVectorRaw.IsEmpty;
            Tensor targetVec = hasTargetVector ? TensorOps.NormalizeL2(targetVectorRaw.Flatten()) : new Tensor();

            double stabilityW = Math.Max(0.0, Math.Min(0.95, stabilityWeight));
            double targetW = 1.0 - stabilityW;
            bool addTargetBonus = !string.IsNullOrEmpty(targetLabel);

            int dim = symbols.SymbolDim;
            if (dim == 0 && requestedDim > 0)
            {
                dim = requestedDim;
            }
            if (dim == 0)
            {
                error = "missing_dim";
                return false;
            }
            if (targetProto.Total != dim)
            {
                error = "target_proto_dim_mismatch";
                return false;
            }
            if (hasTargetVector && targetVec.Total != dim)
            {
                error = "target_vector_dim_mismatch";
                return false;
            }

            int bestIndex = -1;
            double bestObjective = double.NegativeInfinity;
            double bestTargetScore = 0.0;
            double bestStability = 0.0;

            for (int i = 0; i < candidates.Count; i++)
            {
                TokenizeSequenceForHdc(candidates[i], kmer, stride, maxKmers, dim, out List<string> tokens, out List<int> positions);
                if (!EncodeTokens(symbols, tokens, positions, requestedDim, out Tensor encoded, out error, out errorToken))
                {
                    return false;
                }

                double stability = TensorOps.CosineSimilarity(encoded, targetProto);
                double targetScore = hasTargetVector ? TensorOps.CosineSimilarity(encoded, targetVec) : 0.0;
                double objective = targetW * targetScore + stabilityW * stability;
                if (addTargetBonus)
                {
                    objective += 0.05;
                }

                if (objective > bestObjective || bestIndex < 0)
                {
                    bestIndex = i;
                    bestObjective = objective;
                    bestTargetScore = targetScore;
                    bestStability = stability;
                }
            }

            if (bestIndex < 0 || bestIndex >= candidates.Count)
            {
                error = "no_candidate_selected";
                return false;
            }

            result.BestIndex = bestIndex;
            result.BestCandidate = candidates[bestIndex];
            result.Objective = bestObjective;
            result.TargetScore = bestTargetScore;
            result.Stability = bestStability;
            result.PredLabel = string.IsNullOrEmpty(targetLabel) ? "<unknown>" : targetLabel;
            if (edits != null && bestIndex < edits.Count)
            {
                result.Edits = edits[bestIndex];
            }
            result.Dim = dim;
            result.CandidateCount = candidates.Count;
            return true;
        }

    }
}

