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
            public int ScreenWidth = 0;
            public int ScreenHeight = 0;
            public int LogicalWidth = 24;
            public int LogicalHeight = 18;
            public int Dim = 48;
            public double Threshold = 0.002;
            public int TargetFps = 8;
            public int Seed = 1337;
            public bool DebugOverlay = false;
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
            public bool DebugOverlay;
            public int Frame0NonBlackMinY = -1;
            public int Frame0NonBlackMaxY = -1;
            public int Frame0NonBlackBlocks;
            public int Frame0TotalBlocks;
            public double Frame0AvgLuma;
            public double Frame0PeakLuma;
        }

        private sealed class PaletteEntry
        {
            public string Name = string.Empty;
            public Color Color = Color.Black;
            public Tensor Vector = new Tensor();
        }

        private sealed class FrameDebugStats
        {
            public int NonBlackMinY = int.MaxValue;
            public int NonBlackMaxY = -1;
            public int NonBlackBlocks;
            public int TotalBlocks;
            public double LumaSum;
            public double PeakLuma;
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
            int requestedScreenWidth = config.ScreenWidth;
            int requestedScreenHeight = config.ScreenHeight;
            int screenWidth = 0;
            int screenHeight = 0;
            int logicalWidth = Clamp(config.LogicalWidth, 24, 200);
            int logicalHeight = Clamp(config.LogicalHeight, 18, 160);
            int dim = Clamp(config.Dim, 32, 4096);
            int fps = Clamp(config.TargetFps, 1, 30);
            double threshold = Clamp(config.Threshold, -1.0, 1.0);
            int seed = config.Seed;
            bool debugOverlay = config.DebugOverlay;

            Canvas canvas = null;
            DateTime startedUtc = DateTime.UtcNow;
            try
            {
                try
                {
                    if (requestedScreenWidth > 0 && requestedScreenHeight > 0)
                    {
                        int reqW = Clamp(requestedScreenWidth, 160, 1024);
                        int reqH = Clamp(requestedScreenHeight, 120, 768);
                        canvas = FullScreenCanvas.GetFullScreenCanvas(new Mode(reqW, reqH, ColorDepth.ColorDepth32));
                    }
                    else
                    {
                        canvas = FullScreenCanvas.GetFullScreenCanvas();
                    }
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
                FrameDebugStats frame0Debug = null;

                while (renderedFrames < targetFrames)
                {
                    TensorOps.PermuteInPlace(sceneTensor, phase, frameSceneScratch);
                    double frameAvgSimilarity;
                    double framePeakSimilarity;
                    FrameDebugStats currentDebug = renderedFrames == 0 ? new FrameDebugStats() : null;
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
                            phase,
                            canvas,
                            framePen,
                            debugOverlay,
                            currentDebug,
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

                    if (renderedFrames == 0)
                    {
                        frame0Debug = currentDebug;
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
                report.DebugOverlay = debugOverlay;
                if (frame0Debug != null)
                {
                    report.Frame0NonBlackMinY = frame0Debug.NonBlackBlocks > 0 ? frame0Debug.NonBlackMinY : -1;
                    report.Frame0NonBlackMaxY = frame0Debug.NonBlackBlocks > 0 ? frame0Debug.NonBlackMaxY : -1;
                    report.Frame0NonBlackBlocks = frame0Debug.NonBlackBlocks;
                    report.Frame0TotalBlocks = frame0Debug.TotalBlocks;
                    report.Frame0AvgLuma = frame0Debug.TotalBlocks > 0 ? frame0Debug.LumaSum / frame0Debug.TotalBlocks : 0.0;
                    report.Frame0PeakLuma = frame0Debug.PeakLuma;
                }
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

            int wave = (x * 5 + y * 3 + (x ^ y)) % paletteLength;
            if (wave < 0)
            {
                wave += paletteLength;
            }
            return wave;
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
            int phase,
            Canvas canvas,
            Pen pen,
            bool debugOverlay,
            FrameDebugStats debugStats,
            out double averageBestSimilarity,
            out double peakBestSimilarity)
        {
            float[] scene = sceneTensor.Data;
            double sumBest = 0.0;
            double maxBest = -1.0;
            int sampleCount = 0;
            int phaseShift = logicalWidth > 0 ? (phase % logicalWidth) : 0;

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

                    int shiftedX = x + phaseShift;
                    if (shiftedX >= logicalWidth)
                    {
                        shiftedX -= logicalWidth;
                    }

                    int idx = y * logicalWidth + shiftedX;
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
                    if (bestPalette >= 0)
                    {
                        double normalized = Clamp((bestScore + 1.0) * 0.5, 0.0, 1.0);
                        double intensity = bestScore >= threshold
                            ? 0.35 + (0.65 * normalized)
                            : 0.10 + (0.20 * normalized);
                        output = ScaleColor(palette[bestPalette].Color, intensity);
                    }

                    DrawBlock(canvas, pen, x0, y0, x1, y1, output, screenWidth, screenHeight);

                    if (debugStats != null)
                    {
                        double luma = ComputeLuma(output);
                        debugStats.TotalBlocks++;
                        debugStats.LumaSum += luma;
                        if (luma > debugStats.PeakLuma)
                        {
                            debugStats.PeakLuma = luma;
                        }

                        if (output.ToArgb() != Color.Black.ToArgb())
                        {
                            int minY = Clamp(y0, 0, screenHeight - 1);
                            int maxY = Clamp(y1 - 1, 0, screenHeight - 1);
                            if (minY < debugStats.NonBlackMinY)
                            {
                                debugStats.NonBlackMinY = minY;
                            }
                            if (maxY > debugStats.NonBlackMaxY)
                            {
                                debugStats.NonBlackMaxY = maxY;
                            }
                            debugStats.NonBlackBlocks++;
                        }
                    }
                }
            }

            if (debugOverlay)
            {
                DrawDebugOverlay(canvas, pen, screenWidth, screenHeight, debugStats);
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
            int sx1 = Clamp(x1 - 1, 0, screenWidth - 1);
            int sy1 = Clamp(y1 - 1, 0, screenHeight - 1);
            if (sx1 < sx0 || sy1 < sy0)
            {
                return;
            }

            pen.Color = color;
            for (int py = sy0; py <= sy1; py++)
            {
                canvas.DrawLine(pen, sx0, py, sx1, py);
            }
        }

        private static void DrawDebugOverlay(Canvas canvas, Pen pen, int screenWidth, int screenHeight, FrameDebugStats stats)
        {
            if (screenWidth <= 1 || screenHeight <= 1)
            {
                return;
            }

            int maxX = screenWidth - 1;
            int maxY = screenHeight - 1;
            int y25 = maxY / 4;
            int y50 = maxY / 2;
            int y75 = (maxY * 3) / 4;

            pen.Color = Color.White;
            canvas.DrawLine(pen, 0, 0, maxX, 0);
            canvas.DrawLine(pen, 0, maxY, maxX, maxY);
            canvas.DrawLine(pen, 0, 0, 0, maxY);
            canvas.DrawLine(pen, maxX, 0, maxX, maxY);

            pen.Color = Color.FromArgb(255, 64, 64);
            canvas.DrawLine(pen, 0, y25, maxX, y25);
            pen.Color = Color.FromArgb(64, 255, 64);
            canvas.DrawLine(pen, 0, y50, maxX, y50);
            pen.Color = Color.FromArgb(64, 64, 255);
            canvas.DrawLine(pen, 0, y75, maxX, y75);

            if (stats != null && stats.NonBlackBlocks > 0)
            {
                int yMin = Clamp(stats.NonBlackMinY, 0, maxY);
                int yMax = Clamp(stats.NonBlackMaxY, 0, maxY);
                pen.Color = Color.Yellow;
                canvas.DrawLine(pen, 0, yMin, maxX, yMin);
                canvas.DrawLine(pen, 0, yMax, maxX, yMax);
            }
        }

        private static Color ScaleColor(Color color, double intensity)
        {
            int r = Clamp((int)(color.R * intensity), 0, 255);
            int g = Clamp((int)(color.G * intensity), 0, 255);
            int b = Clamp((int)(color.B * intensity), 0, 255);
            return Color.FromArgb(r, g, b);
        }

        private static double ComputeLuma(Color color)
        {
            return ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255.0;
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
