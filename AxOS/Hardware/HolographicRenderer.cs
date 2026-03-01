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
using System.Collections.Generic;
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
            public bool MouseOverlay = false;
            public bool MouseOnly = false;
            public bool PatternOverlay = false;
            public bool ForceVga8 = false;
            public bool ForceSvga = false;
            public bool BlueSquare = false;
            public int ColorDepthBits = 32;
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
            public bool MouseOverlay;
            public bool MouseOnly;
            public bool PatternOverlay;
            public bool ForceVga8;
            public bool ForceSvga;
            public bool BlueSquare;
            public int ColorDepthBits;
            public string CanvasBackend = string.Empty;
            public bool DisplayFlipUsed;
            public int Frame0NonBlackMinY = -1;
            public int Frame0NonBlackMaxY = -1;
            public int Frame0NonBlackBlocks;
            public int Frame0TotalBlocks;
            public double Frame0AvgLuma;
            public double Frame0PeakLuma;
            public int MouseSamples;
            public int MouseStartX = -1;
            public int MouseStartY = -1;
            public int MouseEndX = -1;
            public int MouseEndY = -1;
            public int MouseMinX = -1;
            public int MouseMaxX = -1;
            public int MouseMinY = -1;
            public int MouseMaxY = -1;
            public bool PatternProbeValid;
            public int PatternProbeTopArgb;
            public int PatternProbeMidArgb;
            public int PatternProbeBottomArgb;
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

        private sealed class MouseDebugStats
        {
            public int Samples;
            public int StartX = -1;
            public int StartY = -1;
            public int EndX = -1;
            public int EndY = -1;
            public int MinX = int.MaxValue;
            public int MaxX = -1;
            public int MinY = int.MaxValue;
            public int MaxY = -1;
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
            bool mouseOverlay = config.MouseOverlay;
            bool mouseOnly = config.MouseOnly;
            bool patternOverlay = config.PatternOverlay;
            bool forceVga8 = config.ForceVga8;
            bool forceSvga = config.ForceSvga;
            bool blueSquare = config.BlueSquare;
            int requestedDepthBits = NormalizeDepthBits(config.ColorDepthBits);

            Canvas canvas = null;
            DateTime startedUtc = DateTime.UtcNow;
            try
            {
                if (forceVga8 && forceSvga)
                {
                    error = "invalid_config:svga_and_vga8_are_mutually_exclusive";
                    return false;
                }

                string canvasBackend = string.Empty;
                int reqW;
                int reqH;
                ColorDepth reqDepth;
                if (forceVga8)
                {
                    reqW = 320;
                    reqH = 200;
                    reqDepth = ColorDepth.ColorDepth8;
                }
                else
                {
                    reqW = requestedScreenWidth > 0 ? Clamp(requestedScreenWidth, 160, 1024) : 640;
                    reqH = requestedScreenHeight > 0 ? Clamp(requestedScreenHeight, 120, 768) : 480;
                    reqDepth = BitsToColorDepth(requestedDepthBits);
                }

                Mode requestedMode = new Mode((uint)reqW, (uint)reqH, reqDepth);
                try
                {
                    if (forceVga8)
                    {
                        canvas = new VGACanvas(requestedMode);
                        canvasBackend = "VGACanvas";
                    }
                    else if (forceSvga)
                    {
                        canvas = new SVGAIICanvas(requestedMode);
                        canvasBackend = "SVGAIICanvas";
                    }
                    else
                    {
                        canvas = FullScreenCanvas.GetFullScreenCanvas(requestedMode);
                        canvasBackend = canvas == null ? "FullScreenCanvas" : canvas.GetType().Name;
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
                bool useDisplayFlip = ShouldUseDisplayFlip(canvasBackend);
                if (forceSvga)
                {
                    // Explicit SVGA mode still faults on Display() in this stack.
                    useDisplayFlip = false;
                }
                bool useVgaDirectPath =
                    canvasBackend.IndexOf("VGA", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    canvasBackend.IndexOf("SVGA", StringComparison.OrdinalIgnoreCase) < 0;

                Mode mode = canvas.Mode;
                if (mode.Width <= 0 || mode.Height <= 0)
                {
                    error = "graphics_mode_invalid";
                    return false;
                }
                screenWidth = (int)mode.Width;
                screenHeight = (int)mode.Height;
                if (logicalWidth > screenWidth)
                {
                    logicalWidth = screenWidth;
                }
                if (logicalHeight > screenHeight)
                {
                    logicalHeight = screenHeight;
                }

                TryConfigureMouse(screenWidth, screenHeight);

                try
                {
                    canvas.Clear(Color.Black);
                }
                catch
                {
                }

                Tensor[] coordinates = null;
                PaletteEntry[] palette = null;
                Tensor sceneTensor = null;
                Tensor frameSceneScratch = null;
                int encodedPoints = 0;
                if (!mouseOnly)
                {
                    coordinates = BuildCoordinateField(logicalWidth, logicalHeight, dim, (ulong)(uint)seed);
                    palette = BuildPalette(dim, (ulong)(uint)seed);
                    sceneTensor = BuildSceneTensor(logicalWidth, logicalHeight, coordinates, palette, out encodedPoints);
                    frameSceneScratch = sceneTensor.Copy();
                }
                Bitmap mouseFrame = null;
                int[] mousePixels = null;
                bool usePixelBufferPath = mouseOnly && !useVgaDirectPath;
                if (usePixelBufferPath)
                {
                    mouseFrame = new Bitmap((uint)screenWidth, (uint)screenHeight, ColorDepth.ColorDepth32);
                    mousePixels = mouseFrame.RawData;
                }

                int targetFrames = Math.Max(1, seconds * fps);
                double frameAverageAccumulator = 0.0;
                double peakSimilarity = -1.0;
                int phase = 0;
                int renderedFrames = 0;
                bool exitedByKey = false;
                FrameDebugStats frame0Debug = null;
                MouseDebugStats mouseStats = new MouseDebugStats();
                bool patternProbeCaptured = false;
                int patternTopArgb = 0;
                int patternMidArgb = 0;
                int patternBottomArgb = 0;

                while (renderedFrames < targetFrames)
                {
                    double frameAvgSimilarity;
                    double framePeakSimilarity;
                    FrameDebugStats currentDebug = renderedFrames == 0 ? new FrameDebugStats() : null;
                    try
                    {
                        bool renderedToPixelBuffer = false;
                        if (mouseOnly)
                        {
                            if (usePixelBufferPath && mousePixels != null)
                            {
                                RenderMouseFramePixels(
                                    mousePixels,
                                    screenWidth,
                                    screenHeight,
                                    phase,
                                    debugOverlay,
                                    patternOverlay,
                                    currentDebug,
                                    out frameAvgSimilarity,
                                    out framePeakSimilarity);
                                renderedToPixelBuffer = true;
                            }
                            else
                            {
                                RenderMouseFrame(
                                    canvas,
                                    screenWidth,
                                    screenHeight,
                                    phase,
                                    debugOverlay,
                                    patternOverlay,
                                    currentDebug,
                                    out frameAvgSimilarity,
                                    out framePeakSimilarity);
                            }
                        }
                        else
                        {
                            TensorOps.PermuteInPlace(sceneTensor, phase, frameSceneScratch);
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
                                debugOverlay,
                                currentDebug,
                                out frameAvgSimilarity,
                                out framePeakSimilarity);
                        }

                        if (blueSquare)
                        {
                            if (renderedToPixelBuffer && mousePixels != null)
                            {
                                DrawCenteredBlueSquarePixels(mousePixels, screenWidth, screenHeight);
                            }
                            else
                            {
                                DrawCenteredBlueSquare(canvas, screenWidth, screenHeight);
                            }
                        }

                        if (mouseOverlay)
                        {
                            if (TryReadMouse(screenWidth, screenHeight, out int mouseX, out int mouseY, out Sys.MouseState mouseState))
                            {
                                AccumulateMouseStats(mouseStats, mouseX, mouseY);
                                if (renderedToPixelBuffer && mousePixels != null)
                                {
                                    DrawMouseOverlayPixels(mousePixels, screenWidth, screenHeight, mouseX, mouseY, mouseState);
                                }
                                else
                                {
                                    DrawMouseOverlay(canvas, screenWidth, screenHeight, mouseX, mouseY, mouseState);
                                }
                            }
                        }

                        if (renderedToPixelBuffer && mouseFrame != null)
                        {
                            if (!patternProbeCaptured && mousePixels != null)
                            {
                                patternProbeCaptured = TrySamplePatternPixelsFromBuffer(mousePixels, screenWidth, screenHeight, out patternTopArgb, out patternMidArgb, out patternBottomArgb);
                            }
                            canvas.DrawImage(mouseFrame, 0, 0);
                        }
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
                            ", backend=" +
                            canvasBackend +
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

                    if (useDisplayFlip)
                    {
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
                            ", backend=" +
                            canvasBackend +
                            ", detail=" +
                            ex.Message;
                            return false;
                        }
                    }
                    if (!patternProbeCaptured)
                    {
                        // Always probe the post-present canvas once so serial logs reflect what the
                        // active backend reports at key screen points.
                        patternProbeCaptured = TrySamplePatternPixels(canvas, screenWidth, screenHeight, out patternTopArgb, out patternMidArgb, out patternBottomArgb);
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
                report.MouseOverlay = mouseOverlay;
                report.MouseOnly = mouseOnly;
                report.PatternOverlay = patternOverlay;
                report.ForceVga8 = forceVga8;
                report.ForceSvga = forceSvga;
                report.BlueSquare = blueSquare;
                report.ColorDepthBits = ColorDepthToBits(mode.ColorDepth);
                report.CanvasBackend = string.IsNullOrEmpty(canvasBackend) ? (canvas == null ? "unknown" : canvas.GetType().Name) : canvasBackend;
                report.DisplayFlipUsed = useDisplayFlip;
                if (frame0Debug != null)
                {
                    report.Frame0NonBlackMinY = frame0Debug.NonBlackBlocks > 0 ? frame0Debug.NonBlackMinY : -1;
                    report.Frame0NonBlackMaxY = frame0Debug.NonBlackBlocks > 0 ? frame0Debug.NonBlackMaxY : -1;
                    report.Frame0NonBlackBlocks = frame0Debug.NonBlackBlocks;
                    report.Frame0TotalBlocks = frame0Debug.TotalBlocks;
                    report.Frame0AvgLuma = frame0Debug.TotalBlocks > 0 ? frame0Debug.LumaSum / frame0Debug.TotalBlocks : 0.0;
                    report.Frame0PeakLuma = frame0Debug.PeakLuma;
                }
                if (mouseStats.Samples > 0)
                {
                    report.MouseSamples = mouseStats.Samples;
                    report.MouseStartX = mouseStats.StartX;
                    report.MouseStartY = mouseStats.StartY;
                    report.MouseEndX = mouseStats.EndX;
                    report.MouseEndY = mouseStats.EndY;
                    report.MouseMinX = mouseStats.MinX;
                    report.MouseMaxX = mouseStats.MaxX;
                    report.MouseMinY = mouseStats.MinY;
                    report.MouseMaxY = mouseStats.MaxY;
                }
                report.PatternProbeValid = patternProbeCaptured;
                report.PatternProbeTopArgb = patternTopArgb;
                report.PatternProbeMidArgb = patternMidArgb;
                report.PatternProbeBottomArgb = patternBottomArgb;
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

                    DrawBlock(canvas, x0, y0, x1, y1, output, screenWidth, screenHeight);

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
                DrawDebugOverlay(canvas, screenWidth, screenHeight, debugStats);
            }

            averageBestSimilarity = sampleCount > 0 ? sumBest / sampleCount : 0.0;
            peakBestSimilarity = maxBest;
        }

        private static void DrawBlock(Canvas canvas, int x0, int y0, int x1, int y1, Color color, int screenWidth, int screenHeight)
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

            for (int py = sy0; py <= sy1; py++)
            {
                canvas.DrawLine(color, sx0, py, sx1, py);
            }
        }

        private static void RenderMouseFrame(
            Canvas canvas,
            int screenWidth,
            int screenHeight,
            int phase,
            bool debugOverlay,
            bool patternOverlay,
            FrameDebugStats debugStats,
            out double averageBestSimilarity,
            out double peakBestSimilarity)
        {
            Color clearColor = Color.Black;
            if (patternOverlay)
            {
                int band = phase % 4;
                if (band == 1)
                {
                    clearColor = Color.Red;
                }
                else if (band == 2)
                {
                    clearColor = Color.Lime;
                }
                else if (band == 3)
                {
                    clearColor = Color.Blue;
                }
                else
                {
                    clearColor = Color.White;
                }
            }

            try
            {
                canvas.Clear(clearColor);
            }
            catch
            {
            }

            if (patternOverlay)
            {
                DrawCalibrationPattern(canvas, screenWidth, screenHeight, phase);
            }

            if (debugOverlay)
            {
                DrawDebugOverlay(canvas, screenWidth, screenHeight, debugStats);
            }

            averageBestSimilarity = 0.0;
            peakBestSimilarity = 0.0;
        }

        private static void RenderMouseFramePixels(
            int[] pixels,
            int screenWidth,
            int screenHeight,
            int phase,
            bool debugOverlay,
            bool patternOverlay,
            FrameDebugStats debugStats,
            out double averageBestSimilarity,
            out double peakBestSimilarity)
        {
            Color clearColor = Color.Black;
            if (patternOverlay)
            {
                int band = phase % 4;
                if (band == 1)
                {
                    clearColor = Color.Red;
                }
                else if (band == 2)
                {
                    clearColor = Color.Lime;
                }
                else if (band == 3)
                {
                    clearColor = Color.Blue;
                }
                else
                {
                    clearColor = Color.White;
                }
            }

            ClearPixels(pixels, clearColor.ToArgb());

            if (patternOverlay)
            {
                DrawCalibrationPatternPixels(pixels, screenWidth, screenHeight, phase);
            }

            if (debugOverlay)
            {
                DrawDebugOverlayPixels(pixels, screenWidth, screenHeight, debugStats);
            }

            averageBestSimilarity = 0.0;
            peakBestSimilarity = 0.0;
        }

        private static void DrawDebugOverlay(Canvas canvas, int screenWidth, int screenHeight, FrameDebugStats stats)
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

            canvas.DrawLine(Color.White, 0, 0, maxX, 0);
            canvas.DrawLine(Color.White, 0, maxY, maxX, maxY);
            canvas.DrawLine(Color.White, 0, 0, 0, maxY);
            canvas.DrawLine(Color.White, maxX, 0, maxX, maxY);

            canvas.DrawLine(Color.FromArgb(255, 64, 64), 0, y25, maxX, y25);
            canvas.DrawLine(Color.FromArgb(64, 255, 64), 0, y50, maxX, y50);
            canvas.DrawLine(Color.FromArgb(64, 64, 255), 0, y75, maxX, y75);

            if (stats != null && stats.NonBlackBlocks > 0)
            {
                int yMin = Clamp(stats.NonBlackMinY, 0, maxY);
                int yMax = Clamp(stats.NonBlackMaxY, 0, maxY);
                canvas.DrawLine(Color.Yellow, 0, yMin, maxX, yMin);
                canvas.DrawLine(Color.Yellow, 0, yMax, maxX, yMax);
            }
        }

        private static void DrawDebugOverlayPixels(int[] pixels, int screenWidth, int screenHeight, FrameDebugStats stats)
        {
            if (pixels == null || screenWidth <= 1 || screenHeight <= 1)
            {
                return;
            }

            int maxX = screenWidth - 1;
            int maxY = screenHeight - 1;
            int y25 = maxY / 4;
            int y50 = maxY / 2;
            int y75 = (maxY * 3) / 4;

            int white = Color.White.ToArgb();
            DrawHorizontalPixels(pixels, screenWidth, screenHeight, 0, maxX, 0, white);
            DrawHorizontalPixels(pixels, screenWidth, screenHeight, 0, maxX, maxY, white);
            DrawVerticalPixels(pixels, screenWidth, screenHeight, 0, 0, maxY, white);
            DrawVerticalPixels(pixels, screenWidth, screenHeight, maxX, 0, maxY, white);

            DrawHorizontalPixels(pixels, screenWidth, screenHeight, 0, maxX, y25, Color.FromArgb(255, 64, 64).ToArgb());
            DrawHorizontalPixels(pixels, screenWidth, screenHeight, 0, maxX, y50, Color.FromArgb(64, 255, 64).ToArgb());
            DrawHorizontalPixels(pixels, screenWidth, screenHeight, 0, maxX, y75, Color.FromArgb(64, 64, 255).ToArgb());

            if (stats != null && stats.NonBlackBlocks > 0)
            {
                int yMin = Clamp(stats.NonBlackMinY, 0, maxY);
                int yMax = Clamp(stats.NonBlackMaxY, 0, maxY);
                int yellow = Color.Yellow.ToArgb();
                DrawHorizontalPixels(pixels, screenWidth, screenHeight, 0, maxX, yMin, yellow);
                DrawHorizontalPixels(pixels, screenWidth, screenHeight, 0, maxX, yMax, yellow);
            }
        }

        private static void DrawCalibrationPattern(Canvas canvas, int screenWidth, int screenHeight, int phase)
        {
            if (canvas == null || screenWidth <= 1 || screenHeight <= 1)
            {
                return;
            }

            int step = Math.Max(6, Math.Min(screenWidth, screenHeight) / 64);
            int halfY = screenHeight / 2;
            int halfX = screenWidth / 2;
            int phaseBand = (phase / 2) & 1;

            for (int y = 0; y < screenHeight; y += step)
            {
                int yBand = y / step;
                for (int x = 0; x < screenWidth; x += step)
                {
                    int xBand = x / step;
                    bool checker = ((xBand + yBand + phaseBand) & 1) == 0;

                    Color c;
                    if (y < halfY)
                    {
                        c = checker ? Color.FromArgb(255, 64, 64) : Color.FromArgb(64, 64, 255);
                    }
                    else
                    {
                        c = checker ? Color.FromArgb(64, 255, 64) : Color.FromArgb(255, 255, 64);
                    }

                    canvas.DrawPoint(c, x, y);
                }
            }

            for (int x = 0; x < screenWidth; x += 2)
            {
                canvas.DrawPoint(Color.White, x, halfY);
            }
            for (int y = 0; y < screenHeight; y += 2)
            {
                canvas.DrawPoint(Color.White, halfX, y);
            }

            int sweepY = phase % screenHeight;
            int sweepX = (phase * 3) % screenWidth;
            for (int x = 0; x < screenWidth; x += 2)
            {
                canvas.DrawPoint(Color.Magenta, x, sweepY);
            }
            for (int y = 0; y < screenHeight; y += 2)
            {
                canvas.DrawPoint(Color.Cyan, sweepX, y);
            }
        }

        private static void DrawCalibrationPatternPixels(int[] pixels, int screenWidth, int screenHeight, int phase)
        {
            if (pixels == null || screenWidth <= 1 || screenHeight <= 1)
            {
                return;
            }

            int step = Math.Max(6, Math.Min(screenWidth, screenHeight) / 64);
            int halfY = screenHeight / 2;
            int halfX = screenWidth / 2;
            int phaseBand = (phase / 2) & 1;

            for (int y = 0; y < screenHeight; y += step)
            {
                int yBand = y / step;
                for (int x = 0; x < screenWidth; x += step)
                {
                    int xBand = x / step;
                    bool checker = ((xBand + yBand + phaseBand) & 1) == 0;
                    int color = (y < halfY)
                        ? (checker ? Color.FromArgb(255, 64, 64).ToArgb() : Color.FromArgb(64, 64, 255).ToArgb())
                        : (checker ? Color.FromArgb(64, 255, 64).ToArgb() : Color.FromArgb(255, 255, 64).ToArgb());
                    DrawPointPixels(pixels, screenWidth, screenHeight, x, y, color);
                }
            }

            int white = Color.White.ToArgb();
            for (int x = 0; x < screenWidth; x += 2)
            {
                DrawPointPixels(pixels, screenWidth, screenHeight, x, halfY, white);
            }
            for (int y = 0; y < screenHeight; y += 2)
            {
                DrawPointPixels(pixels, screenWidth, screenHeight, halfX, y, white);
            }

            int sweepY = phase % screenHeight;
            int sweepX = (phase * 3) % screenWidth;
            int magenta = Color.Magenta.ToArgb();
            int cyan = Color.Cyan.ToArgb();
            for (int x = 0; x < screenWidth; x += 2)
            {
                DrawPointPixels(pixels, screenWidth, screenHeight, x, sweepY, magenta);
            }
            for (int y = 0; y < screenHeight; y += 2)
            {
                DrawPointPixels(pixels, screenWidth, screenHeight, sweepX, y, cyan);
            }
        }

        private static void TryConfigureMouse(int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return;
            }

            try
            {
                uint maxX = (uint)Math.Max(1, screenWidth - 1);
                uint maxY = (uint)Math.Max(1, screenHeight - 1);

                Sys.MouseManager.ScreenWidth = maxX;
                Sys.MouseManager.ScreenHeight = maxY;

                if (Sys.MouseManager.X > maxX)
                {
                    Sys.MouseManager.X = maxX / 2;
                }
                if (Sys.MouseManager.Y > maxY)
                {
                    Sys.MouseManager.Y = maxY / 2;
                }
            }
            catch
            {
            }
        }

        private static bool TryReadMouse(int screenWidth, int screenHeight, out int x, out int y, out Sys.MouseState mouseState)
        {
            x = 0;
            y = 0;
            mouseState = Sys.MouseState.None;
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return false;
            }

            try
            {
                x = Clamp((int)Sys.MouseManager.X, 0, screenWidth - 1);
                y = Clamp((int)Sys.MouseManager.Y, 0, screenHeight - 1);
                mouseState = Sys.MouseManager.MouseState;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void AccumulateMouseStats(MouseDebugStats stats, int x, int y)
        {
            if (stats == null)
            {
                return;
            }

            if (stats.Samples == 0)
            {
                stats.StartX = x;
                stats.StartY = y;
            }

            stats.EndX = x;
            stats.EndY = y;
            if (x < stats.MinX)
            {
                stats.MinX = x;
            }
            if (x > stats.MaxX)
            {
                stats.MaxX = x;
            }
            if (y < stats.MinY)
            {
                stats.MinY = y;
            }
            if (y > stats.MaxY)
            {
                stats.MaxY = y;
            }
            stats.Samples++;
        }

        private static void DrawMouseOverlay(Canvas canvas, int screenWidth, int screenHeight, int mouseX, int mouseY, Sys.MouseState mouseState)
        {
            if (canvas == null || screenWidth <= 1 || screenHeight <= 1)
            {
                return;
            }

            int maxX = screenWidth - 1;
            int maxY = screenHeight - 1;
            int x = Clamp(mouseX, 0, maxX);
            int y = Clamp(mouseY, 0, maxY);

            Color crossColor = Color.White;
            if ((mouseState & Sys.MouseState.Left) == Sys.MouseState.Left)
            {
                crossColor = Color.Red;
            }
            else if ((mouseState & Sys.MouseState.Right) == Sys.MouseState.Right)
            {
                crossColor = Color.Cyan;
            }
            else if ((mouseState & Sys.MouseState.Middle) == Sys.MouseState.Middle)
            {
                crossColor = Color.Yellow;
            }

            int hLen = Math.Max(4, screenWidth / 40);
            int vLen = Math.Max(4, screenHeight / 30);

            int hx0 = Clamp(x - hLen, 0, maxX);
            int hx1 = Clamp(x + hLen, 0, maxX);
            int vy0 = Clamp(y - vLen, 0, maxY);
            int vy1 = Clamp(y + vLen, 0, maxY);

            canvas.DrawLine(crossColor, hx0, y, hx1, y);
            canvas.DrawLine(crossColor, x, vy0, x, vy1);

            int b = 2;
            int bx0 = Clamp(x - b, 0, maxX);
            int bx1 = Clamp(x + b, 0, maxX);
            int by0 = Clamp(y - b, 0, maxY);
            int by1 = Clamp(y + b, 0, maxY);
            canvas.DrawLine(crossColor, bx0, by0, bx1, by0);
            canvas.DrawLine(crossColor, bx1, by0, bx1, by1);
            canvas.DrawLine(crossColor, bx1, by1, bx0, by1);
            canvas.DrawLine(crossColor, bx0, by1, bx0, by0);
        }

        private static void DrawCenteredBlueSquare(Canvas canvas, int screenWidth, int screenHeight)
        {
            if (canvas == null || screenWidth <= 2 || screenHeight <= 2)
            {
                return;
            }

            int side = Math.Max(16, Math.Min(screenWidth, screenHeight) / 5);
            int x0 = Clamp((screenWidth - side) / 2, 0, screenWidth - 1);
            int y0 = Clamp((screenHeight - side) / 2, 0, screenHeight - 1);
            int x1 = Clamp(x0 + side - 1, 0, screenWidth - 1);
            int y1 = Clamp(y0 + side - 1, 0, screenHeight - 1);
            canvas.DrawFilledRectangle(Color.Blue, x0, y0, (x1 - x0) + 1, (y1 - y0) + 1);

            canvas.DrawLine(Color.White, x0, y0, x1, y0);
            canvas.DrawLine(Color.White, x1, y0, x1, y1);
            canvas.DrawLine(Color.White, x1, y1, x0, y1);
            canvas.DrawLine(Color.White, x0, y1, x0, y0);

            // Secondary top-left marker to detect "top strip only" scanout faults.
            int diagSide = Math.Max(12, side / 3);
            int dx0 = 8;
            int dy0 = 8;
            int dx1 = Clamp(dx0 + diagSide - 1, 0, screenWidth - 1);
            int dy1 = Clamp(dy0 + diagSide - 1, 0, screenHeight - 1);
            canvas.DrawFilledRectangle(Color.Blue, dx0, dy0, (dx1 - dx0) + 1, (dy1 - dy0) + 1);
            canvas.DrawLine(Color.White, dx0, dy0, dx1, dy0);
            canvas.DrawLine(Color.White, dx1, dy0, dx1, dy1);
            canvas.DrawLine(Color.White, dx1, dy1, dx0, dy1);
            canvas.DrawLine(Color.White, dx0, dy1, dx0, dy0);
        }

        private static void DrawMouseOverlayPixels(int[] pixels, int screenWidth, int screenHeight, int mouseX, int mouseY, Sys.MouseState mouseState)
        {
            if (pixels == null || screenWidth <= 1 || screenHeight <= 1)
            {
                return;
            }

            int maxX = screenWidth - 1;
            int maxY = screenHeight - 1;
            int x = Clamp(mouseX, 0, maxX);
            int y = Clamp(mouseY, 0, maxY);

            Color crossColor = Color.White;
            if ((mouseState & Sys.MouseState.Left) == Sys.MouseState.Left)
            {
                crossColor = Color.Red;
            }
            else if ((mouseState & Sys.MouseState.Right) == Sys.MouseState.Right)
            {
                crossColor = Color.Cyan;
            }
            else if ((mouseState & Sys.MouseState.Middle) == Sys.MouseState.Middle)
            {
                crossColor = Color.Yellow;
            }

            int hLen = Math.Max(4, screenWidth / 40);
            int vLen = Math.Max(4, screenHeight / 30);
            int hx0 = Clamp(x - hLen, 0, maxX);
            int hx1 = Clamp(x + hLen, 0, maxX);
            int vy0 = Clamp(y - vLen, 0, maxY);
            int vy1 = Clamp(y + vLen, 0, maxY);
            int color = crossColor.ToArgb();

            DrawHorizontalPixels(pixels, screenWidth, screenHeight, hx0, hx1, y, color);
            DrawVerticalPixels(pixels, screenWidth, screenHeight, x, vy0, vy1, color);

            int b = 2;
            int bx0 = Clamp(x - b, 0, maxX);
            int bx1 = Clamp(x + b, 0, maxX);
            int by0 = Clamp(y - b, 0, maxY);
            int by1 = Clamp(y + b, 0, maxY);
            DrawRectanglePixels(pixels, screenWidth, screenHeight, bx0, by0, bx1, by1, color);
        }

        private static void DrawCenteredBlueSquarePixels(int[] pixels, int screenWidth, int screenHeight)
        {
            if (pixels == null || screenWidth <= 2 || screenHeight <= 2)
            {
                return;
            }

            int side = Math.Max(16, Math.Min(screenWidth, screenHeight) / 5);
            int x0 = Clamp((screenWidth - side) / 2, 0, screenWidth - 1);
            int y0 = Clamp((screenHeight - side) / 2, 0, screenHeight - 1);
            int x1 = Clamp(x0 + side - 1, 0, screenWidth - 1);
            int y1 = Clamp(y0 + side - 1, 0, screenHeight - 1);

            int blue = Color.Blue.ToArgb();
            for (int y = y0; y <= y1; y++)
            {
                DrawHorizontalPixels(pixels, screenWidth, screenHeight, x0, x1, y, blue);
            }

            DrawRectanglePixels(pixels, screenWidth, screenHeight, x0, y0, x1, y1, Color.White.ToArgb());

            int diagSide = Math.Max(12, side / 3);
            int dx0 = 8;
            int dy0 = 8;
            int dx1 = Clamp(dx0 + diagSide - 1, 0, screenWidth - 1);
            int dy1 = Clamp(dy0 + diagSide - 1, 0, screenHeight - 1);
            for (int y = dy0; y <= dy1; y++)
            {
                DrawHorizontalPixels(pixels, screenWidth, screenHeight, dx0, dx1, y, blue);
            }
            DrawRectanglePixels(pixels, screenWidth, screenHeight, dx0, dy0, dx1, dy1, Color.White.ToArgb());
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

        private static bool TrySamplePatternPixels(Canvas canvas, int screenWidth, int screenHeight, out int topArgb, out int midArgb, out int bottomArgb)
        {
            topArgb = 0;
            midArgb = 0;
            bottomArgb = 0;
            if (canvas == null || screenWidth <= 0 || screenHeight <= 0)
            {
                return false;
            }

            int x = Clamp(screenWidth / 2, 0, screenWidth - 1);
            int xTopLeft = Clamp(Math.Min(16, screenWidth - 1), 0, screenWidth - 1);
            int yTop = Clamp(Math.Min(16, screenHeight - 1), 0, screenHeight - 1);
            int yMid = Clamp(screenHeight / 2, 0, screenHeight - 1);
            int yBottom = screenHeight - 1;

            try
            {
                topArgb = canvas.GetPointColor(xTopLeft, yTop).ToArgb();
                midArgb = canvas.GetPointColor(x, yMid).ToArgb();
                bottomArgb = canvas.GetPointColor(x, yBottom).ToArgb();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySamplePatternPixelsFromBuffer(int[] pixels, int screenWidth, int screenHeight, out int topArgb, out int midArgb, out int bottomArgb)
        {
            topArgb = 0;
            midArgb = 0;
            bottomArgb = 0;
            if (pixels == null || screenWidth <= 0 || screenHeight <= 0 || pixels.Length < screenWidth * screenHeight)
            {
                return false;
            }

            int x = Clamp(screenWidth / 2, 0, screenWidth - 1);
            int xTopLeft = Clamp(Math.Min(16, screenWidth - 1), 0, screenWidth - 1);
            int yTop = Clamp(Math.Min(16, screenHeight - 1), 0, screenHeight - 1);
            int yMid = Clamp(screenHeight / 2, 0, screenHeight - 1);
            int yBottom = screenHeight - 1;

            topArgb = pixels[yTop * screenWidth + xTopLeft];
            midArgb = pixels[yMid * screenWidth + x];
            bottomArgb = pixels[yBottom * screenWidth + x];
            return true;
        }

        private static void ClearPixels(int[] pixels, int argb)
        {
            if (pixels == null)
            {
                return;
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = argb;
            }
        }

        private static void DrawPointPixels(int[] pixels, int screenWidth, int screenHeight, int x, int y, int argb)
        {
            if (pixels == null || screenWidth <= 0 || screenHeight <= 0)
            {
                return;
            }
            if (x < 0 || y < 0 || x >= screenWidth || y >= screenHeight)
            {
                return;
            }

            int idx = y * screenWidth + x;
            if (idx >= 0 && idx < pixels.Length)
            {
                pixels[idx] = argb;
            }
        }

        private static void DrawHorizontalPixels(int[] pixels, int screenWidth, int screenHeight, int x0, int x1, int y, int argb)
        {
            if (pixels == null || screenWidth <= 0 || screenHeight <= 0 || y < 0 || y >= screenHeight)
            {
                return;
            }

            int sx0 = Clamp(x0, 0, screenWidth - 1);
            int sx1 = Clamp(x1, 0, screenWidth - 1);
            if (sx1 < sx0)
            {
                return;
            }

            int row = y * screenWidth;
            int start = row + sx0;
            int end = row + sx1;
            if (start < 0)
            {
                start = 0;
            }
            if (end >= pixels.Length)
            {
                end = pixels.Length - 1;
            }
            for (int i = start; i <= end; i++)
            {
                pixels[i] = argb;
            }
        }

        private static void DrawVerticalPixels(int[] pixels, int screenWidth, int screenHeight, int x, int y0, int y1, int argb)
        {
            if (pixels == null || screenWidth <= 0 || screenHeight <= 0 || x < 0 || x >= screenWidth)
            {
                return;
            }

            int sy0 = Clamp(y0, 0, screenHeight - 1);
            int sy1 = Clamp(y1, 0, screenHeight - 1);
            if (sy1 < sy0)
            {
                return;
            }

            for (int y = sy0; y <= sy1; y++)
            {
                int idx = (y * screenWidth) + x;
                if (idx >= 0 && idx < pixels.Length)
                {
                    pixels[idx] = argb;
                }
            }
        }

        private static void DrawRectanglePixels(int[] pixels, int screenWidth, int screenHeight, int x0, int y0, int x1, int y1, int argb)
        {
            DrawHorizontalPixels(pixels, screenWidth, screenHeight, x0, x1, y0, argb);
            DrawHorizontalPixels(pixels, screenWidth, screenHeight, x0, x1, y1, argb);
            DrawVerticalPixels(pixels, screenWidth, screenHeight, x0, y0, y1, argb);
            DrawVerticalPixels(pixels, screenWidth, screenHeight, x1, y0, y1, argb);
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

        private static bool ShouldUseDisplayFlip(string canvasBackend)
        {
            if (string.IsNullOrEmpty(canvasBackend))
            {
                return true;
            }

            // SVGAII and VBE both may require explicit present to update scanout in some drivers.
            if (canvasBackend.IndexOf("SVGAII", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (canvasBackend.IndexOf("VBE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (canvasBackend.IndexOf("VGA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
            return true;
        }

        private static int NormalizeDepthBits(int bits)
        {
            if (bits == 8 || bits == 16 || bits == 24 || bits == 32)
            {
                return bits;
            }
            return 32;
        }

        private static ColorDepth BitsToColorDepth(int bits)
        {
            if (bits == 8)
            {
                return ColorDepth.ColorDepth8;
            }
            if (bits == 16)
            {
                return ColorDepth.ColorDepth16;
            }
            if (bits == 24)
            {
                return ColorDepth.ColorDepth24;
            }
            return ColorDepth.ColorDepth32;
        }

        private static int ColorDepthToBits(ColorDepth depth)
        {
            if (depth == ColorDepth.ColorDepth8)
            {
                return 8;
            }
            if (depth == ColorDepth.ColorDepth16)
            {
                return 16;
            }
            if (depth == ColorDepth.ColorDepth24)
            {
                return 24;
            }
            if (depth == ColorDepth.ColorDepth32)
            {
                return 32;
            }
            return 0;
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
