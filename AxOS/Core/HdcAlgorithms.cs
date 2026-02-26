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

namespace AxOS.Core
{
    public static class HdcAlgorithms
    {
        public sealed class HopfieldRelaxResult
        {
            public Tensor Value = new Tensor();
            public float[] Alignments = Array.Empty<float>();
            public int Rows;
            public int Dim;
            public int Iterations;
            public double Momentum;
            public double AlignmentPower;
        }

        public sealed class HopfieldManifoldSearchResult
        {
            public int[] Indices = Array.Empty<int>();
            public float[] Scores = Array.Empty<float>();
            public float[] AllScores = Array.Empty<float>();
            public Tensor Attractor = new Tensor();
            public int TrainRows;
            public int CandidateRows;
            public int Dim;
            public int Iterations;
            public double Momentum;
            public double AlignmentPower;
            public double Temperature;
            public int TopK;
        }

        public sealed class CosineSearchResult
        {
            public int[] Indices = Array.Empty<int>();
            public float[] Scores = Array.Empty<float>();
            public float[] AllScores = Array.Empty<float>();
            public int Rows;
            public int Dim;
            public int TopK;
        }

        public sealed class QuantumAnnealResult
        {
            public int[] BestRoute = Array.Empty<int>();
            public int Cities;
            public int Steps;
            public int Dim;
            public int KPref;
            public double ConstraintWeight;
            public double BaseDistance;
            public double BestDistance;
            public double BestSimilarity;
            public double BestEnergy;
            public int Accepted;
            public int Improved;
            public double ElapsedMs;
            public bool UseHdc;
        }

        public static Tensor VectorMath(Tensor baseTensor, Tensor addTensor, Tensor subTensor, bool normalize = true)
        {
            Tensor baseFlat = baseTensor.Flatten();
            if (baseFlat.IsEmpty)
            {
                throw new ArgumentException("empty_base");
            }

            int dim = baseFlat.Total;
            Tensor addFlat = addTensor == null || addTensor.IsEmpty ? null : addTensor.Flatten();
            Tensor subFlat = subTensor == null || subTensor.IsEmpty ? null : subTensor.Flatten();

            if (addFlat != null && addFlat.Total != dim)
            {
                throw new ArgumentException("add_dim_mismatch");
            }
            if (subFlat != null && subFlat.Total != dim)
            {
                throw new ArgumentException("sub_dim_mismatch");
            }

            Tensor output = new Tensor(new Shape(dim), 0.0f);
            for (int i = 0; i < dim; i++)
            {
                float value = baseFlat.Data[i];
                if (addFlat != null)
                {
                    value += addFlat.Data[i];
                }
                if (subFlat != null)
                {
                    value -= subFlat.Data[i];
                }
                output.Data[i] = value;
            }

            return normalize ? TensorOps.NormalizeL2(output) : output;
        }

