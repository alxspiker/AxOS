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
using Cosmos.System.Graphics;
using System.Drawing;
using Sys = Cosmos.System;

namespace AxOS.Hardware
{
    public sealed class HolographicRenderer
    {
        public sealed class RenderConfig
        {
            public int DurationSeconds = 8;
            public int ScreenWidth = 320;
            public int ScreenHeight = 240;
            public int LogicalWidth = 24;
            public int LogicalHeight = 18;
            public int Dim = 48;
            public double Threshold = 0.002;
            public int TargetFps = 8;
            public int Seed = 1337;
        }

        public sealed class RenderReport
        {
            public int RenderedFrames;
            public int TargetFrames;
            public int EncodedPoints;
            public int Dim;
            public int ScreenWidth;
            public int ScreenHeight;
            public int LogicalWidth;
            public int LogicalHeight;
            public int TargetFps;
            public int DurationSeconds;
            public double Threshold;
            public double AvgBestSimilarity;
            public double PeakBestSimilarity;
            public long ElapsedMilliseconds;
            public bool ExitedByKey;
        }

        private sealed class PaletteEntry
        {
            public string Name = string.Empty;
            public Color Color = Color.Black;
            public Tensor Vector = new Tensor();
        }

        public bool RunDemo(RenderConfig config, out RenderReport report, out string error)
        {
            report = new RenderReport();
            error = string.Empty;

            if (config == null)
            {
                error = "missing_config";
                return false;
            }

            int seconds = Clamp(config.DurationSeconds, 1, 120);
            int screenWidth = Clamp(config.ScreenWidth, 160, 1024);
            int screenHeight = Clamp(config.ScreenHeight, 120, 768);
            int logicalWidth = Clamp(config.LogicalWidth, 24, 200);
            int logicalHeight = Clamp(config.LogicalHeight, 18, 160);
            int dim = Clamp(config.Dim, 32, 4096);
            int fps = Clamp(config.TargetFps, 1, 30);
            double threshold = Clamp(config.Threshold, -1.0, 1.0);
            int seed = config.Seed;

            Canvas canvas = null;
            DateTime startedUtc = DateTime.UtcNow;
            try
            {
                try
                {
                    canvas = FullScreenCanvas.GetFullScreenCanvas();
                }
                catch (Exception ex)
                {
                    error = "graphics_unavailable:" + ex.Message;
                    return false;
                }
                if (canvas == null)
                {
                    error = "graphics_canvas_unavailable";
                    return false;
                }

                try
                {
                    canvas.Mode = new Mode(screenWidth, screenHeight, ColorDepth.ColorDepth32);
                }
                catch (Exception ex)
                {
                    error = "graphics_mode_set_failed:" + ex.Message;
                    return false;
                }

                Mode mode = canvas.Mode;
                if (mode.Columns <= 0 || mode.Rows <= 0)
                {
                    error = "graphics_mode_invalid";
                    return false;
                }
                screenWidth = mode.Columns;
                screenHeight = mode.Rows;
                if (logicalWidth > screenWidth)
                {
                    logicalWidth = screenWidth;
                }
                if (logicalHeight > screenHeight)
                {
                    logicalHeight = screenHeight;
                }

                try
                {
                    canvas.Clear(Color.Black);
                }
                catch
                {
                }

                Tensor[] coordinates = BuildCoordinateField(logicalWidth, logicalHeight, dim, (ulong)(uint)seed);
                PaletteEntry[] palette = BuildPalette(dim, (ulong)(uint)seed);
                Tensor sceneTensor = BuildSceneTensor(logicalWidth, logicalHeight, coordinates, palette, out int encodedPoints);
                Tensor frameSceneScratch = sceneTensor.Copy();
                Pen framePen = new Pen(Color.Black, 1);

                int targetFrames = Math.Max(1, seconds * fps);
                double frameAverageAccumulator = 0.0;
                double peakSimilarity = -1.0;
                int phase = 0;
                int renderedFrames = 0;
                bool exitedByKey = false;

                while (renderedFrames < targetFrames)
                {
                    TensorOps.PermuteInPlace(sceneTensor, phase, frameSceneScratch);
                    double frameAvgSimilarity;
                    double framePeakSimilarity;
                    try
                    {
                        RenderFrame(
                            frameSceneScratch,
                            coordinates,
                            palette,
                            threshold,
                            logicalWidth,
                            logicalHeight,
                            screenWidth,
                            screenHeight,
                            canvas,
                            framePen,
                            out frameAvgSimilarity,
                            out framePeakSimilarity);
                    }
                    catch (Exception ex)
                    {
                        error =
                            "render_failed:draw_stage:frame=" +
                            renderedFrames +
                            ", mode=" +
                            screenWidth +
                            "x" +
                            screenHeight +
                            ", logical=" +
                            logicalWidth +
                            "x" +
                            logicalHeight +
                            ", phase=" +
                            phase +
                            ", detail=" +
                            ex.Message;
                        return false;
                    }

                    try
                    {
                        canvas.Display();
                    }
                    catch (Exception ex)
                    {
                        error =
                            "render_failed:display_stage:frame=" +
                            renderedFrames +
                            ", mode=" +
                            screenWidth +
                            "x" +
                            screenHeight +
                            ", detail=" +
                            ex.Message;
                        return false;
                    }

                    renderedFrames++;
                    frameAverageAccumulator += frameAvgSimilarity;
                    if (framePeakSimilarity > peakSimilarity)
                    {
                        peakSimilarity = framePeakSimilarity;
                    }

                    phase++;
                    if (phase >= dim)
                    {
                        phase = 0;
                    }

                    if (Sys.KeyboardManager.KeyAvailable && Sys.KeyboardManager.TryReadKey(out Sys.KeyEvent keyEvent))
                    {
                        char keyChar = keyEvent.KeyChar;
                        if (keyChar == (char)27 || keyChar == '\r' || keyChar == '\n' || keyChar == 'q' || keyChar == 'Q')
                        {
                            exitedByKey = true;
                            break;
                        }
                    }

                    if ((DateTime.UtcNow - startedUtc).TotalSeconds >= seconds)
                    {
                        break;
                    }
                }

                TimeSpan elapsed = DateTime.UtcNow - startedUtc;
                report.RenderedFrames = renderedFrames;
                report.TargetFrames = targetFrames;
                report.EncodedPoints = encodedPoints;
                report.Dim = dim;
                report.ScreenWidth = screenWidth;
                report.ScreenHeight = screenHeight;
                report.LogicalWidth = logicalWidth;
                report.LogicalHeight = logicalHeight;
                report.TargetFps = fps;
                report.DurationSeconds = seconds;
                report.Threshold = threshold;
                report.AvgBestSimilarity = renderedFrames > 0 ? frameAverageAccumulator / renderedFrames : 0.0;
                report.PeakBestSimilarity = peakSimilarity;
                report.ElapsedMilliseconds = (long)elapsed.TotalMilliseconds;
                report.ExitedByKey = exitedByKey;
                return true;
            }
            catch (Exception ex)
            {
                error = "render_failed:" + ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (canvas != null)
                    {
                        canvas.Disable();
                    }
                }
                catch
                {
                }
            }
        }

