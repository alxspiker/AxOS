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
using System.Globalization;

namespace AxOS.Kernel
{
    public sealed class KernelLoop
    {
        public sealed class StatusSnapshot
        {
            public float EnergyBudget;
            public float MaxEnergyBudget;
            public float EnergyPercent;
            public float FatigueThreshold;
            public float ZombieActivationThreshold;
            public float FatigueRemainingRatio;
            public float ZombieActivationRatio;
            public float ZombieCriticThreshold;
            public bool ZombieModeActive;
            public int WorkingMemoryCount;
            public int WorkingMemoryCapacity;
            public long ProcessedInputs;
            public float CognitiveEntropyBuffer;
            public long SleepCycles;
            public bool SleepInterruptsLocked;
            public string LastSleepTrigger = string.Empty;
            public double SecondsSinceLastSleep;
            public double SecondsSinceLastActivity;
            public ulong TotalRamMb;
            public ulong AvailableRamMb;
            public uint UsedRamEstimate;
            public long CpuCycleHz;
            public float RecommendedPhysicalBudget;
        }

        private const float DefaultFatigueRatio = 0.28f;
        private const float DefaultZombieActivationRatio = 0.20f;
        private const float DefaultZombieCriticThreshold = 0.95f;

        private readonly HdcSystem _hdc;
        private readonly CognitiveAdapter _bioCore;
        private readonly WorkingMemoryCache _workingMemory;
        private readonly SystemMetabolism _metabolism;
        private readonly SleepCycleScheduler _sleepScheduler;
        private readonly SubstrateMonitor _substrateMonitor;
        private readonly bool _autoScaleFromSubstrate;
        private SubstrateMonitor.Snapshot _lastSubstrate;
        private long _processedInputs;

        public HdcSystem Hdc => _hdc;

        public KernelLoop(
            HdcSystem hdc,
            int cacheCapacity = 128,
            HeuristicConfig heuristicConfig = null,
            SubstrateMonitor substrateMonitor = null,
            bool autoScaleFromSubstrate = true)
        {
            _hdc = hdc ?? throw new ArgumentNullException(nameof(hdc));
            _bioCore = new CognitiveAdapter(_hdc, heuristicConfig);
            _workingMemory = new WorkingMemoryCache(cacheCapacity);
            _metabolism = new SystemMetabolism();
            _sleepScheduler = new SleepCycleScheduler();
            _substrateMonitor = substrateMonitor ?? new SubstrateMonitor();
            _autoScaleFromSubstrate = autoScaleFromSubstrate;
            _lastSubstrate = new SubstrateMonitor.Snapshot();
        }

        public void BootSequence(float maxCapacity = -1.0f, float fatigueThreshold = -1.0f, float zombieThreshold = DefaultZombieCriticThreshold)
        {
            if (maxCapacity > 0.0f)
            {
                float safeFatigue = fatigueThreshold > 0.0f
                    ? fatigueThreshold
                    : maxCapacity * DefaultFatigueRatio;
                float safeZombieCritic = zombieThreshold <= 0.0f
                    ? DefaultZombieCriticThreshold
                    : zombieThreshold;

                _metabolism.Configure(maxCapacity, safeFatigue, safeZombieCritic);
            }
            else
            {
                float fatigueRatio = fatigueThreshold > 0.0f
                    ? Clamp(fatigueThreshold, 0.01f, 0.95f)
                    : DefaultFatigueRatio;
                float safeZombieCritic = zombieThreshold <= 0.0f
                    ? DefaultZombieCriticThreshold
                    : zombieThreshold;

                BootSequenceFromSubstrate(fatigueRatio, DefaultZombieActivationRatio, safeZombieCritic);
                return;
            }

            ConfigureSchedulerAndCounters();
        }