        public static HopfieldRelaxResult HopfieldRelax(
            IReadOnlyList<Tensor> vectors,
            int iterations = 5,
            double momentum = 0.9,
            double alignmentPower = 2.0,
            bool normalizeInputs = true)
        {
            if (vectors == null || vectors.Count == 0)
            {
                throw new ArgumentException("missing_vectors");
            }

            int rows = vectors.Count;
            int dim = vectors[0].Flatten().Total;
            if (dim <= 0)
            {
                throw new ArgumentException("empty_vectors");
            }

            float[] flat = new float[rows * dim];
            for (int row = 0; row < rows; row++)
            {
                Tensor vec = vectors[row].Flatten();
                if (vec.Total != dim)
                {
                    throw new ArgumentException("vector_dim_mismatch");
                }
                Array.Copy(vec.Data, 0, flat, row * dim, dim);
            }

            iterations = Math.Max(1, Math.Min(64, iterations));
            momentum = Clamp(momentum, 0.0, 0.999);
            alignmentPower = Clamp(alignmentPower, 1.0, 8.0);
            float momentumF = (float)momentum;
            float invMomentumF = 1.0f - momentumF;

            float[] normed = (float[])flat.Clone();
            if (normalizeInputs)
            {
                float[] rowTmp = new float[dim];
                for (int row = 0; row < rows; row++)
                {
                    int baseOffset = row * dim;
                    Array.Copy(normed, baseOffset, rowTmp, 0, dim);
                    bool ok = NormalizeInPlace(rowTmp);
                    for (int d = 0; d < dim; d++)
                    {
                        normed[baseOffset + d] = ok ? rowTmp[d] : 0.0f;
                    }
                }
            }

            float[] state = new float[dim];
            for (int row = 0; row < rows; row++)
            {
                int baseOffset = row * dim;
                for (int d = 0; d < dim; d++)
                {
                    state[d] += normed[baseOffset + d];
                }
            }
            if (!NormalizeInPlace(state))
            {
                Array.Copy(normed, 0, state, 0, dim);
                NormalizeInPlace(state);
            }

            float[] gradient = new float[dim];
            float[] nextState = new float[dim];
            for (int step = 0; step < iterations; step++)
            {
                Array.Clear(gradient, 0, gradient.Length);
                for (int row = 0; row < rows; row++)
                {
                    int baseOffset = row * dim;
                    double align = 0.0;
                    for (int d = 0; d < dim; d++)
                    {
                        align += state[d] * normed[baseOffset + d];
                    }
                    if (align <= 0.0)
                    {
                        continue;
                    }
                    float weight = (float)Math.Pow(align, alignmentPower);
                    if (weight <= 0.0f)
                    {
                        continue;
                    }
                    for (int d = 0; d < dim; d++)
                    {
                        gradient[d] += normed[baseOffset + d] * weight;
                    }
                }

                if (!NormalizeInPlace(gradient))
                {
                    break;
                }
                for (int d = 0; d < dim; d++)
                {
                    nextState[d] = momentumF * state[d] + invMomentumF * gradient[d];
                }
                if (!NormalizeInPlace(nextState))
                {
                    break;
                }
                Array.Copy(nextState, state, dim);
            }

            float[] alignments = new float[rows];
            for (int row = 0; row < rows; row++)
            {
                int baseOffset = row * dim;
                double align = 0.0;
                for (int d = 0; d < dim; d++)
                {
                    align += state[d] * normed[baseOffset + d];
                }
                alignments[row] = (float)align;
            }

            return new HopfieldRelaxResult
            {
                Value = new Tensor(state),
                Alignments = alignments,
                Rows = rows,
                Dim = dim,
                Iterations = iterations,
                Momentum = momentum,
                AlignmentPower = alignmentPower
            };
        }

