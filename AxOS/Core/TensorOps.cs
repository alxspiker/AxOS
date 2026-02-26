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

namespace AxOS.Core
{
    public static class TensorOps
    {
        public const int DefaultHypervectorDim = 1024;

        public static Tensor NullVector(int dim = DefaultHypervectorDim)
        {
            return new Tensor(new Shape(dim), 0.0f);
        }

        public static Tensor IdentityVector(int dim = DefaultHypervectorDim)
        {
            return new Tensor(new Shape(dim), 1.0f);
        }

        public static Tensor RandomHypervector(int dim, ulong seed)
        {
            Tensor outVec = new Tensor(new Shape(dim), 0.0f);
            ulong state = seed;
            for (int i = 0; i < dim; i++)
            {
                state = SplitMix64(state + 0x9E3779B97F4A7C15UL);
                outVec.Data[i] = (state & 1UL) == 0UL ? -1.0f : 1.0f;
            }
            return NormalizeL2(outVec);
        }

        public static Tensor NormalizeL2(Tensor input, float eps = 1e-8f)
        {
            Tensor outVec = input.Copy();
            double sumSq = 0.0;
            for (int i = 0; i < outVec.Data.Length; i++)
            {
                float val = outVec.Data[i];
                // Manual finite check: NaN check (x != x) and Infinity check
                if (val != val || float.IsInfinity(val)) val = 0.0f;
                sumSq += (double)val * val;
            }

            double norm = Math.Sqrt(sumSq);
            if (norm != norm || norm < eps) // NaN check + eps
            {
                outVec.Fill(0.0f);
                return outVec;
            }

            float inv = (float)(1.0 / norm);
            for (int i = 0; i < outVec.Data.Length; i++)
            {
                float val = outVec.Data[i];
                if (val != val || float.IsInfinity(val)) val = 0.0f;
                outVec.Data[i] = val * inv;
            }

            return outVec;
        }

        public static Tensor Subtract(Tensor lhs, Tensor rhs)
        {
            RequireCompatible(lhs, rhs, "subtract");
            Tensor outVec = lhs.Copy();
            for (int i = 0; i < outVec.Data.Length; i++)
            {
                float a = float.IsFinite(lhs.Data[i]) ? lhs.Data[i] : 0.0f;
                float b = float.IsFinite(rhs.Data[i]) ? rhs.Data[i] : 0.0f;
                outVec.Data[i] = a - b;
            }
            return outVec;
        }

        public static Tensor Bind(Tensor lhs, Tensor rhs)
        {
            RequireCompatible(lhs, rhs, "bind");
            Tensor outVec = lhs.Copy();
            for (int i = 0; i < outVec.Data.Length; i++)
            {
                float a = float.IsFinite(lhs.Data[i]) ? lhs.Data[i] : 0.0f;
                float b = float.IsFinite(rhs.Data[i]) ? rhs.Data[i] : 0.0f;
                outVec.Data[i] = a * b;
            }
            return outVec;
        }

        public static Tensor Bundle(Tensor lhs, Tensor rhs, bool normalize = true)
        {
            RequireCompatible(lhs, rhs, "bundle");
            Tensor outVec = lhs.Copy();
            for (int i = 0; i < outVec.Data.Length; i++)
            {
                float a = lhs.Data[i];
                float b = rhs.Data[i];
                if (a != a || float.IsInfinity(a)) a = 0.0f;
                if (b != b || float.IsInfinity(b)) b = 0.0f;
                outVec.Data[i] = a + b;
            }
            return normalize ? NormalizeL2(outVec) : outVec;
        }

        public static Tensor Permute(Tensor input, int steps)
        {
            if (input == null) return null;
            int n = input.Data.Length;
            if (n == 0) return input.Copy();

            int mod = steps % n;
            int shift = mod < 0 ? mod + n : mod;
            if (shift == 0) return input.Copy();

            float[] rotated = new float[n];
            for (int i = 0; i < n; i++)
            {
                int dst = (i + shift) % n;
                rotated[dst] = input.Data[i];
            }
            
            // Re-wrap with original shape to avoid logic "gaslighting"
            return new Tensor(rotated).Reshape(input.Shape);
        }

        public static double CosineSimilarity(Tensor lhs, Tensor rhs, float eps = 1e-8f)
        {
            RequireCompatible(lhs, rhs, "cosine_similarity");
            double dot = 0.0;
            double lhsSq = 0.0;
            double rhsSq = 0.0;
            for (int i = 0; i < lhs.Data.Length; i++)
            {
                float la = lhs.Data[i];
                float ra = rhs.Data[i];
                if (la != la || float.IsInfinity(la)) la = 0.0f;
                if (ra != ra || float.IsInfinity(ra)) ra = 0.0f;

                double a = (double)la;
                double b = (double)ra;
                dot += a * b;
                lhsSq += a * a;
                rhsSq += b * b;
            }

            double lhsNorm = Math.Sqrt(lhsSq);
            double rhsNorm = Math.Sqrt(rhsSq);
            double denom = Math.Max(eps, lhsNorm * rhsNorm);
            
            if (denom == 0 || denom != denom) return 0.0;

            float sim = (float)(dot / denom);
            if (sim != sim) return 0.0;
            if (sim > 1.0f) return 1.0;
            if (sim < -1.0f) return -1.0;
            return sim;
        }

        private static void RequireCompatible(Tensor lhs, Tensor rhs, string opName)
        {
            if (lhs.Total != rhs.Total)
            {
                throw new ArgumentException(opName + ": tensor element count mismatch");
            }
        }

        private static ulong SplitMix64(ulong x)
        {
            ulong z = x + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}