        private static Tensor[] BuildCoordinateField(int logicalWidth, int logicalHeight, int dim, ulong seed)
        {
            Tensor xBasis = TensorOps.RandomHypervector(dim, MixSeed(seed, "axis_x"));
            Tensor yBasis = TensorOps.RandomHypervector(dim, MixSeed(seed, "axis_y"));

            Tensor[] xVectors = new Tensor[logicalWidth];
            Tensor[] yVectors = new Tensor[logicalHeight];
            for (int x = 0; x < logicalWidth; x++)
            {
                xVectors[x] = TensorOps.Permute(xBasis, x);
            }
            for (int y = 0; y < logicalHeight; y++)
            {
                yVectors[y] = TensorOps.Permute(yBasis, y);
            }

            Tensor[] field = new Tensor[logicalWidth * logicalHeight];
            int idx = 0;
            for (int y = 0; y < logicalHeight; y++)
            {
                for (int x = 0; x < logicalWidth; x++)
                {
                    Tensor coord = TensorOps.Bind(xVectors[x], yVectors[y]);
                    field[idx++] = TensorOps.NormalizeL2(coord);
                }
            }

            return field;
        }

        private static PaletteEntry[] BuildPalette(int dim, ulong seed)
        {
            return new[]
            {
                CreatePalette("azure", Color.FromArgb(44, 145, 255), TensorOps.RandomHypervector(dim, MixSeed(seed, "azure"))),
                CreatePalette("cyan", Color.FromArgb(0, 206, 201), TensorOps.RandomHypervector(dim, MixSeed(seed, "cyan"))),
                CreatePalette("amber", Color.FromArgb(255, 179, 71), TensorOps.RandomHypervector(dim, MixSeed(seed, "amber"))),
                CreatePalette("magenta", Color.FromArgb(224, 64, 251), TensorOps.RandomHypervector(dim, MixSeed(seed, "magenta"))),
                CreatePalette("lime", Color.FromArgb(120, 224, 95), TensorOps.RandomHypervector(dim, MixSeed(seed, "lime")))
            };
        }

