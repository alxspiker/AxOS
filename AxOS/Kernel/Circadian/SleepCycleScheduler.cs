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

namespace AxOS.Kernel
{
    public sealed class SleepCycleScheduler
    {
        public enum SleepTriggerReason
        {
            None = 0,
            Manual = 1,
            MetabolicDrain = 2,
            CognitiveOverload = 3,
            IdleConsolidation = 4
        }

        public static string FormatReason(SleepTriggerReason reason)
        {
            switch (reason)
            {
                case SleepTriggerReason.Manual:
                    return "manual";
                case SleepTriggerReason.MetabolicDrain:
                    return "metabolic_drain";
                case SleepTriggerReason.CognitiveOverload:
                    return "cognitive_overload";
                case SleepTriggerReason.IdleConsolidation:
                    return "idle_consolidation";
                default:
                    return "none";
            }
        }

        public sealed class Snapshot
        {
            public float CognitiveEntropyBuffer;
            public float CriticalSleepThresholdPercent;
            public float MaxEntropyCapacity;
            public double OptimalConsolidationIntervalSeconds;
            public double IdleWindowSeconds;
            public double SecondsSinceLastSleep;
            public double SecondsSinceLastActivity;
            public bool InterruptsLocked;
            public long SleepCycles;
            public SleepTriggerReason LastTrigger;
        }

        private float _cognitiveEntropyBuffer;
        private float _criticalSleepThresholdPercent = 0.08f;
        private float _maxEntropyCapacity = 0.72f;
        private double _optimalConsolidationIntervalSeconds = 120.0;
        private double _idleWindowSeconds = 30.0;
        private DateTime _lastSleepUtc = DateTime.MinValue;
        private DateTime _lastActivityUtc = DateTime.MinValue;
        private bool _interruptsLocked;
        private long _sleepCycles;
        private SleepTriggerReason _lastTrigger = SleepTriggerReason.None;

        public bool InterruptsLocked => _interruptsLocked;

        public void Configure(
            float criticalSleepThresholdPercent = 0.08f,
            float maxEntropyCapacity = 0.72f,
            double optimalConsolidationIntervalSeconds = 120.0,
            double idleWindowSeconds = 30.0)
        {
            _criticalSleepThresholdPercent = Clamp(criticalSleepThresholdPercent, 0.01f, 0.95f);
            _maxEntropyCapacity = Clamp(maxEntropyCapacity, 0.05f, 1.0f);
            _optimalConsolidationIntervalSeconds = ClampDouble(optimalConsolidationIntervalSeconds, 1.0, 86400.0);
            _idleWindowSeconds = ClampDouble(idleWindowSeconds, 0.0, 86400.0);
        }

        public void Reset(DateTime nowUtc)
        {
            _cognitiveEntropyBuffer = 0.0f;
            _lastSleepUtc = nowUtc;
            _lastActivityUtc = nowUtc;
            _interruptsLocked = false;
            _sleepCycles = 0;
            _lastTrigger = SleepTriggerReason.None;
        }

        public void MarkActivity(DateTime nowUtc)
        {
            _lastActivityUtc = nowUtc;
        }

        public bool MonitorMetabolicLoad(
            SystemMetabolism metabolism,
            WorkingMemoryCache workingMemory,
            bool systemIsIdle,
            DateTime nowUtc,
            out SleepTriggerReason reason)
        {
            _cognitiveEntropyBuffer = CalculateManifoldEntropy(workingMemory);
            reason = ShouldTriggerSleep(metabolism, systemIsIdle, nowUtc);
            if (reason == SleepTriggerReason.None)
            {
                return false;
            }

            LockHardwareInterrupts(reason);
            return true;
        }

        public void LockHardwareInterrupts(SleepTriggerReason reason)
        {
            _interruptsLocked = true;
            _sleepCycles++;
            _lastTrigger = reason;
        }

