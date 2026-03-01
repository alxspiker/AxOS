// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using System;
using System.Drawing;
using AxOS.Core;
using AxOS.Kernel;
using Cosmos.Core;
using Cosmos.System.Graphics;
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
            public int ColorDepthBits;
            public string CanvasBackend = string.Empty;
            public bool DisplayFlipUsed;
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

        private sealed class PaletteEntry
        {
            public Tensor Vector;
            public Color Color;
        }

        public const string FinalRenderProfileName = "vga8_blue_square_mouseonly_20s";

        public static RenderConfig CreateFinalRenderConfig()
        {
            return new RenderConfig
            {
                DurationSeconds = 20,
                ScreenWidth = 640,
                ScreenHeight = 480,
                LogicalWidth = 24,
                LogicalHeight = 18,
                Dim = 48,
                Threshold = 0.002,
                TargetFps = 8,
                Seed = 1337
            };
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
            int logicalWidth = Clamp(config.LogicalWidth, 24, 200);
            int logicalHeight = Clamp(config.LogicalHeight, 18, 160);
            int dim = Clamp(config.Dim, 32, 4096);
            int fps = Clamp(config.TargetFps, 1, 30);
            double threshold = Clamp(config.Threshold, -1.0, 1.0);

            Canvas canvas = null;
            DateTime startedUtc = DateTime.UtcNow;
            try
            {
                const string canvasBackend = "VGACanvas";
                const int colorDepthBits = 8;
                canvas = new VGACanvas(new Mode(320, 200, ColorDepth.ColorDepth8));
                if (canvas == null || canvas.Mode.Width <= 0 || canvas.Mode.Height <= 0)
                {
                    error = "graphics_canvas_unavailable";
                    return false;
                }

                int screenWidth = (int)canvas.Mode.Width;
                int screenHeight = (int)canvas.Mode.Height;
                if (logicalWidth > screenWidth)
                {
                    logicalWidth = screenWidth;
                }
                if (logicalHeight > screenHeight)
                {
                    logicalHeight = screenHeight;
                }

                TryConfigureMouse(screenWidth, screenHeight);

                int targetFrames = Math.Max(1, seconds * fps);
                int renderedFrames = 0;
                bool exitedByKey = false;
                double frameAverageAccumulator = 0.0;
                double peakSimilarity = 0.0;
                MouseDebugStats mouseStats = new MouseDebugStats();
                bool patternProbeCaptured = false;
                int patternTopArgb = 0;
                int patternMidArgb = 0;
                int patternBottomArgb = 0;

                bool hasPreviousMouseOverlay = false;
                int previousMouseX = 0;
                int previousMouseY = 0;
                Sys.MouseState previousMouseState = Sys.MouseState.None;

                while (renderedFrames < targetFrames)
                {
                    double frameAvgSimilarity;
                    double framePeakSimilarity;
                    RenderMouseFrame(canvas, renderedFrames > 0, out frameAvgSimilarity, out framePeakSimilarity);
                    DrawCenteredBlueSquare(canvas, screenWidth, screenHeight);

                    if (TryReadMouse(screenWidth, screenHeight, out int mouseX, out int mouseY, out Sys.MouseState mouseState))
                    {
                        AccumulateMouseStats(mouseStats, mouseX, mouseY);
                        bool overlayChanged = !hasPreviousMouseOverlay ||
                            previousMouseX != mouseX ||
                            previousMouseY != mouseY ||
                            previousMouseState != mouseState;

                        if (hasPreviousMouseOverlay && overlayChanged)
                        {
                            EraseMouseOverlay(canvas, screenWidth, screenHeight, previousMouseX, previousMouseY);
                            DrawCenteredBlueSquare(canvas, screenWidth, screenHeight);
                        }

                        if (overlayChanged)
                        {
                            DrawMouseOverlay(canvas, screenWidth, screenHeight, mouseX, mouseY, mouseState);
                        }

                        hasPreviousMouseOverlay = true;
                        previousMouseX = mouseX;
                        previousMouseY = mouseY;
                        previousMouseState = mouseState;
                    }

                    if (!patternProbeCaptured)
                    {
                        patternProbeCaptured = TrySamplePatternPixels(
                            canvas,
                            screenWidth,
                            screenHeight,
                            out patternTopArgb,
                            out patternMidArgb,
                            out patternBottomArgb);
                    }

                    renderedFrames++;
                    frameAverageAccumulator += frameAvgSimilarity;
                    if (framePeakSimilarity > peakSimilarity)
                    {
                        peakSimilarity = framePeakSimilarity;
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
                report.EncodedPoints = 0;
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
                report.ColorDepthBits = colorDepthBits;
                report.CanvasBackend = canvasBackend;
                report.DisplayFlipUsed = false;
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
                    canvas?.Disable();
                }
                catch
                {
                }
            }
        }

        public bool RunManifoldPreview(
            ProgramManifold manifold,
            out RenderReport report,
            out string error,
            Action<string> frameLogger = null,
            bool logFrameFps = false)
        {
            report = new RenderReport();
            error = string.Empty;

            if (manifold == null)
            {
                error = "missing_manifold";
                return false;
            }

            if (!TryGetTopManifoldTensor(manifold, out Tensor manifoldTensor))
            {
                AdvanceManifoldFrame(manifold, 0);
                if (!TryGetTopManifoldTensor(manifold, out manifoldTensor))
                {
                    error = "manifold_empty";
                    return false;
                }
            }

            Canvas canvas = null;
            try
            {
                const int seconds = 20;
                const int targetFps = 30;
                const int manifoldUpdatesPerSecond = 1;
                const string canvasBackend = "VGACanvas";
                const int colorDepthBits = 8;
                canvas = new VGACanvas(new Mode(320, 200, ColorDepth.ColorDepth8));
                if (canvas == null || canvas.Mode.Width <= 0 || canvas.Mode.Height <= 0)
                {
                    error = "graphics_canvas_unavailable";
                    return false;
                }

                int screenWidth = (int)canvas.Mode.Width;
                int screenHeight = (int)canvas.Mode.Height;
                int targetFrames = Math.Max(1, seconds * targetFps);
                int manifoldUpdateInterval = Math.Max(1, targetFps / manifoldUpdatesPerSecond);
                int renderedFrames = 0;
                int manifoldUpdateCount = 0;
                bool exitedByKey = false;
                bool patternProbeCaptured = false;
                int patternTopArgb = 0;
                int patternMidArgb = 0;
                int patternBottomArgb = 0;
                long cpuHz = GetCpuCycleHzSafe();
                double startedSeconds = ReadClockSeconds(cpuHz);
                double nextFrameDueSeconds = startedSeconds;
                double lastFpsLogSeconds = startedSeconds;
                int lastFpsLogFrame = 0;
                const int cols = 32;
                const int rows = 20;
                int cells = cols * rows;
                float[] projectedField = new float[cells];
                float[] smoothedField = new float[cells];
                float[] lastDrawnField = new float[cells];
                for (int i = 0; i < lastDrawnField.Length; i++)
                {
                    lastDrawnField[i] = -1.0f;
                }
                int previousStrongestCell = -1;
                bool firstFrame = true;
                BuildManifoldFieldNormalized(manifoldTensor, projectedField, 0);

                while (renderedFrames < targetFrames)
                {
                    double frameStartSeconds = ReadClockSeconds(cpuHz);
                    bool manifoldUpdatedThisFrame = (renderedFrames % manifoldUpdateInterval) == 0;
                    if (manifoldUpdatedThisFrame)
                    {
                        AdvanceManifoldFrame(manifold, renderedFrames + 1);
                        manifoldUpdateCount++;
                        if (TryGetTopManifoldTensor(manifold, out Tensor refreshedTensor))
                        {
                            manifoldTensor = refreshedTensor;
                        }
                    }
                    BuildManifoldFieldNormalized(manifoldTensor, projectedField, renderedFrames);

                    float blend = manifoldUpdatedThisFrame ? 0.24f : 0.12f;
                    for (int i = 0; i < cells; i++)
                    {
                        // Temporal smoothing for softer transitions.
                        smoothedField[i] = (smoothedField[i] * (1.0f - blend)) + (projectedField[i] * blend);
                    }

                    DrawManifoldHeatmap(
                        canvas,
                        smoothedField,
                        lastDrawnField,
                        screenWidth,
                        screenHeight,
                        ref previousStrongestCell,
                        firstFrame,
                        renderedFrames);
                    firstFrame = false;
                    if (!patternProbeCaptured)
                    {
                        patternProbeCaptured = TrySamplePatternPixels(
                            canvas,
                            screenWidth,
                            screenHeight,
                            out patternTopArgb,
                            out patternMidArgb,
                            out patternBottomArgb);
                    }

                    renderedFrames++;

                    if (Sys.KeyboardManager.KeyAvailable && Sys.KeyboardManager.TryReadKey(out Sys.KeyEvent keyEvent))
                    {
                        char keyChar = keyEvent.KeyChar;
                        if (keyChar == (char)27 || keyChar == '\r' || keyChar == '\n' || keyChar == 'q' || keyChar == 'Q')
                        {
                            exitedByKey = true;
                            break;
                        }
                    }

                    double nowSeconds = ReadClockSeconds(cpuHz);
                    if (nowSeconds - startedSeconds >= seconds)
                    {
                        break;
                    }

                    nextFrameDueSeconds += 1.0 / targetFps;
                    while (ReadClockSeconds(cpuHz) < nextFrameDueSeconds)
                    {
                        if (Sys.KeyboardManager.KeyAvailable && Sys.KeyboardManager.TryReadKey(out Sys.KeyEvent waitKeyEvent))
                        {
                            char keyChar = waitKeyEvent.KeyChar;
                            if (keyChar == (char)27 || keyChar == '\r' || keyChar == '\n' || keyChar == 'q' || keyChar == 'Q')
                            {
                                exitedByKey = true;
                                break;
                            }
                        }
                    }
                    if (exitedByKey)
                    {
                        break;
                    }

                    if (logFrameFps && frameLogger != null)
                    {
                        double afterFrameSeconds = ReadClockSeconds(cpuHz);
                        if ((afterFrameSeconds - lastFpsLogSeconds) >= 1.0 || renderedFrames == targetFrames)
                        {
                            int intervalFrames = renderedFrames - lastFpsLogFrame;
                            double intervalSeconds = afterFrameSeconds - lastFpsLogSeconds;
                            double elapsedSeconds = afterFrameSeconds - startedSeconds;
                            double intervalFps = intervalSeconds > 0.0 ? intervalFrames / intervalSeconds : 0.0;
                            double averageFps = elapsedSeconds > 0.0 ? renderedFrames / elapsedSeconds : 0.0;
                            double frameMs = (afterFrameSeconds - frameStartSeconds) * 1000.0;
                            frameLogger(
                                "holo_manifold_fps: frame=" +
                                renderedFrames +
                                "/" +
                                targetFrames +
                                ", last_frame_ms=" +
                                frameMs.ToString("0.0") +
                                ", fps_1s=" +
                                intervalFps.ToString("0.00") +
                                ", avg_fps=" +
                                averageFps.ToString("0.00") +
                                ", manifold_updates=" +
                                manifoldUpdateCount);
                            lastFpsLogSeconds = afterFrameSeconds;
                            lastFpsLogFrame = renderedFrames;
                        }
                    }
                }

                double elapsedSecondsFinal = ReadClockSeconds(cpuHz) - startedSeconds;
                if (elapsedSecondsFinal < 0.0)
                {
                    elapsedSecondsFinal = 0.0;
                }
                report.RenderedFrames = renderedFrames;
                report.TargetFrames = targetFrames;
                report.EncodedPoints = manifoldTensor.Total;
                report.Dim = manifoldTensor.Total;
                report.ScreenWidth = screenWidth;
                report.ScreenHeight = screenHeight;
                report.LogicalWidth = cols;
                report.LogicalHeight = rows;
                report.TargetFps = targetFps;
                report.DurationSeconds = seconds;
                report.Threshold = 0.0;
                report.AvgBestSimilarity = 0.0;
                report.PeakBestSimilarity = 0.0;
                report.ElapsedMilliseconds = (long)(elapsedSecondsFinal * 1000.0);
                report.ExitedByKey = exitedByKey;
                report.ColorDepthBits = colorDepthBits;
                report.CanvasBackend = canvasBackend;
                report.DisplayFlipUsed = false;
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
                    canvas?.Disable();
                }
                catch
                {
                }
            }
        }

        public bool RunUiDemo(RenderConfig config, out RenderReport report, out string error)
        {
            report = new RenderReport();
            error = string.Empty;

            if (config == null)
            {
                error = "missing_config";
                return false;
            }

            int seconds = Clamp(config.DurationSeconds, 1, 120);
            int logicalWidth = Clamp(config.LogicalWidth, 8, 200);
            int logicalHeight = Clamp(config.LogicalHeight, 6, 160);
            int dim = Math.Max(1024, Clamp(config.Dim, 32, 4096));
            int fps = Clamp(config.TargetFps, 1, 30);

            Canvas canvas = null;
            DateTime startedUtc = DateTime.UtcNow;
            try
            {
                const string canvasBackend = "VGACanvas";
                const int colorDepthBits = 8;
                canvas = new VGACanvas(new Mode(320, 200, ColorDepth.ColorDepth8));
                if (canvas == null || canvas.Mode.Width <= 0 || canvas.Mode.Height <= 0)
                {
                    error = "graphics_canvas_unavailable";
                    return false;
                }

                canvas.Clear(Color.Black);

                int screenWidth = (int)canvas.Mode.Width;
                int screenHeight = (int)canvas.Mode.Height;
                if (logicalWidth > screenWidth)
                {
                    logicalWidth = screenWidth;
                }
                if (logicalHeight > screenHeight)
                {
                    logicalHeight = screenHeight;
                }

                Tensor[] coordinates = BuildUiCoordinates(logicalWidth, logicalHeight, dim, config.Seed);
                PaletteEntry[] palette = BuildUiPalette(dim, config.Seed);
                Tensor scene = BuildUiManifold(logicalWidth, logicalHeight, coordinates, palette);

                int targetFrames = Math.Max(1, seconds * fps);
                int renderedFrames = 0;
                bool exitedByKey = false;
                bool patternProbeCaptured = false;
                int patternTopArgb = 0;
                int patternMidArgb = 0;
                int patternBottomArgb = 0;
                DateTime nextFrameDue = startedUtc;

                while (renderedFrames < targetFrames)
                {
                    if (renderedFrames == 0)
                    {
                        DrawUiScene(
                            canvas,
                            scene,
                            logicalWidth,
                            logicalHeight,
                            coordinates,
                            palette,
                            screenWidth,
                            screenHeight);

                        patternProbeCaptured = TrySamplePatternPixels(
                            canvas,
                            screenWidth,
                            screenHeight,
                            out patternTopArgb,
                            out patternMidArgb,
                            out patternBottomArgb);
                    }

                    renderedFrames++;

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

                    nextFrameDue = nextFrameDue.AddMilliseconds(1000.0 / fps);
                    while (DateTime.UtcNow < nextFrameDue)
                    {
                        if (Sys.KeyboardManager.KeyAvailable && Sys.KeyboardManager.TryReadKey(out Sys.KeyEvent waitKeyEvent))
                        {
                            char keyChar = waitKeyEvent.KeyChar;
                            if (keyChar == (char)27 || keyChar == '\r' || keyChar == '\n' || keyChar == 'q' || keyChar == 'Q')
                            {
                                exitedByKey = true;
                                break;
                            }
                        }
                    }
                    if (exitedByKey)
                    {
                        break;
                    }
                }

                TimeSpan elapsed = DateTime.UtcNow - startedUtc;
                report.RenderedFrames = renderedFrames;
                report.TargetFrames = targetFrames;
                report.EncodedPoints = logicalWidth * logicalHeight;
                report.Dim = dim;
                report.ScreenWidth = screenWidth;
                report.ScreenHeight = screenHeight;
                report.LogicalWidth = logicalWidth;
                report.LogicalHeight = logicalHeight;
                report.TargetFps = fps;
                report.DurationSeconds = seconds;
                report.Threshold = 0.0;
                report.AvgBestSimilarity = 0.0;
                report.PeakBestSimilarity = 0.0;
                report.ElapsedMilliseconds = (long)elapsed.TotalMilliseconds;
                report.ExitedByKey = exitedByKey;
                report.ColorDepthBits = colorDepthBits;
                report.CanvasBackend = canvasBackend;
                report.DisplayFlipUsed = false;
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
                    canvas?.Disable();
                }
                catch
                {
                }
            }
        }

        private static Tensor BuildUiManifold(int logicalWidth, int logicalHeight, Tensor[] coordinates, PaletteEntry[] palette)
        {
            if (coordinates == null || coordinates.Length == 0 || palette == null || palette.Length < 4)
            {
                return new Tensor();
            }

            int dim = coordinates[0].Total;
            float[] sceneRaw = new float[dim];

            for (int y = 0; y < logicalHeight; y++)
            {
                for (int x = 0; x < logicalWidth; x++)
                {
                    int idx = y * logicalWidth + x;
                    float[] query = coordinates[idx].Data;

                    for (int d = 0; d < dim; d++)
                    {
                        sceneRaw[d] += query[d] * palette[0].Vector.Data[d] * 0.4f;
                    }

                    if (x >= 4 && x <= 18 && y >= 3 && y <= 12)
                    {
                        for (int d = 0; d < dim; d++)
                        {
                            sceneRaw[d] += query[d] * palette[1].Vector.Data[d];
                        }
                    }

                    if (x >= 12 && x <= 28 && y >= 8 && y <= 18)
                    {
                        for (int d = 0; d < dim; d++)
                        {
                            sceneRaw[d] += query[d] * palette[2].Vector.Data[d];
                        }
                    }

                    if (y >= logicalHeight - 2)
                    {
                        for (int d = 0; d < dim; d++)
                        {
                            sceneRaw[d] += query[d] * palette[3].Vector.Data[d] * 1.5f;
                        }
                    }
                }
            }

            return TensorOps.NormalizeL2(new Tensor(sceneRaw));
        }

        private static Tensor[] BuildUiCoordinates(int logicalWidth, int logicalHeight, int dim, int seed)
        {
            Tensor[] xBasis = new Tensor[logicalWidth];
            Tensor[] yBasis = new Tensor[logicalHeight];
            Tensor[] coordinates = new Tensor[logicalWidth * logicalHeight];

            ulong baseSeed = ((ulong)(seed >= 0 ? seed : -seed)) + 1UL;
            for (int x = 0; x < logicalWidth; x++)
            {
                xBasis[x] = TensorOps.RandomHypervector(dim, baseSeed + ((ulong)(x + 1) * 1315423911UL));
            }
            for (int y = 0; y < logicalHeight; y++)
            {
                yBasis[y] = TensorOps.RandomHypervector(dim, baseSeed + ((ulong)(y + 1) * 2654435761UL));
            }

            for (int y = 0; y < logicalHeight; y++)
            {
                for (int x = 0; x < logicalWidth; x++)
                {
                    int idx = y * logicalWidth + x;
                    coordinates[idx] = TensorOps.NormalizeL2(TensorOps.Bind(xBasis[x], yBasis[y]));
                }
            }

            return coordinates;
        }

        private static PaletteEntry[] BuildUiPalette(int dim, int seed)
        {
            ulong baseSeed = ((ulong)(seed >= 0 ? seed : -seed)) + 0x9E3779B9UL;
            PaletteEntry[] palette = new PaletteEntry[4];

            palette[0] = new PaletteEntry
            {
                Color = Color.FromArgb(30, 120, 230), // Azure desktop
                Vector = TensorOps.RandomHypervector(dim, baseSeed + 0x1001UL)
            };
            palette[1] = new PaletteEntry
            {
                Color = Color.FromArgb(20, 210, 245), // Cyan window A
                Vector = TensorOps.RandomHypervector(dim, baseSeed + 0x2003UL)
            };
            palette[2] = new PaletteEntry
            {
                Color = Color.FromArgb(245, 180, 30), // Amber window B
                Vector = TensorOps.RandomHypervector(dim, baseSeed + 0x3007UL)
            };
            palette[3] = new PaletteEntry
            {
                Color = Color.FromArgb(210, 40, 170), // Magenta taskbar
                Vector = TensorOps.RandomHypervector(dim, baseSeed + 0x4009UL)
            };

            return palette;
        }

        private static void DrawUiScene(
            Canvas canvas,
            Tensor scene,
            int logicalWidth,
            int logicalHeight,
            Tensor[] coordinates,
            PaletteEntry[] palette,
            int screenWidth,
            int screenHeight)
        {
            if (canvas == null || scene == null || scene.IsEmpty || coordinates == null || palette == null)
            {
                return;
            }

            int cellW = Math.Max(1, screenWidth / logicalWidth);
            int cellH = Math.Max(1, screenHeight / logicalHeight);
            int drawW = logicalWidth * cellW;
            int drawH = logicalHeight * cellH;
            int offX = Math.Max(0, (screenWidth - drawW) / 2);
            int offY = Math.Max(0, (screenHeight - drawH) / 2);

            for (int y = 0; y < logicalHeight; y++)
            {
                for (int x = 0; x < logicalWidth; x++)
                {
                    int idx = y * logicalWidth + x;
                    int px = offX + (x * cellW);
                    int py = offY + (y * cellH);
                    Color color = DecodeUiCellColor(scene, coordinates[idx], palette);
                    canvas.DrawFilledRectangle(color, px, py, cellW, cellH);
                }
            }
        }

        private static Color DecodeUiCellColor(Tensor scene, Tensor coordinate, PaletteEntry[] palette)
        {
            int dim = scene.Total;
            if (coordinate == null || coordinate.Total == 0 || palette == null || palette.Length == 0)
            {
                return Color.Black;
            }

            if (coordinate.Total < dim)
            {
                dim = coordinate.Total;
            }

            int paletteCount = palette.Length;
            double[] dots = new double[paletteCount];
            double unboundNormSq = 0.0;

            for (int i = 0; i < dim; i++)
            {
                double unbound = scene.Data[i] * coordinate.Data[i];
                unboundNormSq += unbound * unbound;

                for (int p = 0; p < paletteCount; p++)
                {
                    Tensor pv = palette[p].Vector;
                    if (pv != null && i < pv.Total)
                    {
                        dots[p] += unbound * pv.Data[i];
                    }
                }
            }

            double unboundNorm = Math.Sqrt(unboundNormSq);
            if (unboundNorm <= 1e-8)
            {
                return palette[0].Color;
            }

            float weightedR = 0.0f;
            float weightedG = 0.0f;
            float weightedB = 0.0f;
            float totalWeight = 0.0f;
            for (int p = 0; p < paletteCount; p++)
            {
                float similarity = (float)(dots[p] / unboundNorm); // palette vectors are already unit-normalized
                if (similarity <= 0.0f)
                {
                    continue;
                }

                // Square positive similarity so strong semantics dominate weak noise.
                float weight = similarity * similarity;
                totalWeight += weight;
                weightedR += palette[p].Color.R * weight;
                weightedG += palette[p].Color.G * weight;
                weightedB += palette[p].Color.B * weight;
            }

            if (totalWeight <= 0.000001f)
            {
                return palette[0].Color;
            }

            int r = Clamp((int)(weightedR / totalWeight), 0, 255);
            int g = Clamp((int)(weightedG / totalWeight), 0, 255);
            int b = Clamp((int)(weightedB / totalWeight), 0, 255);
            return Color.FromArgb(r, g, b);
        }

        private static bool TryGetTopManifoldTensor(ProgramManifold manifold, out Tensor tensor)
        {
            tensor = null;
            var snapshot = manifold.SnapshotWorkingMemory(1);
            if (snapshot == null || snapshot.Count == 0)
            {
                return false;
            }

            Tensor top = snapshot[0].Value == null ? null : snapshot[0].Value.Flatten();
            if (top == null || top.IsEmpty)
            {
                return false;
            }

            tensor = top;
            return true;
        }

        private static void AdvanceManifoldFrame(ProgramManifold manifold, int frameIndex)
        {
            int phase = frameIndex % 12;
            manifold.Enqueue(new DataStream
            {
                DatasetType = "semantic_logic",
                DatasetId = "live_phase_" + phase,
                Payload = BuildLivePayload(phase)
            });
            manifold.RunBatch(1);
        }

        private static string BuildLivePayload(int phase)
        {
            switch (phase)
            {
                case 0: return "ALPHA BETA GAMMA ALPHA";
                case 1: return "BETA ALPHA GAMMA BETA";
                case 2: return "GAMMA BETA ALPHA GAMMA";
                case 3: return "ALPHA GAMMA BETA ALPHA";
                case 4: return "GAMMA ALPHA BETA ALPHA";
                case 5: return "BETA GAMMA ALPHA BETA";
                case 6: return "ALPHA ALPHA BETA GAMMA";
                case 7: return "BETA BETA GAMMA ALPHA";
                case 8: return "GAMMA GAMMA ALPHA BETA";
                case 9: return "ALPHA BETA ALPHA GAMMA";
                case 10: return "BETA GAMMA BETA ALPHA";
                default: return "GAMMA ALPHA GAMMA BETA";
            }
        }

        private static long GetCpuCycleHzSafe()
        {
            try
            {
                long hz = CPU.GetCPUCycleSpeed();
                if (hz > 1_000_000L)
                {
                    return hz;
                }
            }
            catch
            {
            }

            return 1_000_000_000L;
        }

        private static double ReadClockSeconds(long cpuHz)
        {
            try
            {
                ulong ticks = CPU.GetCPUUptime();
                long safeHz = cpuHz > 0 ? cpuHz : 1_000_000_000L;
                return ticks / (double)safeHz;
            }
            catch
            {
                return DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
            }
        }

        private static void BuildManifoldFieldNormalized(Tensor tensor, float[] field, int frameIndex)
        {
            if (tensor == null || tensor.IsEmpty || field == null || field.Length == 0)
            {
                return;
            }

            for (int i = 0; i < field.Length; i++)
            {
                field[i] = 0.0f;
            }

            int cells = field.Length;
            for (int i = 0; i < tensor.Total; i++)
            {
                float energy = tensor.Data[i];
                if (energy < 0.0f)
                {
                    energy = -energy;
                }
                if (energy <= 0.000001f)
                {
                    continue;
                }

                uint h = (uint)(i * 2654435761u);
                int c0 = (int)(h % (uint)cells);
                int c1 = (int)((h >> 8) % (uint)cells);
                int c2 = (int)((h >> 16) % (uint)cells);
                field[c0] += energy;
                field[c1] += energy * 0.60f;
                field[c2] += energy * 0.35f;
            }

            float maxField = 0.0f;
            for (int i = 0; i < cells; i++)
            {
                if (field[i] > maxField)
                {
                    maxField = field[i];
                }
            }
            if (maxField <= 0.000001f)
            {
                maxField = 1.0f;
            }

            for (int i = 0; i < cells; i++)
            {
                float normalized = field[i] / maxField;
                if (normalized < 0.0f)
                {
                    normalized = 0.0f;
                }
                if (normalized > 1.0f)
                {
                    normalized = 1.0f;
                }

                // Cheap temporal drift so the map feels alive without full remap churn.
                int wave = (frameIndex + (i * 3)) & 63;
                int tri = wave < 32 ? wave : 63 - wave; // 0..31..0
                float modulation = 0.85f + ((tri / 31.0f) * 0.30f); // 0.85..1.15
                normalized *= modulation;
                if (normalized > 1.0f)
                {
                    normalized = 1.0f;
                }

                field[i] = normalized;
            }
        }

        private static void DrawManifoldHeatmap(
            Canvas canvas,
            float[] normalizedField,
            float[] lastDrawnField,
            int screenWidth,
            int screenHeight,
            ref int previousStrongestCell,
            bool fullRedraw,
            int frameIndex)
        {
            if (canvas == null || normalizedField == null || lastDrawnField == null || normalizedField.Length != lastDrawnField.Length)
            {
                return;
            }

            const int cols = 32;
            const int rows = 20;
            int cellW = Math.Max(1, screenWidth / cols);
            int cellH = Math.Max(1, screenHeight / rows);
            int drawW = cols * cellW;
            int drawH = rows * cellH;
            int offX = Math.Max(0, (screenWidth - drawW) / 2);
            int offY = Math.Max(0, (screenHeight - drawH) / 2);
            int cells = cols * rows;
            if (cells != normalizedField.Length)
            {
                return;
            }

            if (fullRedraw)
            {
                canvas.Clear(Color.Black);
            }

            int strongestCell = 0;
            float strongestNorm = -1.0f;
            for (int i = 0; i < cells; i++)
            {
                float normalized = normalizedField[i];
                if (normalized > strongestNorm)
                {
                    strongestNorm = normalized;
                    strongestCell = i;
                }
            }

            for (int i = 0; i < cells; i++)
            {
                float delta = Math.Abs(normalizedField[i] - lastDrawnField[i]);
                bool valueChanged = delta >= 0.015f;
                bool valueChangedStrong = delta >= 0.060f;
                bool highlightChanged = i == strongestCell || i == previousStrongestCell;
                bool cadenceSlot = ((i + frameIndex) & 3) == 0;
                if (!fullRedraw && !highlightChanged && !valueChangedStrong && (!cadenceSlot || !valueChanged))
                {
                    continue;
                }

                int x = offX + ((i % cols) * cellW);
                int y = offY + ((i / cols) * cellH);
                Color c = ManifoldColor(normalizedField[i]);
                canvas.DrawFilledRectangle(c, x, y, cellW, cellH);
                lastDrawnField[i] = normalizedField[i];
            }

            DrawRectangleOutlineFilled(canvas, offX, offY, offX + drawW - 1, offY + drawH - 1, Color.White);
            int sx = offX + ((strongestCell % cols) * cellW);
            int sy = offY + ((strongestCell / cols) * cellH);
            DrawRectangleOutlineFilled(canvas, sx, sy, sx + cellW - 1, sy + cellH - 1, Color.Yellow);
            previousStrongestCell = strongestCell;
        }

        private static Color ManifoldColor(float normalized)
        {
            if (normalized < 0.0f)
            {
                normalized = 0.0f;
            }
            if (normalized > 1.0f)
            {
                normalized = 1.0f;
            }

            int b = 32 + (int)(223.0f * normalized);
            int g = (int)(140.0f * normalized);
            int r = (int)(40.0f * normalized);
            return Color.FromArgb(r, g, b);
        }

        private static void RenderMouseFrame(Canvas canvas, bool skipClear, out double averageBestSimilarity, out double peakBestSimilarity)
        {
            if (!skipClear)
            {
                try
                {
                    canvas.Clear(Color.Black);
                }
                catch
                {
                }
            }

            averageBestSimilarity = 0.0;
            peakBestSimilarity = 0.0;
        }

        private static void TryConfigureMouse(int screenWidth, int screenHeight)
        {
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

            DrawMouseOverlayShape(canvas, screenWidth, screenHeight, mouseX, mouseY, crossColor);
        }

        private static void EraseMouseOverlay(Canvas canvas, int screenWidth, int screenHeight, int mouseX, int mouseY)
        {
            DrawMouseOverlayShape(canvas, screenWidth, screenHeight, mouseX, mouseY, Color.Black);
        }

        private static void DrawMouseOverlayShape(Canvas canvas, int screenWidth, int screenHeight, int mouseX, int mouseY, Color crossColor)
        {
            int hLen = Math.Max(4, screenWidth / 40);
            int vLen = Math.Max(4, screenHeight / 30);
            int maxX = screenWidth - 1;
            int maxY = screenHeight - 1;
            int x = Clamp(mouseX, 0, maxX);
            int y = Clamp(mouseY, 0, maxY);

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
            int side = Math.Max(16, Math.Min(screenWidth, screenHeight) / 5);
            int x0 = Clamp((screenWidth - side) / 2, 0, screenWidth - 1);
            int y0 = Clamp((screenHeight - side) / 2, 0, screenHeight - 1);
            int x1 = Clamp(x0 + side - 1, 0, screenWidth - 1);
            int y1 = Clamp(y0 + side - 1, 0, screenHeight - 1);
            canvas.DrawFilledRectangle(Color.Blue, x0, y0, (x1 - x0) + 1, (y1 - y0) + 1);
            DrawRectangleOutlineFilled(canvas, x0, y0, x1, y1, Color.White);
        }

        private static void DrawRectangleOutlineFilled(Canvas canvas, int x0, int y0, int x1, int y1, Color color)
        {
            int width = (x1 - x0) + 1;
            int height = (y1 - y0) + 1;
            canvas.DrawFilledRectangle(color, x0, y0, width, 1);
            if (height > 1)
            {
                canvas.DrawFilledRectangle(color, x0, y1, width, 1);
            }
            if (height > 2)
            {
                int innerHeight = height - 2;
                canvas.DrawFilledRectangle(color, x0, y0 + 1, 1, innerHeight);
                if (width > 1)
                {
                    canvas.DrawFilledRectangle(color, x1, y0 + 1, 1, innerHeight);
                }
            }
        }

        private static bool TrySamplePatternPixels(Canvas canvas, int screenWidth, int screenHeight, out int topArgb, out int midArgb, out int bottomArgb)
        {
            topArgb = 0;
            midArgb = 0;
            bottomArgb = 0;
            try
            {
                int xTopLeft = Clamp(Math.Min(16, screenWidth - 1), 0, screenWidth - 1);
                int yTop = Clamp(Math.Min(16, screenHeight - 1), 0, screenHeight - 1);
                int xMid = Clamp(screenWidth / 2, 0, screenWidth - 1);
                int yMid = Clamp(screenHeight / 2, 0, screenHeight - 1);
                int yBottom = screenHeight - 1;

                topArgb = canvas.GetPointColor(xTopLeft, yTop).ToArgb();
                midArgb = canvas.GetPointColor(xMid, yMid).ToArgb();
                bottomArgb = canvas.GetPointColor(xMid, yBottom).ToArgb();
                return true;
            }
            catch
            {
                return false;
            }
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