        public static HopfieldManifoldSearchResult HopfieldManifoldSearch(
            IReadOnlyList<Tensor> trainVectors,
            IReadOnlyList<Tensor> candidateVectors,
            int iterations = 10,
            double momentum = 0.9,
            double alignmentPower = 2.0,
            double temperature = 0.1,
            int topK = 10,
            bool normalizeInputs = true)
        {
            if (trainVectors == null || trainVectors.Count == 0)
            {
                throw new ArgumentException("invalid_train_vectors");
            }
            if (candidateVectors == null || candidateVectors.Count == 0)
            {
                throw new ArgumentException("invalid_candidate_vectors");
            }

            int trainRows = trainVectors.Count;
            int candidateRows = candidateVectors.Count;
            int dim = trainVectors[0].Flatten().Total;
            if (dim <= 0)
            {
                throw new ArgumentException("empty_train_vectors");
            }

            float[] trainFlat = new float[trainRows * dim];
            for (int row = 0; row < trainRows; row++)
            {
                Tensor v = trainVectors[row].Flatten();
                if (v.Total != dim)
                {
                    throw new ArgumentException("train_dim_mismatch");
                }
                Array.Copy(v.Data, 0, trainFlat, row * dim, dim);
            }

            float[] candidateFlat = new float[candidateRows * dim];
            for (int row = 0; row < candidateRows; row++)
            {
                Tensor v = candidateVectors[row].Flatten();
                if (v.Total != dim)
                {
                    throw new ArgumentException("candidate_dim_mismatch");
                }
                Array.Copy(v.Data, 0, candidateFlat, row * dim, dim);
            }

            iterations = Math.Max(1, Math.Min(128, iterations));
            momentum = Clamp(momentum, 0.0, 0.999);
            alignmentPower = Clamp(alignmentPower, 1.0, 8.0);
            temperature = Clamp(temperature, 1e-3, 10.0);
            topK = topK <= 0 ? candidateRows : Math.Min(Math.Max(1, topK), candidateRows);

            float momentumF = (float)momentum;
            float invMomentumF = 1.0f - momentumF;
            double invTemperature = 1.0 / temperature;

            float[] trainNormed = (float[])trainFlat.Clone();
            if (normalizeInputs)
            {
                float[] rowTmp = new float[dim];
                for (int row = 0; row < trainRows; row++)
                {
                    int baseOffset = row * dim;
                    Array.Copy(trainNormed, baseOffset, rowTmp, 0, dim);
                    bool ok = NormalizeInPlace(rowTmp);
                    for (int d = 0; d < dim; d++)
                    {
                        trainNormed[baseOffset + d] = ok ? rowTmp[d] : 0.0f;
                    }
                }
            }

            float[] state = new float[dim];
            for (int row = 0; row < trainRows; row++)
            {
                int baseOffset = row * dim;
                for (int d = 0; d < dim; d++)
                {
                    state[d] += trainNormed[baseOffset + d];
                }
            }
            if (!NormalizeInPlace(state))
            {
                Array.Copy(trainNormed, 0, state, 0, dim);
                NormalizeInPlace(state);
            }

            float[] gradient = new float[dim];
            float[] nextState = new float[dim];
            for (int step = 0; step < iterations; step++)
            {
                Array.Clear(gradient, 0, gradient.Length);
                for (int row = 0; row < trainRows; row++)
                {
                    int baseOffset = row * dim;
                    double align = 0.0;
                    for (int d = 0; d < dim; d++)
                    {
                        align += state[d] * trainNormed[baseOffset + d];
                    }
                    if (align <= 0.0)
                    {
                        continue;
                    }
                    double scaledAlign = align * invTemperature;
                    if (scaledAlign <= 0.0)
                    {
                        continue;
                    }
                    float weight = (float)Math.Pow(scaledAlign, alignmentPower);
                    if (weight <= 0.0f)
                    {
                        continue;
                    }
                    for (int d = 0; d < dim; d++)
                    {
                        gradient[d] += trainNormed[baseOffset + d] * weight;
                    }
                }

                if (!NormalizeInPlace(gradient))
                {
                    break;
                }
                for (int d = 0; d < dim; d++)
                {
                    nextState[d] = momentumF * state[d] + invMomentumF * gradient[d];
                }
                if (!NormalizeInPlace(nextState))
                {
                    break;
                }
                Array.Copy(nextState, state, dim);
            }

            float[] allScores = new float[candidateRows];
            List<KeyValuePair<int, float>> scored = new List<KeyValuePair<int, float>>(candidateRows);
            for (int row = 0; row < candidateRows; row++)
            {
                int baseOffset = row * dim;
                double normSq = 0.0;
                double dot = 0.0;
                for (int d = 0; d < dim; d++)
                {
                    double v = candidateFlat[baseOffset + d];
                    normSq += v * v;
                    dot += v * state[d];
                }

                float sim = normSq > 1e-20 ? (float)(dot / Math.Sqrt(normSq)) : 0.0f;
                allScores[row] = sim;
                scored.Add(new KeyValuePair<int, float>(row, sim));
            }

            SortPairsByValueDesc(scored);
            int[] indices = new int[topK];
            float[] topScores = new float[topK];
            for (int i = 0; i < topK; i++)
            {
                indices[i] = scored[i].Key;
                topScores[i] = scored[i].Value;
            }

            return new HopfieldManifoldSearchResult
            {
                Indices = indices,
                Scores = topScores,
                AllScores = allScores,
                Attractor = new Tensor(state),
                TrainRows = trainRows,
                CandidateRows = candidateRows,
                Dim = dim,
                Iterations = iterations,
                Momentum = momentum,
                AlignmentPower = alignmentPower,
                Temperature = temperature,
                TopK = topK
            };
        }

