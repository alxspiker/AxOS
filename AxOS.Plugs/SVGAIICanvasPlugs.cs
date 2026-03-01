// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using Cosmos.HAL.Drivers.PCI.Video;
using Cosmos.System.Graphics;
using IL2CPU.API.Attribs;

namespace AxOS.Hardware
{
    // AuraOS-style scanout path: copy rows directly and force full-frame update.
    [Plug(Target = typeof(SVGAIICanvas))]
    internal static class SVGAIICanvasPlugs
    {
        private static uint[] _rowScratch = new uint[0];

        public static void DrawImage(
            SVGAIICanvas aThis,
            Image image,
            int x,
            int y,
            [FieldAccess(Name = "Cosmos.HAL.Drivers.PCI.Video.VMWareSVGAII Cosmos.System.Graphics.SVGAIICanvas._xSVGADriver")]
            ref VMWareSVGAII driver)
        {
            if (image == null || driver == null || driver.VideoMemory == null)
            {
                return;
            }

            int screenW = aThis.Mode.Columns;
            int screenH = aThis.Mode.Rows;
            int width = (int)image.Width;
            int height = (int)image.Height;
            int[] data = image.rawData;
            if (screenW <= 0 || screenH <= 0 || width <= 0 || height <= 0 || data == null)
            {
                return;
            }

            // Fast path when image fully fits on-screen: row copies to VRAM.
            if (x >= 0 && y >= 0 && (x + width) <= screenW && (y + height) <= screenH)
            {
                if (_rowScratch == null || _rowScratch.Length < width)
                {
                    _rowScratch = new uint[width];
                }

                for (int row = 0; row < height; row++)
                {
                    int src = row * width;
                    for (int i = 0; i < width; i++)
                    {
                        _rowScratch[i] = unchecked((uint)data[src + i]);
                    }
                    uint frameOffset = driver.FrameOffset;
                    uint frameSize = driver.FrameSize;
                    int dstPixel = ((y + row) * screenW) + x;
                    uint dstByte = frameOffset + (uint)(dstPixel * 4);
                    uint endByte = (uint)(width * 4);
                    if (frameSize > 0U)
                    {
                        uint rel = dstByte - frameOffset;
                        if (rel > frameSize || endByte > frameSize - rel)
                        {
                            continue;
                        }
                    }
                    driver.VideoMemory.Copy(dstByte, _rowScratch, 0, width);
                }
                driver.Update((uint)x, (uint)y, (uint)width, (uint)height);
                return;
            }

            // Clipped fallback.
            for (int row = 0; row < height; row++)
            {
                int dy = y + row;
                if (dy < 0 || dy >= screenH)
                {
                    continue;
                }

                int src = row * width;
                for (int col = 0; col < width; col++)
                {
                    int dx = x + col;
                    if (dx < 0 || dx >= screenW)
                    {
                        continue;
                    }

                    int idx = src + col;
                    if (idx < 0 || idx >= data.Length)
                    {
                        continue;
                    }

                    driver.SetPixel((uint)dx, (uint)dy, unchecked((uint)data[idx]));
                }
            }
            driver.Update(0U, 0U, (uint)screenW, (uint)screenH);
        }

        public static void Display(
            SVGAIICanvas aThis,
            [FieldAccess(Name = "Cosmos.HAL.Drivers.PCI.Video.VMWareSVGAII Cosmos.System.Graphics.SVGAIICanvas._xSVGADriver")]
            ref VMWareSVGAII driver)
        {
            if (aThis == null || driver == null)
            {
                return;
            }

            int screenW = aThis.Mode.Columns;
            int screenH = aThis.Mode.Rows;
            if (screenW <= 0 || screenH <= 0)
            {
                return;
            }

            driver.Update(0U, 0U, (uint)screenW, (uint)screenH);
        }
    }
}