        public void CompleteSleep(DateTime nowUtc)
        {
            _cognitiveEntropyBuffer = 0.0f;
            _lastSleepUtc = nowUtc;
            _lastActivityUtc = nowUtc;
            _interruptsLocked = false;
        }

        public Snapshot GetSnapshot(DateTime nowUtc)
        {
            return new Snapshot
            {
                CognitiveEntropyBuffer = _cognitiveEntropyBuffer,
                CriticalSleepThresholdPercent = _criticalSleepThresholdPercent,
                MaxEntropyCapacity = _maxEntropyCapacity,
                OptimalConsolidationIntervalSeconds = _optimalConsolidationIntervalSeconds,
                IdleWindowSeconds = _idleWindowSeconds,
                SecondsSinceLastSleep = Math.Max(0.0, (nowUtc - _lastSleepUtc).TotalSeconds),
                SecondsSinceLastActivity = Math.Max(0.0, (nowUtc - _lastActivityUtc).TotalSeconds),
                InterruptsLocked = _interruptsLocked,
                SleepCycles = _sleepCycles,
                LastTrigger = _lastTrigger
            };
        }

        private SleepTriggerReason ShouldTriggerSleep(SystemMetabolism metabolism, bool systemIsIdle, DateTime nowUtc)
        {
            if (metabolism == null)
            {
                return SleepTriggerReason.None;
            }

            if (metabolism.EnergyPercent < _criticalSleepThresholdPercent)
            {
                return SleepTriggerReason.MetabolicDrain;
            }

            if (_cognitiveEntropyBuffer > _maxEntropyCapacity)
            {
                return SleepTriggerReason.CognitiveOverload;
            }

            double secondsSinceSleep = (nowUtc - _lastSleepUtc).TotalSeconds;
            double secondsSinceActivity = (nowUtc - _lastActivityUtc).TotalSeconds;
            if (systemIsIdle &&
                secondsSinceSleep >= _optimalConsolidationIntervalSeconds &&
                secondsSinceActivity >= _idleWindowSeconds)
            {
                return SleepTriggerReason.IdleConsolidation;
            }

            return SleepTriggerReason.None;
        }

        private float CalculateManifoldEntropy(WorkingMemoryCache workingMemory)
        {
            if (workingMemory == null || workingMemory.Count == 0)
            {
                return 0.0f;
            }

            List<WorkingMemoryCache.CacheEntry> entries = workingMemory.SnapshotByPriority(64);
            if (entries.Count == 0)
            {
                return 0.0f;
            }

            float totalWeight = 0.0f;
            int unresolvedCount = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                WorkingMemoryCache.CacheEntry entry = entries[i];
                float weight = Math.Max(0.01f, entry.Fitness) * (1.0f / (1.0f + entry.Hits));
                totalWeight += weight;

                bool unresolved = entry.Hits == 0 || entry.Fitness > 0.90f;
                if (unresolved)
                {
                    unresolvedCount++;
                }
            }

            if (totalWeight <= 0.0f)
            {
                return 0.0f;
            }

            float concentration = 0.0f;
            for (int i = 0; i < entries.Count; i++)
            {
                WorkingMemoryCache.CacheEntry entry = entries[i];
                float weight = Math.Max(0.01f, entry.Fitness) * (1.0f / (1.0f + entry.Hits));
                float p = weight / totalWeight;
                concentration += p * p;
            }

            float diversity = Clamp(1.0f - concentration, 0.0f, 1.0f);
            float load = workingMemory.Capacity <= 0
                ? 0.0f
                : Clamp((float)entries.Count / workingMemory.Capacity, 0.0f, 1.0f);
            float unresolvedRatio = Clamp((float)unresolvedCount / Math.Max(1, entries.Count), 0.0f, 1.0f);

            return Clamp((diversity * 0.55f) + (load * 0.30f) + (unresolvedRatio * 0.15f), 0.0f, 1.0f);
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

        private static double ClampDouble(double value, double min, double max)
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