        public static CosineSearchResult CosineSearch(Tensor targetVector, IReadOnlyList<Tensor> candidateVectors, int topK = 10)
        {
            Tensor target = TensorOps.NormalizeL2(targetVector.Flatten());
            if (target.IsEmpty)
            {
                throw new ArgumentException("empty_target_vector");
            }
            if (candidateVectors == null || candidateVectors.Count == 0)
            {
                throw new ArgumentException("missing_candidate_vectors");
            }

            int dim = target.Total;
            int rows = candidateVectors.Count;
            topK = topK <= 0 ? rows : Math.Min(Math.Max(1, topK), rows);

            float[] allScores = new float[rows];
            List<KeyValuePair<int, float>> scored = new List<KeyValuePair<int, float>>(rows);
            for (int row = 0; row < rows; row++)
            {
                Tensor cand = candidateVectors[row].Flatten();
                if (cand.Total != dim)
                {
                    throw new ArgumentException("candidate_dim_mismatch");
                }
                double candNormSq = 0.0;
                for (int d = 0; d < dim; d++)
                {
                    double v = cand.Data[d];
                    candNormSq += v * v;
                }

                float sim = 0.0f;
                if (candNormSq > 1e-20)
                {
                    double invNorm = 1.0 / Math.Sqrt(candNormSq);
                    double dot = 0.0;
                    for (int d = 0; d < dim; d++)
                    {
                        dot += cand.Data[d] * invNorm * target.Data[d];
                    }
                    sim = (float)dot;
                }

                allScores[row] = sim;
                scored.Add(new KeyValuePair<int, float>(row, sim));
            }

            SortPairsByValueDesc(scored);
            int[] indices = new int[topK];
            float[] scores = new float[topK];
            for (int i = 0; i < topK; i++)
            {
                indices[i] = scored[i].Key;
                scores[i] = scored[i].Value;
            }

            return new CosineSearchResult
            {
                Indices = indices,
                Scores = scores,
                AllScores = allScores,
                Rows = rows,
                Dim = dim,
                TopK = topK
            };
        }

