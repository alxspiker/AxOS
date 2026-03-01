// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using Cosmos.HAL.Drivers.Video.SVGAII;
using Cosmos.System.Graphics;
using IL2CPU.API.Attribs;

namespace AxOS.Hardware
{
    // Compatibility path for environments where SVGAIICanvas.Display() double-buffer copy faults.
    [Plug(Target = typeof(SVGAIICanvas))]
    internal static class SVGAIICanvasPlugs
    {
        public static void DrawImage(
            SVGAIICanvas aThis,
            Image image,
            int x,
            int y,
            bool preventOffBoundPixels,
            [FieldAccess(Name = "Cosmos.HAL.Drivers.Video.SVGAII.VMWareSVGAII Cosmos.System.Graphics.SVGAIICanvas.driver")]
            ref VMWareSVGAII driver)
        {
            if (aThis == null || image == null || driver == null || driver.videoMemory == null)
            {
                return;
            }

            int screenW = (int)aThis.Mode.Width;
            int screenH = (int)aThis.Mode.Height;
            int width = (int)image.Width;
            int height = (int)image.Height;
            int[] data = image.RawData;
            if (screenW <= 0 || screenH <= 0 || width <= 0 || height <= 0 || data == null)
            {
                return;
            }

            int startX = x;
            int startY = y;
            int sourceX = 0;
            int sourceY = 0;
            int copyW = width;
            int copyH = height;

            // Always clip to prevent out-of-range writes on strict VRAM bounds.
            if (preventOffBoundPixels || x < 0 || y < 0 || x + width > screenW || y + height > screenH)
            {
                startX = x < 0 ? 0 : x;
                startY = y < 0 ? 0 : y;
                sourceX = x < 0 ? -x : 0;
                sourceY = y < 0 ? -y : 0;
                copyW = width - sourceX;
                copyH = height - sourceY;

                int maxW = screenW - startX;
                int maxH = screenH - startY;
                if (copyW > maxW) copyW = maxW;
                if (copyH > maxH) copyH = maxH;
            }

            if (copyW <= 0 || copyH <= 0)
            {
                return;
            }

            int frameBase = (int)driver.FrameOffset;
            int bytesPerLine = screenW * 4;
            int rowBytes = copyW * 4;
            int vramSize = (int)driver.videoMemory.Size;

            for (int row = 0; row < copyH; row++)
            {
                int srcIndex = (sourceY + row) * width + sourceX;
                int dstByte = frameBase + ((startY + row) * bytesPerLine) + (startX * 4);
                if (dstByte < 0 || dstByte + rowBytes > vramSize)
                {
                    continue;
                }

                driver.videoMemory.Copy(dstByte, data, srcIndex, copyW);
            }
        }

        public static void Display(SVGAIICanvas aThis)
        {
            // No-op on this environment: stock DoubleBufferUpdate path faults.
        }
    }
}
