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
    public sealed class SignalPhaseAligner
    {
        public sealed class InitOptions
        {
            public int WarmupFrames;
            public int NnRampFrames = 64;
            public float NnWeight = 1.0f;
            public float MinNnSimilarity = 0.0f;
            public float LagSmooth = 0.0f;
            public int SearchWindow = 4096;
            public int MaxNeighborAgeFrames = 32;
            public float RecencyBias = 0.15f;
            public bool StrictNn;
            public bool AdaptiveGain = true;
            public float GainAlpha = 0.10f;
            public float GainMin = 0.5f;
            public float GainMax = 1.5f;
            public float Gain = 1.0f;
            public float MaxLagFraction = 0.10f;
            public float EdgeTaper = 0.20f;
        }

        public sealed class StepResult
        {
            public float[] AntiFrame = Array.Empty<float>();
            public double Similarity = 1.0;
            public int Lag;
            public float EffectiveWeight;
            public float AppliedGain;
            public float TargetGain = 1.0f;
            public int FrameIndex;
            public int MemoryCount;
            public int SearchWindow;
            public int NeighborAgeFrames;
        }

        private readonly object _gate = new object();

        private bool _initialized;
        private int _frameSize;
        private int _dim;
        private int _memorySize;
        private int _warmupFrames;
        private int _nnRampFrames;
        private float _nnWeight;
        private float _minNnSimilarity;
        private float _lagSmooth;
        private int _searchWindow;
        private int _maxNeighborAgeFrames;
        private float _recencyBias;
        private bool _strictNn;
        private bool _adaptiveGain;
        private float _gainAlpha;
        private float _gainMin;
        private float _gainMax;
        private float _gain;
        private float _gainState = 1.0f;
        private int _maxLag;
        private float[] _alignWindow = Array.Empty<float>();
        private float[] _stateBank = Array.Empty<float>();
        private float[] _frameBank = Array.Empty<float>();
        private int _count;
        private int _writeIndex;
        private int _frameIndex;
        private int _prevLag;

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

        public int Dim
        {
            get
            {
                lock (_gate)
                {
                    return _dim;
                }
            }
        }

        public int FrameSize
        {
            get
            {
                lock (_gate)
                {
                    return _frameSize;
                }
            }
        }

        public int MemorySize
        {
            get
            {
                lock (_gate)
                {
                    return _memorySize;
                }
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _initialized = false;
                _frameSize = 0;
                _dim = 0;
                _memorySize = 0;
                _warmupFrames = 0;
                _nnRampFrames = 64;
                _nnWeight = 1.0f;
                _minNnSimilarity = 0.0f;
                _lagSmooth = 0.0f;
                _searchWindow = 4096;
                _maxNeighborAgeFrames = 32;
                _recencyBias = 0.15f;
                _strictNn = false;
                _adaptiveGain = true;
                _gainAlpha = 0.10f;
                _gainMin = 0.5f;
                _gainMax = 1.5f;
                _gain = 1.0f;
                _gainState = 1.0f;
                _maxLag = 0;
                _alignWindow = Array.Empty<float>();
                _stateBank = Array.Empty<float>();
                _frameBank = Array.Empty<float>();
                _count = 0;
                _writeIndex = 0;
                _frameIndex = 0;
                _prevLag = 0;
            }
        }

        public bool Initialize(
            SymbolSpace symbols,
            IReadOnlyList<string> bootstrapTokens,
            int dim,
            int frameSize,
            int memorySize,
            InitOptions options,
            int maxDim,
            out string error,
            out string errorToken)
        {
            error = string.Empty;
            errorToken = string.Empty;

            if (dim <= 0 || frameSize <= 0 || memorySize <= 0)
            {
                error = "invalid_signal_phase_dims";
                return false;
            }
            if (frameSize > 32768 || memorySize > 262144)
            {
                error = "signal_phase_size_limit";
                return false;
            }
            if (maxDim > 0 && dim > maxDim)
            {
                error = "hdc_dim_limit_exceeded";
                return false;
            }

            InitOptions cfg = options ?? new InitOptions();

            cfg.NnRampFrames = Math.Max(1, cfg.NnRampFrames);
            cfg.NnWeight = Clamp(cfg.NnWeight, 0.0f, 1.0f);
            cfg.MinNnSimilarity = Clamp(cfg.MinNnSimilarity, 0.0f, 1.0f);
            cfg.LagSmooth = Clamp(cfg.LagSmooth, 0.0f, 0.999f);
            cfg.SearchWindow = Math.Max(1, cfg.SearchWindow);
            cfg.MaxNeighborAgeFrames = Math.Max(0, cfg.MaxNeighborAgeFrames);
            cfg.RecencyBias = Clamp(cfg.RecencyBias, 0.0f, 2.0f);
            cfg.GainAlpha = Clamp(cfg.GainAlpha, 0.0f, 1.0f);
            cfg.GainMin = Math.Max(0.0f, cfg.GainMin);
            cfg.GainMax = Math.Max(cfg.GainMin, cfg.GainMax);
            cfg.MaxLagFraction = Clamp(cfg.MaxLagFraction, 0.0f, 1.0f);
            cfg.EdgeTaper = Clamp(cfg.EdgeTaper, 0.0f, 1.0f);

            if (symbols.SymbolDim != 0 && symbols.SymbolDim != dim)
            {
                error = "symbol_dim_mismatch";
                return false;
            }

            if (bootstrapTokens != null && bootstrapTokens.Count > 200000)
            {
                error = "too_many_tokens";
                return false;
            }

            if (bootstrapTokens != null && bootstrapTokens.Count > 0 &&
                !symbols.ResolveTokens(bootstrapTokens, dim, out _, out error, out errorToken))
            {
                return false;
            }

            long stateElems = (long)memorySize * dim;
            long frameElems = (long)memorySize * frameSize;
            if (stateElems > int.MaxValue || frameElems > int.MaxValue)
            {
                error = "signal_phase_size_overflow";
                return false;
            }

            lock (_gate)
            {
                _initialized = true;
                _frameSize = frameSize;
                _dim = dim;
                _memorySize = memorySize;
                _warmupFrames = Math.Max(0, cfg.WarmupFrames);
                _nnRampFrames = cfg.NnRampFrames;
                _nnWeight = cfg.NnWeight;
                _minNnSimilarity = cfg.MinNnSimilarity;
                _lagSmooth = cfg.LagSmooth;
                _searchWindow = Math.Min(memorySize, cfg.SearchWindow);
                _maxNeighborAgeFrames = Math.Min(_searchWindow, Math.Max(1, cfg.MaxNeighborAgeFrames));
                _recencyBias = cfg.RecencyBias;
                _strictNn = cfg.StrictNn;
                _adaptiveGain = cfg.AdaptiveGain;
                _gainAlpha = cfg.GainAlpha;
                _gainMin = cfg.GainMin;
                _gainMax = cfg.GainMax;
                _gain = cfg.Gain;
                _gainState = 1.0f;
                _maxLag = (int)Math.Min(frameSize > 0 ? frameSize - 1 : 0, Math.Round(frameSize * cfg.MaxLagFraction));
                _alignWindow = BuildTukeyWindow(frameSize, cfg.EdgeTaper);
                _stateBank = new float[(int)stateElems];
                _frameBank = new float[(int)frameElems];
                _count = 0;
                _writeIndex = 0;
                _frameIndex = 0;
                _prevLag = 0;
            }

            return true;
        }

        public bool Step(
            SymbolSpace symbols,
            IReadOnlyList<string> tokens,
            IReadOnlyList<float> frame,
            IReadOnlyList<int> positions,
            out StepResult result,
            out string error,
            out string errorToken)
        {
            result = new StepResult();
            error = string.Empty;
            errorToken = string.Empty;

            if (tokens == null || tokens.Count == 0)
            {
                error = "missing_tokens";
                return false;
            }
            if (tokens.Count > 8192)
            {
                error = "too_many_tokens";
                return false;
            }
            if (frame == null || frame.Count == 0)
            {
                error = "missing_frame";
                return false;
            }

            bool hasPositions = positions != null && positions.Count > 0;
            if (hasPositions && positions.Count != tokens.Count)
            {
                error = "positions_size_mismatch";
                return false;
            }

            int localFrameSize;
            int localDim;
            lock (_gate)
            {
                if (!_initialized)
                {
                    error = "signal_phase_not_initialized";
                    return false;
                }

                if (frame.Count != _frameSize)
                {
                    error = "frame_size_mismatch";
                    return false;
                }

                if (symbols.SymbolDim != _dim)
                {
                    error = "symbol_dim_mismatch";
                    return false;
                }

                localFrameSize = _frameSize;
                localDim = _dim;
            }

            if (!symbols.ResolveTokens(tokens, localDim, out List<Tensor> symbolVecs, out error, out errorToken))
            {
                return false;
            }

            float[] currentFrame = new float[localFrameSize];
            for (int i = 0; i < localFrameSize; i++)
            {
                currentFrame[i] = frame[i];
            }

            Tensor acc = new Tensor(new Shape(localDim), 0.0f);
            for (int i = 0; i < symbolVecs.Count; i++)
            {
                int steps = i % Math.Max(1, localDim);
                if (hasPositions)
                {
                    steps = positions[i];
                }

                Tensor rolled = TensorOps.Permute(symbolVecs[i], steps);
                for (int d = 0; d < localDim; d++)
                {
                    acc.Data[d] += rolled.Data[d];
                }
            }
            Tensor stateTensor = TensorOps.NormalizeL2(acc);
            float[] state = stateTensor.Data;

            lock (_gate)
            {
                float[] templateFrame = _strictNn ? new float[localFrameSize] : (float[])currentFrame.Clone();
                float[] predicted = (float[])templateFrame.Clone();
                float[] aligned = new float[localFrameSize];

                double bestSim = 1.0;
                double bestScore = double.NegativeInfinity;
                int neighborAgeFrames = 0;
                int lag = 0;
                float effectiveWeight = 0.0f;

                if (_count > 0)
                {
                    bestSim = double.NegativeInfinity;
                    int bestIndex = 0;
                    int window = Math.Min(_count, Math.Max(1, _searchWindow));
                    int maxAge = Math.Min(window, Math.Max(1, _maxNeighborAgeFrames));

                    for (int off = 0; off < window; off++)
                    {
                        if (off >= maxAge)
                        {
                            break;
                        }

                        int index = (_writeIndex + _memorySize - 1 - off) % _memorySize;
                        int rowBase = index * localDim;

                        double dot = 0.0;
                        for (int d = 0; d < localDim; d++)
                        {
                            dot += _stateBank[rowBase + d] * state[d];
                        }

                        double ageNorm = (double)off / Math.Max(1, maxAge);
                        double score = dot - (_recencyBias * ageNorm);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestSim = dot;
                            bestIndex = index;
                            neighborAgeFrames = off;
                        }
                    }

                    int bestFrameBase = bestIndex * localFrameSize;
                    lag = BestLagBounded(currentFrame, _frameBank, bestFrameBase, localFrameSize, _maxLag);
                    if (_lagSmooth > 0.0f)
                    {
                        lag = (int)Math.Round((_prevLag * _lagSmooth) + (lag * (1.0f - _lagSmooth)));
                        if (lag > _maxLag)
                        {
                            lag = _maxLag;
                        }
                        else if (lag < -_maxLag)
                        {
                            lag = -_maxLag;
                        }
                    }
                    _prevLag = lag;

                    ShiftWithZeroPad(_frameBank, bestFrameBase, localFrameSize, lag, aligned);
                    for (int i = 0; i < localFrameSize; i++)
                    {
                        aligned[i] *= _alignWindow[i];
                    }

                    float rampFactor = 0.0f;
                    if (_frameIndex >= _warmupFrames)
                    {
                        int rampStep = _frameIndex - _warmupFrames + 1;
                        rampFactor = (float)Math.Min(1.0, (double)rampStep / Math.Max(1, _nnRampFrames));
                    }

                    effectiveWeight = _nnWeight * rampFactor;
                    if (_minNnSimilarity > 0.0f && effectiveWeight > 0.0f)
                    {
                        float denom = Math.Max(1e-6f, 1.0f - _minNnSimilarity);
                        float simFactor = Clamp((float)((bestSim - _minNnSimilarity) / denom), 0.0f, 1.0f);
                        effectiveWeight *= simFactor;
                    }

                    for (int i = 0; i < localFrameSize; i++)
                    {
                        predicted[i] = ((1.0f - effectiveWeight) * templateFrame[i]) + (effectiveWeight * aligned[i]);
                    }
                }

                float[] anti = new float[localFrameSize];
                float appliedGain = _gain;
                float targetGain = 1.0f;

                if (_adaptiveGain)
                {
                    double num = 0.0;
                    double den = 1e-12;
                    for (int i = 0; i < localFrameSize; i++)
                    {
                        num += currentFrame[i] * predicted[i];
                        den += predicted[i] * predicted[i];
                    }
                    targetGain = (float)(num / den);
                    if (!float.IsFinite(targetGain))
                    {
                        targetGain = 1.0f;
                    }
                    targetGain = Clamp(targetGain, _gainMin, _gainMax);
                    _gainState = ((1.0f - _gainAlpha) * _gainState) + (_gainAlpha * targetGain);
                    appliedGain *= _gainState;
                }

                for (int i = 0; i < localFrameSize; i++)
                {
                    anti[i] = -appliedGain * predicted[i];
                }

                int slot = _writeIndex;
                int stateBase = slot * localDim;
                int frameBase = slot * localFrameSize;
                Array.Copy(state, 0, _stateBank, stateBase, localDim);
                Array.Copy(currentFrame, 0, _frameBank, frameBase, localFrameSize);
                _writeIndex = (_writeIndex + 1) % _memorySize;
                if (_count < _memorySize)
                {
                    _count++;
                }
                _frameIndex++;

                result = new StepResult
                {
                    AntiFrame = anti,
                    Similarity = bestSim,
                    Lag = lag,
                    EffectiveWeight = effectiveWeight,
                    AppliedGain = appliedGain,
                    TargetGain = targetGain,
                    FrameIndex = _frameIndex,
                    MemoryCount = _count,
                    SearchWindow = _searchWindow,
                    NeighborAgeFrames = neighborAgeFrames
                };
            }

            return true;
        }

        private static float[] BuildTukeyWindow(int n, float alpha)
        {
            float[] w = new float[n];
            for (int i = 0; i < n; i++)
            {
                w[i] = 1.0f;
            }
            if (n == 0)
            {
                return w;
            }

            float a = Clamp(alpha, 0.0f, 1.0f);
            if (a <= 0.0f)
            {
                return w;
            }

            if (n == 1)
            {
                w[0] = 1.0f;
                return w;
            }

            double pi = Math.PI;
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / (n - 1);
                double v;
                if (a >= 1.0f)
                {
                    v = 0.5 * (1.0 - Math.Cos(2.0 * pi * x));
                }
                else if (x < (a / 2.0))
                {
                    v = 0.5 * (1.0 + Math.Cos(pi * ((2.0 * x / a) - 1.0)));
                }
                else if (x <= (1.0 - (a / 2.0)))
                {
                    v = 1.0;
                }
                else
                {
                    v = 0.5 * (1.0 + Math.Cos(pi * ((2.0 * x / a) - (2.0 / a) + 1.0)));
                }
                w[i] = (float)v;
            }
            return w;
        }

        private static void ShiftWithZeroPad(float[] source, int sourceOffset, int n, int lag, float[] destination)
        {
            Array.Clear(destination, 0, destination.Length);
            if (source == null || n == 0)
            {
                return;
            }

            if (lag >= 0)
            {
                int shift = lag;
                if (shift >= n)
                {
                    return;
                }
                int keep = n - shift;
                Array.Copy(source, sourceOffset, destination, shift, keep);
            }
            else
            {
                int shift = -lag;
                if (shift >= n)
                {
                    return;
                }
                int keep = n - shift;
                Array.Copy(source, sourceOffset + shift, destination, 0, keep);
            }
        }

        private static int BestLagBounded(float[] current, float[] predictedBank, int predictedOffset, int frameSize, int maxLag)
        {
            if (predictedBank == null || frameSize == 0)
            {
                return 0;
            }

            int lagLimit = Math.Min(maxLag, frameSize > 0 ? frameSize - 1 : 0);
            double bestCorr = double.NegativeInfinity;
            int bestLag = 0;
            for (int lag = -lagLimit; lag <= lagLimit; lag++)
            {
                double corr = 0.0;
                if (lag >= 0)
                {
                    int shift = lag;
                    for (int i = shift; i < frameSize; i++)
                    {
                        corr += current[i] * predictedBank[predictedOffset + (i - shift)];
                    }
                }
                else
                {
                    int shift = -lag;
                    int end = frameSize - shift;
                    for (int i = 0; i < end; i++)
                    {
                        corr += current[i] * predictedBank[predictedOffset + (i + shift)];
                    }
                }

                if (corr > bestCorr)
                {
                    bestCorr = corr;
                    bestLag = lag;
                }
            }

            return bestLag;
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
    }
}