        public static QuantumAnnealResult QuantumAnneal(
            int cities,
            double[] distanceFlat,
            double[] coordsFlat,
            int[] routeInit,
            int steps,
            double tempStart,
            double tempEnd,
            int seed,
            bool useHdc,
            double constraintWeight,
            int kPref,
            int dim,
            int edgeCacheMax)
        {
            if (cities < 4)
            {
                throw new ArgumentException("invalid_cities");
            }

            int cells = checked(cities * cities);
            double[] dist = BuildDistanceMatrix(cities, distanceFlat, coordsFlat);
            if (dist.Length != cells)
            {
                throw new ArgumentException("distance_build_failed");
            }

            int[] route = BuildRouteInit(cities, routeInit, seed);
            steps = Math.Max(1, steps);
            tempStart = (!double.IsFinite(tempStart) || tempStart <= 0.0) ? 1.0 : tempStart;
            tempEnd = (!double.IsFinite(tempEnd) || tempEnd <= 0.0) ? 0.005 : tempEnd;
            constraintWeight = (!double.IsFinite(constraintWeight) || constraintWeight < 0.0) ? 0.20 : constraintWeight;
            if (!useHdc)
            {
                constraintWeight = 0.0;
            }

            kPref = Math.Max(1, Math.Min(kPref, cities - 1));
            dim = Math.Max(1, dim);
            edgeCacheMax = Math.Max(256, edgeCacheMax);

            Func<int, int, ulong> edgeKey = (a, b) =>
            {
                if (a < 0 || b < 0 || a >= cities || b >= cities)
                {
                    return ulong.MaxValue;
                }
                uint lo = (uint)Math.Min(a, b);
                uint hi = (uint)Math.Max(a, b);
                return ((ulong)lo << 32) | hi;
            };

            Dictionary<ulong, float[]> edgeCache = new Dictionary<ulong, float[]>();
            Queue<ulong> edgeCacheOrder = new Queue<ulong>();
            float edgeScale = 1.0f / (float)Math.Sqrt(dim);

            Func<int, int, float[]> getEdgeVec = (a, b) =>
            {
                ulong key = edgeKey(a, b);
                if (key == ulong.MaxValue)
                {
                    return null;
                }
                if (edgeCache.TryGetValue(key, out float[] existing))
                {
                    return existing;
                }

                float[] vec = new float[dim];
                for (int i = 0; i < dim; i++)
                {
                    ulong z = SplitMix64(key ^ (0x9E3779B97F4A7C15UL * (ulong)(i + 1)));
                    vec[i] = (z & 1UL) == 0UL ? -edgeScale : edgeScale;
                }
                edgeCache[key] = vec;
                edgeCacheOrder.Enqueue(key);
                while (edgeCacheOrder.Count > edgeCacheMax)
                {
                    ulong evict = edgeCacheOrder.Dequeue();
                    edgeCache.Remove(evict);
                }
                return vec;
            };

            float[] constraintVec = new float[dim];
            double constraintNormSq = 0.0;
            if (useHdc)
            {
                List<int> neighbors = new List<int>(cities - 1);
                for (int i = 0; i < cities; i++)
                {
                    neighbors.Clear();
                    for (int j = 0; j < cities; j++)
                    {
                        if (j != i)
                        {
                            neighbors.Add(j);
                        }
                    }
                    SortIntsByDistanceRow(neighbors, dist, i * cities);
                    int keep = Math.Min(kPref, neighbors.Count);
                    for (int rank = 0; rank < keep; rank++)
                    {
                        int j = neighbors[rank];
                        float[] src = getEdgeVec(i, j);
                        if (src == null)
                        {
                            continue;
                        }
                        int repeat = Math.Max(1, keep - rank);
                        for (int rep = 0; rep < repeat; rep++)
                        {
                            for (int d = 0; d < dim; d++)
                            {
                                constraintVec[d] += src[d];
                            }
                        }
                    }
                }
                NormalizeInPlace(constraintVec);
                for (int d = 0; d < dim; d++)
                {
                    constraintNormSq += constraintVec[d] * constraintVec[d];
                }
                if (!double.IsFinite(constraintNormSq) || constraintNormSq <= 1e-12)
                {
                    constraintNormSq = 1.0;
                }
            }

            Func<int[], double> routeDistance = routeCandidate =>
            {
                if (routeCandidate.Length != cities)
                {
                    return double.PositiveInfinity;
                }
                double total = 0.0;
                for (int i = 0; i < cities; i++)
                {
                    int a = routeCandidate[i];
                    int b = routeCandidate[(i + 1) % cities];
                    if (a < 0 || b < 0 || a >= cities || b >= cities)
                    {
                        return double.PositiveInfinity;
                    }
                    total += dist[a * cities + b];
                }
                return total;
            };

            Func<int[], float[]> fillRouteAcc = routeCandidate =>
            {
                float[] acc = new float[dim];
                if (routeCandidate.Length != cities)
                {
                    return null;
                }

                for (int pos = 0; pos < cities; pos++)
                {
                    int a = routeCandidate[pos];
                    int b = routeCandidate[(pos + 1) % cities];
                    float[] src = getEdgeVec(a, b);
                    if (src == null)
                    {
                        return null;
                    }
                    for (int d = 0; d < dim; d++)
                    {
                        acc[d] += src[d];
                    }
                }
                return acc;
            };

            Func<float[], double> simFromAcc = acc =>
            {
                if (!useHdc || acc == null)
                {
                    return 0.0;
                }
                double dot = 0.0;
                double accNormSq = 0.0;
                for (int d = 0; d < dim; d++)
                {
                    double a = acc[d];
                    dot += a * constraintVec[d];
                    accNormSq += a * a;
                }
                double den = Math.Sqrt(Math.Max(1e-12, accNormSq * constraintNormSq));
                return dot / den;
            };

            Func<double, double, double, double> scoreEnergy = (distValue, baseDistance, sim) =>
            {
                double distTerm = distValue / Math.Max(1e-12, baseDistance);
                if (!useHdc)
                {
                    return distTerm;
                }
                double clipped = Clamp(sim, -1.0, 1.0);
                double simPenalty = 1.0 - ((clipped + 1.0) * 0.5);
                return distTerm + (constraintWeight * simPenalty);
            };

            double baseDistance = Math.Max(1e-12, routeDistance(route));
            int[] current = (int[])route.Clone();
            float[] currentAcc = useHdc ? fillRouteAcc(current) : Array.Empty<float>();
            float[] candidateAcc = useHdc ? new float[dim] : Array.Empty<float>();
            double curDist = routeDistance(current);
            if (!double.IsFinite(curDist))
            {
                throw new ArgumentException("invalid_initial_route");
            }
            double curSim = useHdc ? simFromAcc(currentAcc) : 0.0;
            double curEnergy = scoreEnergy(curDist, baseDistance, curSim);

            int[] best = (int[])current.Clone();
            double bestDist = curDist;
            double bestSim = curSim;
            double bestEnergy = curEnergy;

            int accepted = 0;
            int improved = 0;
            Random rng = new Random(seed);
            DateTime start = DateTime.UtcNow;

            for (int step = 1; step <= steps; step++)
            {
                double frac = (double)(step - 1) / Math.Max(1, steps - 1);
                double temp = tempStart * Math.Pow(tempEnd / Math.Max(1e-12, tempStart), frac);

                int i = rng.Next(0, cities);
                int j = rng.Next(0, cities);
                if (i == j)
                {
                    continue;
                }
                if (i > j)
                {
                    int t = i;
                    i = j;
                    j = t;
                }
                if ((j - i) < 2)
                {
                    continue;
                }
                if (i == 0 && j == cities - 1)
                {
                    continue;
                }

                int a = current[(i - 1 + cities) % cities];
                int b = current[i];
                int c = current[j];
                int d2 = current[(j + 1) % cities];

                double removed = dist[a * cities + b] + dist[c * cities + d2];
                double added = dist[a * cities + c] + dist[b * cities + d2];
                double candDist = curDist + (added - removed);

                double candSim = 0.0;
                if (useHdc)
                {
                    float[] ab = getEdgeVec(a, b);
                    float[] cd = getEdgeVec(c, d2);
                    float[] ac = getEdgeVec(a, c);
                    float[] bd = getEdgeVec(b, d2);
                    if (ab == null || cd == null || ac == null || bd == null)
                    {
                        throw new ArgumentException("missing_edge_vector");
                    }

                    double dot = 0.0;
                    double candNormSq = 0.0;
                    for (int k = 0; k < dim; k++)
                    {
                        float value = currentAcc[k] - ab[k] - cd[k] + ac[k] + bd[k];
                        candidateAcc[k] = value;
                        dot += value * constraintVec[k];
                        candNormSq += value * value;
                    }
                    double den = Math.Sqrt(Math.Max(1e-12, candNormSq * constraintNormSq));
                    candSim = dot / den;
                }

                double candEnergy = scoreEnergy(candDist, baseDistance, candSim);
                double delta = candEnergy - curEnergy;
                bool accept = delta <= 0.0;
                if (!accept)
                {
                    double p = Math.Exp(-delta / Math.Max(1e-9, temp));
                    if (rng.NextDouble() < p)
                    {
                        accept = true;
                    }
                }

                if (!accept)
                {
                    continue;
                }

                accepted++;
                Array.Reverse(current, i, (j - i + 1));
                curDist = candDist;
                curSim = candSim;
                curEnergy = candEnergy;
                if (useHdc)
                {
                    float[] swap = currentAcc;
                    currentAcc = candidateAcc;
                    candidateAcc = swap;
                }

                if (curEnergy < bestEnergy)
                {
                    improved++;
                    best = (int[])current.Clone();
                    bestDist = curDist;
                    bestSim = curSim;
                    bestEnergy = curEnergy;
                }
            }

            double elapsedMs = (DateTime.UtcNow - start).TotalMilliseconds;
            return new QuantumAnnealResult
            {
                BestRoute = best,
                Cities = cities,
                Steps = steps,
                Dim = dim,
                KPref = kPref,
                ConstraintWeight = constraintWeight,
                BaseDistance = baseDistance,
                BestDistance = bestDist,
                BestSimilarity = bestSim,
                BestEnergy = bestEnergy,
                Accepted = accepted,
                Improved = improved,
                ElapsedMs = elapsedMs,
                UseHdc = useHdc
            };
        }

