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
using Cosmos.Core;
using Cosmos.HAL;

namespace AxOS.Hardware
{
    public sealed class SubstrateMonitor
    {
        public sealed class Snapshot
        {
            public ulong TotalRamMb;
            public ulong AvailableRamMb;
            public uint UsedRamEstimate;
            public long CpuCycleHz;
            public ulong CpuUptimeTicks;
            public byte RtcHour;
            public byte RtcMinute;
            public byte RtcSecond;
            public float RecommendedKernelBudget;
        }

        public Snapshot Capture()
        {
            Snapshot snapshot = new Snapshot
            {
                TotalRamMb = SafeReadTotalRamMb(),
                AvailableRamMb = SafeReadAvailableRamMb(),
                UsedRamEstimate = SafeReadUsedRamEstimate(),
                CpuCycleHz = 0,
                CpuUptimeTicks = 0,
                RtcHour = SafeReadRtcHour(),
                RtcMinute = SafeReadRtcMinute(),
                RtcSecond = SafeReadRtcSecond()
            };

            snapshot.RecommendedKernelBudget = ComputeKernelBudget(snapshot);
            return snapshot;
        }

        public float ComputeKernelBudget(Snapshot snapshot)
        {
            if (snapshot == null)
            {
                return 256.0f;
            }

            float totalRamMb = snapshot.TotalRamMb > 0 ? snapshot.TotalRamMb : 128.0f;
            float availableRamMb = snapshot.AvailableRamMb > 0
                ? snapshot.AvailableRamMb
                : totalRamMb;
            if (availableRamMb > totalRamMb)
            {
                availableRamMb = totalRamMb;
            }

            float ramAvailabilityRatio = Clamp01(availableRamMb / Math.Max(1.0f, totalRamMb));

            double availableBytes = snapshot.AvailableRamMb <= 0
                ? totalRamMb * 1024.0 * 1024.0
                : snapshot.AvailableRamMb * 1024.0 * 1024.0;
            float usedRatio = availableBytes <= 0.0
                ? 0.0f
                : Clamp01((float)(snapshot.UsedRamEstimate / availableBytes));

            float cpuGhz = 1.0f;
            if (snapshot.CpuCycleHz > 0)
            {
                cpuGhz = Clamp((float)snapshot.CpuCycleHz / 1_000_000_000.0f, 0.20f, 6.00f);
            }

            float ramTerm = 40.0f + (totalRamMb * 0.95f);
            float cpuTerm = 55.0f + (cpuGhz * 140.0f);

            float pressureScale = (0.35f + (ramAvailabilityRatio * 0.65f)) * (1.0f - (usedRatio * 0.55f));

            float uptimeScale = 1.0f;
            if (snapshot.CpuCycleHz > 0 && snapshot.CpuUptimeTicks > 0)
            {
                double uptimeSeconds = snapshot.CpuUptimeTicks / (double)snapshot.CpuCycleHz;
                float normalizedUptime = Clamp((float)(uptimeSeconds / 86400.0), 0.0f, 30.0f);
                uptimeScale = 1.0f - Clamp(normalizedUptime * 0.08f, 0.0f, 0.08f);
            }

            float budget = (ramTerm + cpuTerm) * pressureScale * uptimeScale;
            return Clamp(budget, 64.0f, 8192.0f);
        }

        public float AllocateFrom(float kernelBudget, float allocationPercentage, float minimumBudget = 24.0f)
        {
            float safeKernelBudget = kernelBudget <= 0.0f ? 64.0f : kernelBudget;
            float ratio = Clamp01(allocationPercentage);
            if (ratio < 0.01f)
            {
                ratio = 0.01f;
            }

            float requested = safeKernelBudget * ratio;
            float minimum = minimumBudget <= 0.0f ? 16.0f : minimumBudget;
            if (requested < minimum)
            {
                requested = minimum;
            }
            if (requested > safeKernelBudget)
            {
                requested = safeKernelBudget;
            }

            return requested;
        }

        private static ulong SafeReadTotalRamMb()
        {
            try
            {
                return CPU.GetAmountOfRAM();
            }
            catch
            {
                return 128;
            }
        }

        private static ulong SafeReadAvailableRamMb()
        {
            try
            {
                return GCImplementation.GetAvailableRAM();
            }
            catch
            {
                return 0;
            }
        }

        private static uint SafeReadUsedRamEstimate()
        {
            try
            {
                return GCImplementation.GetUsedRAM();
            }
            catch
            {
                return 0;
            }
        }

        private static byte SafeReadRtcHour()
        {
            try
            {
                return RTC.Hour;
            }
            catch
            {
                return 0;
            }
        }

        private static byte SafeReadRtcMinute()
        {
            try
            {
                return RTC.Minute;
            }
            catch
            {
                return 0;
            }
        }

        private static byte SafeReadRtcSecond()
        {
            try
            {
                return RTC.Second;
            }
            catch
            {
                return 0;
            }
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

        private static float Clamp01(float value)
        {
            return Clamp(value, 0.0f, 1.0f);
        }
    }
}