        public void BootSequenceFromSubstrate(
            float fatigueRemainingRatio = DefaultFatigueRatio,
            float zombieActivationRatio = DefaultZombieActivationRatio,
            float zombieCriticThreshold = DefaultZombieCriticThreshold)
        {
            RefreshSubstrateState(false);
            float physicalBudget = _lastSubstrate.RecommendedKernelBudget;
            _metabolism.ConfigureRelative(
                physicalBudget,
                fatigueRemainingRatio,
                zombieActivationRatio,
                zombieCriticThreshold);

            ConfigureSchedulerAndCounters();
        }

        public void BootSequenceRelative(
            float maxCapacity,
            float fatigueRemainingRatio,
            float zombieActivationRatio,
            float zombieCriticThreshold = DefaultZombieCriticThreshold)
        {
            _metabolism.ConfigureRelative(
                maxCapacity,
                fatigueRemainingRatio,
                zombieActivationRatio,
                zombieCriticThreshold);

            ConfigureSchedulerAndCounters();
        }

        public float GetCurrentPhysicalBudget()
        {
            RefreshSubstrateState(true);
            return _lastSubstrate.RecommendedKernelBudget > 0.0f
                ? _lastSubstrate.RecommendedKernelBudget
                : _metabolism.MaxCapacity;
        }

        public float AllocateEnergyBudget(float allocationPercentage, float minimumBudget = 24.0f)
        {
            float physicalBudget = GetCurrentPhysicalBudget();
            return _substrateMonitor.AllocateFrom(physicalBudget, allocationPercentage, minimumBudget);
        }

        public SubstrateMonitor.Snapshot GetSubstrateSnapshot()
        {
            RefreshSubstrateState(true);
            return _lastSubstrate;
        }

        private void ConfigureSchedulerAndCounters()
        {
            _sleepScheduler.Configure(
                criticalSleepThresholdPercent: 0.08f,
                maxEntropyCapacity: 0.72f,
                optimalConsolidationIntervalSeconds: 120.0,
                idleWindowSeconds: 30.0);
            _sleepScheduler.Reset(DateTime.UtcNow);
            _processedInputs = 0;
        }

        public WorkingMemoryCache WorkingMemory => _workingMemory;

        public IngestResult ProcessIngestPipeline(DataStream rawInput)
        {
            RefreshSubstrateState(true);

            IngestResult result = new IngestResult();
            if (rawInput == null)
            {
                result.Error = "missing_input";
                result.Outcome = "failed";
                result.EnergyRemaining = _metabolism.CurrentEnergyBudget;
                return result;
            }

            _sleepScheduler.MarkActivity(DateTime.UtcNow);
            rawInput.DatasetType = _bioCore.NormalizeType(rawInput.DatasetType);
            result.Profile = _bioCore.AnalyzeHeuristics(rawInput);

            if (!_bioCore.L2NormalizeAndFlatten(rawInput, result.Profile, out Tensor targetTensor, out string error))
            {
                result.Error = error;
                result.Outcome = "encode_failed";
                result.EnergyRemaining = _metabolism.CurrentEnergyBudget;
                return result;
            }

            _hdc.Remember(targetTensor, out _);

            if (_workingMemory.CosineSimilarityHit(targetTensor, result.Profile.System1SimilarityThreshold, out WorkingMemoryCache.CacheEntry hit, out float similarity))
            {
                ExecuteReflex(targetTensor, rawInput, hit, similarity, result);
            }
            else
            {
                EngageDeepThinking(targetTensor, result.Profile, rawInput, result);
            }

            _processedInputs++;

            if (TryAutoSleep(false, out SleepCycleScheduler.SleepTriggerReason sleepReason))
            {
                result.SleepTriggered = true;
                result.SleepReason = SleepCycleScheduler.FormatReason(sleepReason);
            }

            result.EnergyRemaining = _metabolism.CurrentEnergyBudget;
            return result;
        }