        private static bool NormalizeInPlace(float[] vector)
        {
            double normSq = 0.0;
            for (int i = 0; i < vector.Length; i++)
            {
                normSq += vector[i] * vector[i];
            }
            if (normSq <= 1e-20)
            {
                return false;
            }

            float inv = (float)(1.0 / Math.Sqrt(normSq));
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] *= inv;
            }
            return true;
        }

        private static void SortPairsByValueDesc(List<KeyValuePair<int, float>> items)
        {
            for (int i = 1; i < items.Count; i++)
            {
                KeyValuePair<int, float> key = items[i];
                int j = i - 1;
                while (j >= 0 && items[j].Value < key.Value)
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = key;
            }
        }

        private static void SortIntsByDistanceRow(List<int> items, double[] distanceMatrix, int rowBase)
        {
            for (int i = 1; i < items.Count; i++)
            {
                int key = items[i];
                double keyDist = distanceMatrix[rowBase + key];
                int j = i - 1;
                while (j >= 0 && distanceMatrix[rowBase + items[j]] > keyDist)
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = key;
            }
        }

        private static int[] BuildRouteInit(int cities, int[] routeInit, int seed)
        {
            if (routeInit != null && routeInit.Length > 0)
            {
                if (routeInit.Length != cities)
                {
                    throw new ArgumentException("route_size_mismatch");
                }
                int[] seen = new int[cities];
                for (int i = 0; i < cities; i++)
                {
                    int node = routeInit[i];
                    if (node < 0 || node >= cities)
                    {
                        throw new ArgumentException("route_out_of_range");
                    }
                    if (seen[node] != 0)
                    {
                        throw new ArgumentException("route_duplicate_node");
                    }
                    seen[node] = 1;
                }
                return (int[])routeInit.Clone();
            }

            int[] route = new int[cities];
            for (int i = 0; i < cities; i++)
            {
                route[i] = i;
            }
            Random rng = new Random(seed + 101);
            for (int i = route.Length - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                int t = route[i];
                route[i] = route[j];
                route[j] = t;
            }
            return route;
        }

        private static double[] BuildDistanceMatrix(int cities, double[] distanceFlat, double[] coordsFlat)
        {
            int cells = checked(cities * cities);
            if (coordsFlat != null && coordsFlat.Length > 0)
            {
                if (coordsFlat.Length != cities * 2)
                {
                    throw new ArgumentException("coords_size_mismatch");
                }
                double[] dist = new double[cells];
                for (int i = 0; i < cities; i++)
                {
                    double xi = coordsFlat[2 * i];
                    double yi = coordsFlat[2 * i + 1];
                    for (int j = 0; j < cities; j++)
                    {
                        double xj = coordsFlat[2 * j];
                        double yj = coordsFlat[2 * j + 1];
                        double dx = xi - xj;
                        double dy = yi - yj;
                        dist[i * cities + j] = Math.Sqrt(dx * dx + dy * dy);
                    }
                }
                return dist;
            }

            if (distanceFlat == null || distanceFlat.Length != cells)
            {
                throw new ArgumentException("distance_size_mismatch");
            }

            double[] copy = new double[cells];
            Array.Copy(distanceFlat, copy, cells);
            return copy;
        }

        private static ulong SplitMix64(ulong x)
        {
            ulong z = x + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min)
            {
                return min;
            }
            if (v > max)
            {
                return max;
            }
            return v;
        }
    }
}

