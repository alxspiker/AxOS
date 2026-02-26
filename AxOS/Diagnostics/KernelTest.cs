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
using System.Globalization;

namespace AxOS.Diagnostics
{
    public static class KernelTest
    {
        public static void RunBiologicalStressTest(
            KernelLoop kernelLoop,
            BatchController batchController,
            HardwareSynapse hardwareSynapse = null,
            Action<string> writeLine = null,
            Action onComplete = null)
        {
            RunKernelDiagnostics(kernelLoop, batchController, hardwareSynapse, writeLine, onComplete);
        }

        public static void RunKernelDiagnostics(
            KernelLoop kernelLoop,
            BatchController batchController,
            HardwareSynapse hardwareSynapse = null,
            Action<string> writeLine = null,
            Action onComplete = null)
        {
            Action<string> log = writeLine ?? Console.WriteLine;

            if (kernelLoop == null || batchController == null)
            {
                log("test_error: missing_dependencies");
                return;
            }

            log("=== KERNEL DIAGNOSTICS START ===");

            kernelLoop.BootSequenceFromSubstrate(0.28f, 0.20f, 0.95f);
            batchController.Clear();

            log(string.Empty);
            log("[SUBSTRATE]");
            SubstrateMonitor.Snapshot substrate = kernelLoop.GetSubstrateSnapshot();
            log(
                "substrate: recommended_budget=" +
                substrate.RecommendedKernelBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                ", ram_mb=" +
                substrate.AvailableRamMb +
                "/" +
                substrate.TotalRamMb +
                ", used_est=" +
                substrate.UsedRamEstimate +
                ", rtc=" +
                substrate.RtcHour.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0') +
                ":" +
                substrate.RtcMinute.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0') +
                ":" +
                substrate.RtcSecond.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0'));

            log(string.Empty);
            log("[METABOLISM: BASELINE]");
            KernelLoop.StatusSnapshot status1 = kernelLoop.GetStatus();
            PrintStatus(log, "kernel", status1);

            log(string.Empty);
            log("[INGEST: SMOKE TEST]");
            IngestResult smoke = kernelLoop.ProcessIngestPipeline(new DataStream
            {
                DatasetType = "semantic_logic",
                DatasetId = "kernel_diag_smoke",
                Payload = "SYS_BOOT_01 SYS_BOOT_02"
            });
            log(
                "smoke: outcome=" +
                smoke.Outcome +
                ", success=" +
                smoke.Success +
                ", reflex=" +
                smoke.ReflexHit +
                ", deep=" +
                smoke.DeepThinkPath +
                ", zombie=" +
                smoke.ZombieTriggered +
                ", energy=" +
                smoke.EnergyRemaining.ToString("0.00", CultureInfo.InvariantCulture));

            log(string.Empty);
            log("[SCHEDULER]");
            bool autoSleepTriggered = kernelLoop.PollSleepScheduler(false, out SleepCycleScheduler.SleepTriggerReason autoReason);
            log(
                "scheduler_tick: auto_sleep=" +
                autoSleepTriggered +
                ", reason=" +
                SleepCycleScheduler.FormatReason(autoReason));

            if (hardwareSynapse != null)
            {
                log(string.Empty);
                log("[HARDWARE SYNAPSE]");
                HardwareSynapse.SeedResult seeded = hardwareSynapse.TrainStandardKeyboardProfile();
                log(
                    "synapse_seed_keyboard: requested=" +
                    seeded.Requested +
                    ", trained=" +
                    seeded.Trained +
                    ", failed=" +
                    seeded.Failed);

                byte[] knownPulse = new byte[] { 0x04 };
                HardwareSynapse.PulseResult pre = hardwareSynapse.ProcessSignal(knownPulse);
                log(
                    "synapse_known_pre: recognized=" +
                    pre.Recognized +
                    ", outcome=" +
                    pre.Outcome +
                    ", intent=" +
                    (string.IsNullOrWhiteSpace(pre.Intent) ? "none" : pre.Intent) +
                    ", similarity=" +
                    pre.Similarity.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", compared=" +
                    pre.ComparedReflexes);

                byte[] unknownPulse = new byte[] { 0xFF, 0xA1, 0x00 };
                HardwareSynapse.PulseResult unknownBefore = hardwareSynapse.ProcessSignal(unknownPulse);
                log(
                    "synapse_unknown_pre: recognized=" +
                    unknownBefore.Recognized +
                    ", outcome=" +
                    unknownBefore.Outcome +
                    ", intent=" +
                    (string.IsNullOrWhiteSpace(unknownBefore.Intent) ? "none" : unknownBefore.Intent) +
                    ", similarity=" +
                    unknownBefore.Similarity.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", compared=" +
                    unknownBefore.ComparedReflexes);

                HardwareSynapse.TrainResult trained = hardwareSynapse.TrainSignal(unknownPulse, "AUDIO_MUTE");
                log(
                    "synapse_unknown_train: success=" +
                    trained.Success +
                    ", intent=" +
                    trained.Intent +
                    ", reflex_id=" +
                    trained.ReflexId +
                    ", outcome=" +
                    trained.Outcome);

                HardwareSynapse.PulseResult post = hardwareSynapse.ProcessSignal(unknownPulse);
                log(
                    "synapse_unknown_post: recognized=" +
                    post.Recognized +
                    ", outcome=" +
                    post.Outcome +
                    ", intent=" +
                    (string.IsNullOrWhiteSpace(post.Intent) ? "none" : post.Intent) +
                    ", similarity=" +
                    post.Similarity.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", compared=" +
                    post.ComparedReflexes);
            }

            log(string.Empty);
            log("[SLEEP: MANUAL]");
            KernelLoop.StatusSnapshot beforeSleep = kernelLoop.GetStatus();
            kernelLoop.TriggerSleepCycle(SleepCycleScheduler.SleepTriggerReason.Manual);
            KernelLoop.StatusSnapshot afterSleep = kernelLoop.GetStatus();
            log(
                "sleep: energy_before=" +
                beforeSleep.EnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                ", energy_after=" +
                afterSleep.EnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                ", sleeps=" +
                beforeSleep.SleepCycles +
                "->" +
                afterSleep.SleepCycles);

            log(string.Empty);
            log("[BATCH: PROGRAM MANIFOLD]");
            
            Ruleset testRuleset = new Ruleset();
            testRuleset.ConstraintMode = "semantic_logic";
            int dim = 1024;
            Tensor aVec = new Tensor(new Shape(dim)); aVec.Data[0] = 1.0f;
            Tensor bVec = new Tensor(new Shape(dim)); bVec.Data[1] = 1.0f;
            testRuleset.SymbolDefinitions["TEST_A"] = TensorOps.NormalizeL2(aVec);
            testRuleset.SymbolDefinitions["TEST_B"] = TensorOps.NormalizeL2(bVec);
            
            ProgramManifold testManifold = new ProgramManifold(kernelLoop, testRuleset, new ProgramManifold.Configuration
            {
                Name = "diag_manifold",
                CacheCapacity = 16,
                AllocationPercentage = 0.05f,
                MinimumAllocation = 10.0f
            });
            
            EnqueueDiagnosticInputs(testManifold);
            BatchController.BatchRunResult run = testManifold.RunBatch(1);
            PrintBatchSummary(log, "manifold_batch", run);

            log(string.Empty);
            log("[METABOLISM: FINAL]");
            KernelLoop.StatusSnapshot statusFinal = kernelLoop.GetStatus();
            PrintStatus(log, "kernel", statusFinal);

            bool checkPhysicalBudget = statusFinal.RecommendedPhysicalBudget > 0.0f;
            bool checkEnergyValid = statusFinal.MaxEnergyBudget > 0.0f && statusFinal.EnergyBudget >= 0.0f;
            bool checkSleepAdvanced = afterSleep.SleepCycles >= beforeSleep.SleepCycles + 1;
            bool checkSmokeRan = !string.IsNullOrWhiteSpace(smoke.Outcome);

            log(string.Empty);
            log("[CHECKS]");
            log("check_physical_budget=" + checkPhysicalBudget);
            log("check_energy_valid=" + checkEnergyValid);
            log("check_sleep_advanced=" + checkSleepAdvanced);
            log("check_smoke_outcome=" + checkSmokeRan);

            bool allChecksPass = checkPhysicalBudget && checkEnergyValid && checkSleepAdvanced && checkSmokeRan;
            log("kernel_diagnostics_pass=" + allChecksPass);
            log("=== KERNEL DIAGNOSTICS COMPLETE ===");

            onComplete?.Invoke();
        }

        private static void EnqueueDiagnosticInputs(ProgramManifold manifold)
        {
            manifold.Enqueue(new DataStream
            {
                DatasetType = "semantic_logic",
                DatasetId = "diag_001",
                Payload = "TEST_A TEST_B TEST_A",
                DimHint = 128
            });
            manifold.Enqueue(new DataStream
            {
                DatasetType = "semantic_logic",
                DatasetId = "diag_002",
                Payload = "TEST_B TEST_B TEST_A",
                DimHint = 128
            });
        }

        private static void PrintBatchSummary(Action<string> log, string title, BatchController.BatchRunResult run)
        {
            log("--- " + title + " ---");
            log(
                "Processed: " +
                run.Processed +
                " | Deep Think: " +
                run.DeepThinkHits +
                " | Reflex: " +
                run.ReflexHits +
                " | Zombie: " +
                run.ZombieEvents);
        }

        private static void PrintStatus(Action<string> log, string prefix, KernelLoop.StatusSnapshot status)
        {
            log(
                prefix +
                "_status: energy=" +
                status.EnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                "/" +
                status.MaxEnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                ", energy_pct=" +
                (status.EnergyPercent * 100.0f).ToString("0.0", CultureInfo.InvariantCulture) +
                ", fatigue=" +
                status.FatigueThreshold.ToString("0.00", CultureInfo.InvariantCulture) +
                ", fatigue_ratio=" +
                status.FatigueRemainingRatio.ToString("0.00", CultureInfo.InvariantCulture) +
                ", zombie_at=" +
                status.ZombieActivationThreshold.ToString("0.00", CultureInfo.InvariantCulture) +
                ", zombie_ratio=" +
                status.ZombieActivationRatio.ToString("0.00", CultureInfo.InvariantCulture) +
                ", zombie_critic=" +
                status.ZombieCriticThreshold.ToString("0.00", CultureInfo.InvariantCulture) +
                ", zombie=" +
                status.ZombieModeActive);

            log(
                prefix +
                "_runtime: wm=" +
                status.WorkingMemoryCount +
                "/" +
                status.WorkingMemoryCapacity +
                ", entropy=" +
                status.CognitiveEntropyBuffer.ToString("0.000", CultureInfo.InvariantCulture) +
                ", sleeps=" +
                status.SleepCycles +
                ", last_sleep=" +
                status.LastSleepTrigger +
                ", processed=" +
                status.ProcessedInputs +
                ", physical_cap=" +
                status.RecommendedPhysicalBudget.ToString("0.00", CultureInfo.InvariantCulture));
        }
    }
}