        public void TriggerSleepCycle(SleepCycleScheduler.SleepTriggerReason reason = SleepCycleScheduler.SleepTriggerReason.Manual)
        {
            _sleepScheduler.LockHardwareInterrupts(reason);
            _bioCore.ConsolidateMemory(_workingMemory);
            _workingMemory.ClearAnomalies();
            _workingMemory.ApplyTimeDecay(0.93f, 0.20f);
            _metabolism.Recharge();
            _sleepScheduler.CompleteSleep(DateTime.UtcNow);
        }

        public void ClearWorkingMemory()
        {
            _workingMemory.Clear();
        }

        public bool PollSleepScheduler(bool systemIsIdle, out SleepCycleScheduler.SleepTriggerReason reason)
        {
            return TryAutoSleep(systemIsIdle, out reason);
        }

        public StatusSnapshot GetStatus()
        {
            RefreshSubstrateState(true);

            SleepCycleScheduler.Snapshot scheduler = _sleepScheduler.GetSnapshot(DateTime.UtcNow);
            return new StatusSnapshot
            {
                EnergyBudget = _metabolism.CurrentEnergyBudget,
                MaxEnergyBudget = _metabolism.MaxCapacity,
                EnergyPercent = _metabolism.EnergyPercent,
                FatigueThreshold = _metabolism.FatigueThreshold,
                ZombieActivationThreshold = _metabolism.ZombieActivationThreshold,
                FatigueRemainingRatio = _metabolism.FatigueRemainingRatio,
                ZombieActivationRatio = _metabolism.ZombieActivationRatio,
                ZombieCriticThreshold = _metabolism.ZombieCriticThreshold,
                ZombieModeActive = _metabolism.ZombieModeActive,
                WorkingMemoryCount = _workingMemory.Count,
                WorkingMemoryCapacity = _workingMemory.Capacity,
                ProcessedInputs = _processedInputs,
                CognitiveEntropyBuffer = scheduler.CognitiveEntropyBuffer,
                SleepCycles = scheduler.SleepCycles,
                SleepInterruptsLocked = scheduler.InterruptsLocked,
                LastSleepTrigger = SleepCycleScheduler.FormatReason(scheduler.LastTrigger),
                SecondsSinceLastSleep = scheduler.SecondsSinceLastSleep,
                SecondsSinceLastActivity = scheduler.SecondsSinceLastActivity,
                TotalRamMb = _lastSubstrate.TotalRamMb,
                AvailableRamMb = _lastSubstrate.AvailableRamMb,
                UsedRamEstimate = _lastSubstrate.UsedRamEstimate,
                CpuCycleHz = _lastSubstrate.CpuCycleHz,
                RecommendedPhysicalBudget = _lastSubstrate.RecommendedKernelBudget
            };
        }

        private void ExecuteReflex(
            Tensor targetTensor,
            DataStream rawInput,
            WorkingMemoryCache.CacheEntry hit,
            float similarity,
            IngestResult result)
        {
            _metabolism.Consume(1.0f + (result.Profile.DeepThinkCostBias * 0.15f));

            string cacheKey = BuildCacheKey(rawInput);
            float stability = similarity;
            if (stability < hit.Fitness)
            {
                stability = hit.Fitness;
            }

            float normalizedBurn = (1.0f + (result.Profile.DeepThinkCostBias * 0.15f)) / Math.Max(1.0f, _metabolism.MaxCapacity);
            _workingMemory.PromoteToCache(cacheKey, targetTensor, stability, rawInput.DatasetType, rawInput.DatasetId, normalizedBurn);

            result.Success = true;
            result.ReflexHit = true;
            result.DeepThinkPath = false;
            result.ZombieTriggered = false;
            result.Iterations = 0;
            result.Outcome = "system1_reflex";
            result.Similarity = similarity;
            result.CacheKey = cacheKey;
        }