        private static PaletteEntry CreatePalette(string name, Color color, Tensor vector)
        {
            return new PaletteEntry
            {
                Name = name ?? string.Empty,
                Color = color,
                Vector = vector == null ? new Tensor() : vector
            };
        }

        private static Tensor BuildSceneTensor(int logicalWidth, int logicalHeight, Tensor[] coordinates, PaletteEntry[] palette, out int encodedPoints)
        {
            int dim = coordinates.Length > 0 && coordinates[0] != null ? coordinates[0].Total : 0;
            float[] sceneRaw = new float[dim];
            encodedPoints = 0;

            for (int y = 0; y < logicalHeight; y++)
            {
                for (int x = 0; x < logicalWidth; x++)
                {
                    int colorIndex = PickSceneColor(x, y, logicalWidth, logicalHeight, palette.Length);
                    if (colorIndex < 0)
                    {
                        continue;
                    }

                    int idx = y * logicalWidth + x;
                    float[] query = coordinates[idx].Data;
                    float[] colorVec = palette[colorIndex].Vector.Data;
                    for (int d = 0; d < dim; d++)
                    {
                        sceneRaw[d] += query[d] * colorVec[d];
                    }
                    encodedPoints++;
                }
            }

            return TensorOps.NormalizeL2(new Tensor(sceneRaw));
        }

        private static int PickSceneColor(int x, int y, int width, int height, int paletteLength)
        {
            if (paletteLength <= 0)
            {
                return -1;
            }

            int cxA = (width * 30) / 100;
            int cyA = (height * 42) / 100;
            int rA = Math.Max(2, Math.Min(width, height) / 7);

            int dxA = x - cxA;
            int dyA = y - cyA;
            if (dxA * dxA + dyA * dyA <= rA * rA)
            {
                return 0 % paletteLength;
            }

            int cxB = (width * 62) / 100;
            int cyB = (height * 63) / 100;
            int rB = Math.Max(2, Math.Min(width, height) / 8);
            int dxB = x - cxB;
            int dyB = y - cyB;
            if (dxB * dxB + dyB * dyB <= rB * rB)
            {
                return 1 % paletteLength;
            }

            int rectLeft = (width * 54) / 100;
            int rectRight = (width * 88) / 100;
            int rectTop = (height * 14) / 100;
            int rectBottom = (height * 36) / 100;
            if (x >= rectLeft && x <= rectRight && y >= rectTop && y <= rectBottom)
            {
                return 2 % paletteLength;
            }

            int ridge = (height * 74) / 100 + ((x * 7 + width) % 11) - 5;
            if (Math.Abs(y - ridge) <= 1)
            {
                return 3 % paletteLength;
            }

            if (((x + y) % 17) == 0)
            {
                return 4 % paletteLength;
            }

            return -1;
        }

