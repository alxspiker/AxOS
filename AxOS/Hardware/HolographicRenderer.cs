// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using System;
using System.Drawing;
using AxOS.Core;
using AxOS.Kernel;
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

        public bool RunManifoldPreview(ProgramManifold manifold, out RenderReport report, out string error)
        {
            report = new RenderReport();
            error = string.Empty;

            if (manifold == null)
            {
                error = "missing_manifold";
                return false;
            }

            var snapshot = manifold.SnapshotWorkingMemory(1);
            if (snapshot == null || snapshot.Count == 0)
            {
                error = "manifold_empty";
                return false;
            }

            Tensor manifoldTensor = snapshot[0].Value == null ? null : snapshot[0].Value.Flatten();
            if (manifoldTensor == null || manifoldTensor.IsEmpty)
            {
                error = "manifold_tensor_empty";
                return false;
            }

            Canvas canvas = null;
            DateTime startedUtc = DateTime.UtcNow;
            try
            {
                const int seconds = 20;
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

                DrawManifoldHeatmap(canvas, manifoldTensor, screenWidth, screenHeight);

                bool patternProbeCaptured = TrySamplePatternPixels(
                    canvas,
                    screenWidth,
                    screenHeight,
                    out int patternTopArgb,
                    out int patternMidArgb,
                    out int patternBottomArgb);

                bool exitedByKey = false;
                while ((DateTime.UtcNow - startedUtc).TotalSeconds < seconds)
                {
                    if (Sys.KeyboardManager.KeyAvailable && Sys.KeyboardManager.TryReadKey(out Sys.KeyEvent keyEvent))
                    {
                        char keyChar = keyEvent.KeyChar;
                        if (keyChar == (char)27 || keyChar == '\r' || keyChar == '\n' || keyChar == 'q' || keyChar == 'Q')
                        {
                            exitedByKey = true;
                            break;
                        }
                    }
                }

                TimeSpan elapsed = DateTime.UtcNow - startedUtc;
                report.RenderedFrames = 1;
                report.TargetFrames = 1;
                report.EncodedPoints = manifoldTensor.Total;
                report.Dim = manifoldTensor.Total;
                report.ScreenWidth = screenWidth;
                report.ScreenHeight = screenHeight;
                report.LogicalWidth = 40;
                report.LogicalHeight = 25;
                report.TargetFps = 1;
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

        private static void DrawManifoldHeatmap(Canvas canvas, Tensor tensor, int screenWidth, int screenHeight)
        {
            canvas.Clear(Color.Black);

            const int cols = 40;
            const int rows = 25;
            int cellW = Math.Max(1, screenWidth / cols);
            int cellH = Math.Max(1, screenHeight / rows);
            int drawW = cols * cellW;
            int drawH = rows * cellH;
            int offX = Math.Max(0, (screenWidth - drawW) / 2);
            int offY = Math.Max(0, (screenHeight - drawH) / 2);

            float min = tensor.Data[0];
            float max = tensor.Data[0];
            for (int i = 1; i < tensor.Data.Length; i++)
            {
                float value = tensor.Data[i];
                if (value < min)
                {
                    min = value;
                }
                if (value > max)
                {
                    max = value;
                }
            }

            float range = max - min;
            if (range <= 0.000001f)
            {
                range = 1.0f;
            }

            int cells = cols * rows;
            int strongestCell = 0;
            float strongestNorm = -1.0f;
            for (int i = 0; i < cells; i++)
            {
                int tensorIndex = (int)(((long)i * tensor.Total) / cells);
                if (tensorIndex >= tensor.Total)
                {
                    tensorIndex = tensor.Total - 1;
                }

                float normalized = (tensor.Data[tensorIndex] - min) / range;
                if (normalized > strongestNorm)
                {
                    strongestNorm = normalized;
                    strongestCell = i;
                }

                int x = offX + ((i % cols) * cellW);
                int y = offY + ((i / cols) * cellH);
                Color c = ManifoldColor(normalized);
                canvas.DrawFilledRectangle(c, x, y, cellW, cellH);
            }

            DrawRectangleOutlineFilled(canvas, offX, offY, offX + drawW - 1, offY + drawH - 1, Color.White);
            int sx = offX + ((strongestCell % cols) * cellW);
            int sy = offY + ((strongestCell / cols) * cellH);
            DrawRectangleOutlineFilled(canvas, sx, sy, sx + cellW - 1, sy + cellH - 1, Color.Yellow);
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