        private void EngageDeepThinking(
            Tensor targetTensor,
            SignalProfile profile,
            DataStream rawInput,
            IngestResult result)
        {
            const int MaxIterations = 64;
            List<WorkingMemoryCache.CacheEntry> memoryCandidates = _workingMemory.SnapshotByPriority(12);
            int iterations = 0;
            TensorOpCandidate best = null;

            while (_metabolism.CanDeepThink && iterations < MaxIterations)
            {
                TensorOpCandidate candidate = _bioCore.RouteDynamicConnectome(
                    targetTensor,
                    profile,
                    memoryCandidates,
                    iterations);

                float metabolicCost = _bioCore.CalculateThermodynamicCost(candidate, profile);
                _metabolism.Consume(metabolicCost);
                iterations++;

                if (best == null || candidate.Fitness > best.Fitness)
                {
                    best = candidate;
                }

                if (_bioCore.PassesCriticThreshold(candidate, profile, _metabolism))
                {
                    string cacheKey = BuildCacheKey(rawInput);
                    float normalizedBurn = metabolicCost / Math.Max(1.0f, _metabolism.MaxCapacity);
                    _workingMemory.PromoteToCache(cacheKey, candidate.Candidate, candidate.Fitness, rawInput.DatasetType, rawInput.DatasetId, normalizedBurn);

                    result.Success = true;
                    result.ReflexHit = false;
                    result.DeepThinkPath = true;
                    result.ZombieTriggered = false;
                    result.Iterations = iterations;
                    result.Outcome = "system2_volatile_hit";
                    result.Similarity = candidate.Similarity;
                    result.CacheKey = cacheKey;

                    if (candidate.Strategy == "discovery_induction")
                    {
                        result.DiscoveryTriggered = true;
                        // Register this as a semantic anomaly for sleep consolidation
                        // We simulate a target state by shifting the input (mocking induction)
                        Tensor requiredNext = TensorOps.Permute(targetTensor, 42); 
                        Tensor derived = _bioCore.DeduceGeometricGap(targetTensor, requiredNext);
                        _workingMemory.FlagAnomaly(cacheKey, derived);
                    }
                    return;
                }
            }

            bool shouldZombie = _metabolism.CurrentEnergyBudget <= _metabolism.ZombieActivationThreshold || _metabolism.IsExhausted;
            if (shouldZombie)
            {
                _metabolism.TriggerZombieMode();
            }

            result.Success = false;
            result.ReflexHit = false;
            result.DeepThinkPath = true;
            result.ZombieTriggered = shouldZombie;
            result.Iterations = iterations;
            result.Outcome = shouldZombie ? "zombie_mode" : "fatigue_limit";
            result.Similarity = best == null ? 0.0f : best.Similarity;
            result.Error = shouldZombie ? "critic_threshold_not_met" : "fatigue_threshold_reached";
            result.CacheKey = BuildCacheKey(rawInput);
        }

        private bool TryAutoSleep(bool systemIsIdle, out SleepCycleScheduler.SleepTriggerReason reason)
        {
            if (_sleepScheduler.MonitorMetabolicLoad(_metabolism, _workingMemory, systemIsIdle, DateTime.UtcNow, out reason))
            {
                TriggerSleepCycle(reason);
                return true;
            }

            return false;
        }

        private static string BuildCacheKey(DataStream input)
        {
            string type = (input.DatasetType ?? "text").Trim().ToLowerInvariant();
            string id = (input.DatasetId ?? string.Empty).Trim().ToLowerInvariant();
            string payload = input.Payload ?? string.Empty;

            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < payload.Length; i++)
            {
                hash ^= payload[i];
                hash *= 1099511628211UL;
            }

            string idPart = id.Length == 0 ? "anon" : id;
            return type + "_" + idPart + "_" + hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        private void RefreshSubstrateState(bool preserveEnergyPercent)
        {
            if (_substrateMonitor == null)
            {
                return;
            }

            _lastSubstrate = _substrateMonitor.Capture();
            if (_autoScaleFromSubstrate && _lastSubstrate.RecommendedKernelBudget > 0.0f)
            {
                _metabolism.RescaleMaxCapacity(_lastSubstrate.RecommendedKernelBudget, preserveEnergyPercent);
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
    }
}