        private static void RenderFrame(
            Tensor sceneTensor,
            Tensor[] coordinates,
            PaletteEntry[] palette,
            double threshold,
            int logicalWidth,
            int logicalHeight,
            int screenWidth,
            int screenHeight,
            Canvas canvas,
            Pen pen,
            out double averageBestSimilarity,
            out double peakBestSimilarity)
        {
            float[] scene = sceneTensor.Data;
            double sumBest = 0.0;
            double maxBest = -1.0;
            int sampleCount = 0;

            for (int y = 0; y < logicalHeight; y++)
            {
                int y0 = (y * screenHeight) / logicalHeight;
                int y1 = ((y + 1) * screenHeight) / logicalHeight;
                if (y1 <= y0)
                {
                    y1 = y0 + 1;
                }
                if (y1 > screenHeight)
                {
                    y1 = screenHeight;
                }

                for (int x = 0; x < logicalWidth; x++)
                {
                    int x0 = (x * screenWidth) / logicalWidth;
                    int x1 = ((x + 1) * screenWidth) / logicalWidth;
                    if (x1 <= x0)
                    {
                        x1 = x0 + 1;
                    }
                    if (x1 > screenWidth)
                    {
                        x1 = screenWidth;
                    }

                    int idx = y * logicalWidth + x;
                    float[] query = coordinates[idx].Data;
                    int bestPalette = -1;
                    double bestScore = -2.0;

                    for (int p = 0; p < palette.Length; p++)
                    {
                        double score = DotBound(scene, query, palette[p].Vector.Data);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestPalette = p;
                        }
                    }

                    sumBest += bestScore;
                    sampleCount++;
                    if (bestScore > maxBest)
                    {
                        maxBest = bestScore;
                    }

                    Color output = Color.Black;
                    if (bestPalette >= 0 && bestScore >= threshold)
                    {
                        double intensity = threshold < 1.0
                            ? Clamp((bestScore - threshold) / (1.0 - threshold), 0.0, 1.0)
                            : 1.0;
                        output = ScaleColor(palette[bestPalette].Color, intensity);
                    }

                    DrawBlock(canvas, pen, x0, y0, x1, y1, output, screenWidth, screenHeight);
                }
            }

            averageBestSimilarity = sampleCount > 0 ? sumBest / sampleCount : 0.0;
            peakBestSimilarity = maxBest;
        }

        private static void DrawBlock(Canvas canvas, Pen pen, int x0, int y0, int x1, int y1, Color color, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return;
            }

            int sx0 = Clamp(x0, 0, screenWidth - 1);
            int sy0 = Clamp(y0, 0, screenHeight - 1);
            int sx1 = Clamp(x1, 0, screenWidth);
            int sy1 = Clamp(y1, 0, screenHeight);
            if (sx1 <= sx0 || sy1 <= sy0)
            {
                return;
            }

            pen.Color = color;
            for (int y = sy0; y < sy1; y++)
            {
                for (int x = sx0; x < sx1; x++)
                {
                    canvas.DrawPoint(pen, x, y);
                }
            }
        }

        private static Color ScaleColor(Color color, double intensity)
        {
            int r = Clamp((int)(color.R * intensity), 0, 255);
            int g = Clamp((int)(color.G * intensity), 0, 255);
            int b = Clamp((int)(color.B * intensity), 0, 255);
            return Color.FromArgb(r, g, b);
        }

        private static double DotBound(float[] scene, float[] query, float[] color)
        {
            double dot = 0.0;
            for (int i = 0; i < scene.Length; i++)
            {
                dot += scene[i] * query[i] * color[i];
            }
            return dot;
        }

        private static ulong MixSeed(ulong seed, string label)
        {
            ulong hash = seed ^ 1469598103934665603UL;
            string text = label ?? string.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static int Clamp(int value, int min, int max)
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

        private static double Clamp(double value, double min, double max)
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
