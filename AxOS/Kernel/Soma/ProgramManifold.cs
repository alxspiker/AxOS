// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using System;
using System.Collections.Generic;
using AxOS.Core;
using AxOS.Brain;
using AxOS.Kernel;
using AxOS.Hardware;
using AxOS.Storage;
using AxOS.Diagnostics;

namespace AxOS.Kernel
{
    public sealed class ProgramManifold
    {
        public sealed class Configuration
        {
            public string Name = "app";
            public int CacheCapacity = 64;
            public float AllocationPercentage = 0.15f;
            public float MinimumAllocation = 24.0f;
            public HeuristicConfig Heuristics = new HeuristicConfig();
        }

        private readonly HdcSystem _hdc;
        private readonly KernelLoop _kernelLoop;
        private readonly BatchController _batchController;
        private readonly Ruleset _localRuleset;

        public string Name { get; }
        public float AllocatedEnergyBudget { get; }

        public ProgramManifold(KernelLoop hostKernelLoop, Ruleset ruleset, Configuration config = null)
        {
            if (hostKernelLoop == null) throw new ArgumentNullException(nameof(hostKernelLoop));
            if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));

            Configuration safe = config ?? new Configuration();
            Name = string.IsNullOrWhiteSpace(safe.Name) ? "app" : safe.Name.Trim();

            _hdc = hostKernelLoop.Hdc;
            _localRuleset = ruleset;
            
            foreach (KeyValuePair<string, Tensor> kvp in ruleset.SymbolDefinitions)
            {
                _hdc.Symbols.Register(kvp.Key, kvp.Value, out _);
            }

            HeuristicConfig heuristics = ruleset.Heuristics.Copy();
            _kernelLoop = new KernelLoop(_hdc, safe.CacheCapacity, heuristics, null, false);
            _batchController = new BatchController();

            AllocatedEnergyBudget = hostKernelLoop.AllocateEnergyBudget(
                safe.AllocationPercentage,
                safe.MinimumAllocation);

            _kernelLoop.BootSequenceRelative(
                AllocatedEnergyBudget,
                heuristics.FatigueRemainingRatio,
                heuristics.ZombieActivationRatio,
                heuristics.ZombieCriticThreshold);
        }

        public ProgramManifold(KernelLoop hostKernelLoop, Configuration config = null)
        {
            if (hostKernelLoop == null)
            {
                throw new ArgumentNullException(nameof(hostKernelLoop));
            }

            Configuration safe = config ?? new Configuration();
            Name = string.IsNullOrWhiteSpace(safe.Name) ? "app" : safe.Name.Trim();

            _hdc = hostKernelLoop.Hdc;
            HeuristicConfig heuristics = (safe.Heuristics ?? new HeuristicConfig()).Copy();
            _kernelLoop = new KernelLoop(_hdc, safe.CacheCapacity, heuristics, null, false);
            _batchController = new BatchController();

            AllocatedEnergyBudget = hostKernelLoop.AllocateEnergyBudget(
                safe.AllocationPercentage,
                safe.MinimumAllocation);

            _kernelLoop.BootSequenceRelative(
                AllocatedEnergyBudget,
                heuristics.FatigueRemainingRatio,
                heuristics.ZombieActivationRatio,
                heuristics.ZombieCriticThreshold);
        }

        public void Enqueue(DataStream item)
        {
            _batchController.Enqueue(item);
        }

        public void ClearQueue()
        {
            _batchController.Clear();
        }

        public BatchController.BatchRunResult RunBatch(int maxItems)
        {
            return _batchController.Run(_kernelLoop, maxItems);
        }

        public IngestResult Ingest(DataStream input)
        {
            return _kernelLoop.ProcessIngestPipeline(input);
        }

        public void Sleep()
        {
            EvolveRulesetDuringSleep();
            _kernelLoop.TriggerSleepCycle(SleepCycleScheduler.SleepTriggerReason.Manual);
        }

        private void EvolveRulesetDuringSleep()
        {
            if (_localRuleset == null) return;

            var anomalies = _kernelLoop.WorkingMemory.GetAnomalies();
            foreach (var anomaly in anomalies)
            {
                // Mutate the local DNA (Ruleset)
                _localRuleset.SymbolDefinitions[anomaly.Key] = anomaly.DeducedConstraint ?? anomaly.Value;
                _localRuleset.ReflexTriggers.Add(new ReflexTrigger
                {
                    TargetSymbol = anomaly.Key,
                    SimilarityThreshold = _localRuleset.Heuristics.CriticMin,
                    ActionIntent = "execute_geometric_shift"
                });

                _kernelLoop.WorkingMemory.PromoteToCache(
                    anomaly.Key,
                    anomaly.DeducedConstraint ?? anomaly.Value,
                    0.99f,
                    "x86_64_Discovery",
                    anomaly.Key,
                    0.0f);
            }
        }

        public KernelLoop.StatusSnapshot GetStatus()
        {
            return _kernelLoop.GetStatus();
        }

        public void Tick(bool idle)
        {
            _kernelLoop.PollSleepScheduler(idle, out _);
        }
    }
}

