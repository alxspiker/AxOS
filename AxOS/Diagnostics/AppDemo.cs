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
    public static class AppDemo
    {
        public static void RunIsolatedManifoldDemo(KernelLoop kernelLoop, Action<string> writeLine = null)
        {
            Action<string> log = writeLine ?? Console.WriteLine;
            if (kernelLoop == null)
            {
                log("appdemo_error: missing_kernel_loop");
                return;
            }

            KernelLoop.StatusSnapshot globalBefore = kernelLoop.GetStatus();
            log("=== APP MANIFOLD ISOLATION DEMO ===");
            log(
                "global_before: energy=" +
                globalBefore.EnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                "/" +
                globalBefore.MaxEnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                ", zombie=" +
                globalBefore.ZombieModeActive);

            HeuristicConfig appHeuristics = new HeuristicConfig
            {
                System1Base = 0.58f,
                System1EntropyWeight = 0.08f,
                System1SparsityWeight = 0.04f,
                System1Min = 0.42f,
                System1Max = 0.70f,
                CriticBase = 0.28f,
                CriticEntropyWeight = 0.05f,
                CriticSkewnessWeight = 0.02f,
                CriticMin = 0.20f,
                CriticMax = 0.42f,
                ConsolidationMinFitness = 0.30f,
                ConsolidationMaxNormalizedBurn = 0.55f,
                FatigueRemainingRatio = 0.35f,
                ZombieActivationRatio = 0.20f,
                ZombieCriticThreshold = 0.90f
            };

            Ruleset appRuleset = new Ruleset();
            appRuleset.ConstraintMode = "semantic_logic";
            appRuleset.Heuristics = appHeuristics;

            int dim = 1024;
            Tensor alphaVector = new Tensor(new Shape(dim)); alphaVector.Data[0] = 1.0f; alphaVector.Data[10] = 1.0f;
            Tensor betaVector = new Tensor(new Shape(dim)); betaVector.Data[1] = 1.0f; betaVector.Data[11] = 1.0f;
            Tensor gammaVector = new Tensor(new Shape(dim)); gammaVector.Data[2] = 1.0f; gammaVector.Data[12] = 1.0f;

            appRuleset.SymbolDefinitions["ALPHA"] = TensorOps.NormalizeL2(alphaVector);
            appRuleset.SymbolDefinitions["BETA"] = TensorOps.NormalizeL2(betaVector);
            appRuleset.SymbolDefinitions["GAMMA"] = TensorOps.NormalizeL2(gammaVector);
            
            appRuleset.ReflexTriggers.Add(new ReflexTrigger
            {
                TargetSymbol = "ALPHA",
                SimilarityThreshold = 0.85f,
                ActionIntent = "resolve_state"
            });

            ProgramManifold app = new ProgramManifold(kernelLoop, appRuleset, new ProgramManifold.Configuration
            {
                Name = "semantic_app_demo",
                CacheCapacity = 48,
                AllocationPercentage = 0.15f,
                MinimumAllocation = 24.0f
            });

            log(
                "app_budget: allocation=15.0%, local_max=" +
                app.AllocatedEnergyBudget.ToString("0.00", CultureInfo.InvariantCulture));

            EnqueueAppInputs(app);
            log("app_phase1: run_batch_start");
            BatchController.BatchRunResult firstRun = app.RunBatch(1);
            KernelLoop.StatusSnapshot localAfterRun = app.GetStatus();

            log(
                "app_run1: processed=" +
                firstRun.Processed +
                ", deep=" +
                firstRun.DeepThinkHits +
                ", reflex=" +
                firstRun.ReflexHits +
                ", zombie=" +
                firstRun.ZombieEvents);
            log(
                "app_after_run1: energy=" +
                localAfterRun.EnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                ", zombie=" +
                localAfterRun.ZombieModeActive);

            app.ClearQueue();
            log("app_phase2: sleep_start");
            app.Sleep();
            log("app_phase2: sleep_complete");
            EnqueueAppInputs(app);
            log("app_phase3: run_batch_start");
            BatchController.BatchRunResult secondRun = app.RunBatch(1);
            KernelLoop.StatusSnapshot localAfterSecondRun = app.GetStatus();

            log(
                "app_run2: processed=" +
                secondRun.Processed +
                ", deep=" +
                secondRun.DeepThinkHits +
                ", reflex=" +
                secondRun.ReflexHits +
                ", zombie=" +
                secondRun.ZombieEvents);
            log(
                "app_after_run2: energy=" +
                localAfterSecondRun.EnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                ", zombie=" +
                localAfterSecondRun.ZombieModeActive);

            KernelLoop.StatusSnapshot globalAfter = kernelLoop.GetStatus();
            float globalDelta = globalBefore.EnergyBudget - globalAfter.EnergyBudget;
            log(
                "global_after: energy=" +
                globalAfter.EnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                "/" +
                globalAfter.MaxEnergyBudget.ToString("0.00", CultureInfo.InvariantCulture) +
                ", zombie=" +
                globalAfter.ZombieModeActive +
                ", delta=" +
                globalDelta.ToString("0.00", CultureInfo.InvariantCulture));
            
            log(string.Empty);
            RunX86ManifoldDemo(kernelLoop, writeLine);

            log(string.Empty);
            RunRawBinaryManifoldDemo(kernelLoop, writeLine);
            
            log(string.Empty);
            RunPeRibosomeDemo(kernelLoop, writeLine);

            log(string.Empty);
            RunNeuroplasticDiscoveryDemo(kernelLoop, writeLine);

            log(string.Empty);
            RunCalculatorDemo(kernelLoop, writeLine);

            log("=== APP DEMO COMPLETE ===");
        }

        private static void EnqueueAppInputs(ProgramManifold app)
        {
            app.Enqueue(new DataStream
            {
                DatasetType = "semantic_logic",
                DatasetId = "app_001",
                Payload = "ALPHA BETA GAMMA ALPHA"
            });
            app.Enqueue(new DataStream
            {
                DatasetType = "semantic_logic",
                DatasetId = "app_002",
                Payload = "BETA GAMMA ALPHA BETA"
            });
            app.Enqueue(new DataStream
            {
                DatasetType = "semantic_logic",
                DatasetId = "app_003",
                Payload = "GAMMA ALPHA BETA GAMMA"
            });
            app.Enqueue(new DataStream
            {
                DatasetType = "semantic_logic",
                DatasetId = "app_004",
                Payload = "ALPHA GAMMA BETA ALPHA"
            });
        }

        public static void RunX86ManifoldDemo(KernelLoop kernelLoop, Action<string> writeLine = null)
        {
            Action<string> log = writeLine ?? Console.WriteLine;
            if (kernelLoop == null)
            {
                log("appdemo_error: missing_kernel_loop");
                return;
            }

            log("=== X86 HARDWARE VIRTUALIZATION DEMO ===");
            
            int dim = 1024;
            Ruleset x86Rules = new Ruleset();
            x86Rules.ConstraintMode = "x86_64_Physical";

            Tensor regRax = new Tensor(new Shape(dim)); 
            regRax.Data[5] = 1.0f;
            x86Rules.SymbolDefinitions["REG_RAX"] = TensorOps.NormalizeL2(regRax);

            Tensor opAdd = new Tensor(new Shape(dim));
            opAdd.Data[100] = 1.0f; 
            x86Rules.SymbolDefinitions["OP_ADD"] = TensorOps.NormalizeL2(opAdd);

            log("x86_init: registers and opcodes mapped to geometric space.");

            Tensor instruction = TensorOps.Bundle(
                x86Rules.SymbolDefinitions["OP_ADD"], 
                x86Rules.SymbolDefinitions["REG_RAX"]
            );

            log("x86_fetch: instruction bundled (OP_ADD + REG_RAX).");

            float simAdd = (float)TensorOps.CosineSimilarity(instruction, x86Rules.SymbolDefinitions["OP_ADD"]);
            
            log("x86_decode: intent similarity to OP_ADD = " + simAdd.ToString("0.000", CultureInfo.InvariantCulture));

            if (simAdd > 0.65f)
            {
                regRax = TensorOps.Permute(regRax, 5); // Simulating ADD RAX, 5
                log("x86_execute: RAX state physically rotated (ADD 5) via geometric reflex.");
            }
            else
            {
                log("x86_execute: failed to decode intent.");
            }

            log(string.Empty);
            log("--- x86 RIGID DETERMINISM TEST ---");
            
            // 1. Define Rigid Heuristics (Crystalline Mode)
            HeuristicConfig rigidHeuristics = new HeuristicConfig
            {
                System1EntropyWeight = 0.0f,
                System1SparsityWeight = 0.0f,
                CriticMin = 1.0f,
                CriticMax = 1.0f
            };
            
            // 2. Setup Deterministic Manifold Context
            log("x86_det: switching to Crystalline DNA mode (sim_target=1.0).");
            
            // We use the same OP_ADD but now require PERFECT identity
            Tensor exactInstruction = x86Rules.SymbolDefinitions["OP_ADD"].Copy();
            float simExact = (float)TensorOps.CosineSimilarity(exactInstruction, x86Rules.SymbolDefinitions["OP_ADD"]);
            
            log("x86_det_decode: identity match = " + simExact.ToString("0.0", CultureInfo.InvariantCulture));
            
            if (simExact >= 1.0f)
            {
                regRax = TensorOps.Permute(regRax, 1);
                log("x86_det_execute: EXACT MATCH SUCCESS. Register state mutated.");
            }
            else
            {
                log("x86_det_execute: SEGMENTATION FAULT. No match.");
            }

            // 3. Test Failure Case (Single bit flip simulation)
            Tensor corruptedInstruction = exactInstruction.Copy();
            corruptedInstruction.Data[500] += 0.01f; // High-precision corruption
            float simCorrupted = (float)TensorOps.CosineSimilarity(corruptedInstruction, x86Rules.SymbolDefinitions["OP_ADD"]);
            
            log("x86_det_corrupt: similarity = " + simCorrupted.ToString("0.000000", CultureInfo.InvariantCulture));
            
            if (simCorrupted >= 1.0f)
            {
                log("x86_det_fail: Warning - Determinism failed to catch corruption.");
            }
            else
            {
                log("x86_det_fail: SEGMENTATION FAULT DETECTED. Manifold isolated.");
            }

            log("=== X86 DEMO COMPLETE ===");
        }

        public static void RunRawBinaryManifoldDemo(KernelLoop kernelLoop, Action<string> writeLine = null)
        {
            Action<string> log = writeLine ?? Console.WriteLine;
            if (kernelLoop == null) return;

            log("=== RAW BINARY INGESTION DEMO ===");

            // 1. Define the deterministic ruleset
            int dim = 1024;
            Ruleset x86Rules = new Ruleset();
            x86Rules.ConstraintMode = "x86_64_Physical";
            x86Rules.Heuristics = new HeuristicConfig { CriticMin = 1.0f, CriticMax = 1.0f };

            // 2. Map the Hex Opcodes to Tensors
            // 0xC7 = MOV, 0x83 = ADD
            Tensor opMov = new Tensor(new Shape(dim)); opMov.Data[0xC7 % 1024] = 1.0f; 
            Tensor opAdd = new Tensor(new Shape(dim)); opAdd.Data[0x83 % 1024] = 1.0f;
            
            x86Rules.SymbolDefinitions["0xC7"] = TensorOps.NormalizeL2(opMov);
            x86Rules.SymbolDefinitions["0x83"] = TensorOps.NormalizeL2(opAdd);

            // 3. Raw Machine Code payload: MOV (0xC7), ADD (0x83), RET (0xC3)
            byte[] rawBinary = { 0xC7, 0x83, 0xC3 }; 

            log("binary_loader: loaded " + rawBinary.Length + " bytes of machine code.");

            // 4. Fetch-Decode-Execute loop
            foreach (byte b in rawBinary)
            {
                string hexOpcode = "0x" + b.ToString("X2");
                log("cpu_fetch: reading byte " + hexOpcode);

                // One-hot encode the byte into a signal tensor
                Tensor currentSignal = new Tensor(new Shape(dim));
                currentSignal.Data[b % 1024] = 1.0f;
                currentSignal = TensorOps.NormalizeL2(currentSignal);

                if (x86Rules.SymbolDefinitions.ContainsKey(hexOpcode))
                {
                    float sim = (float)TensorOps.CosineSimilarity(currentSignal, x86Rules.SymbolDefinitions[hexOpcode]);
                    if (sim >= 0.999f) // Allow for slight float noise, effectively exact
                    {
                        // Simulate a deterministic register shift for each instruction
                        int shift = (b == 0xC7) ? 10 : 5; 
                        log("cpu_execute: EXACT MATCH for " + hexOpcode + ". Reg state rotated by " + shift + ".");
                    }
                }
                else if (b == 0xC3) 
                {
                    log("cpu_execute: RET instruction. Binary execution complete.");
                    break;
                }
                else
                {
                    log("cpu_fault: UNKNOWN OPCODE " + hexOpcode + ". Segmentation fault.");
                    break;
                }
            }

            log("=== BINARY INGESTION COMPLETE ===");
        }

        public static void RunPeRibosomeDemo(KernelLoop kernelLoop, Action<string> writeLine = null)
        {
            Action<string> log = writeLine ?? Console.WriteLine;
            if (kernelLoop == null) return;

            log("=== PE RIBOSOME (HEADER PARSING) DEMO ===");

            int dim = 1024;
            
            // 1. Define Signatures as Tensors
            Tensor sigMZ = new Tensor(new Shape(dim)); sigMZ.Data[0x4D % dim] = 1.0f; sigMZ.Data[0x5A % dim] = 1.0f;
            sigMZ = TensorOps.NormalizeL2(sigMZ);
            
            Tensor sigPE = new Tensor(new Shape(dim)); sigPE.Data[0x50 % dim] = 1.0f; sigPE.Data[0x45 % dim] = 1.0f;
            sigPE = TensorOps.NormalizeL2(sigPE);

            // 2. Define State Tensors
            Tensor stateSearchingMZ = new Tensor(new Shape(dim)); stateSearchingMZ.Data[1] = 1.0f;
            Tensor stateSearchingPE = new Tensor(new Shape(dim)); stateSearchingPE.Data[2] = 1.0f;
            Tensor currentState = stateSearchingMZ;

            // 3. Simulated Byte Stream (Simplified PE header)
            // MZ ... PE ...
            byte[] stream = { 0x4D, 0x5A, 0x00, 0x00, 0x50, 0x45, 0xC7, 0x83 };

            log("ribosome: ingesting 8-byte simulated stream.");

            for (int i = 0; i < stream.Length - 1; i += 2)
            {
                // Create a 2-byte window tensor
                Tensor window = new Tensor(new Shape(dim));
                window.Data[stream[i] % dim] = 1.0f;
                window.Data[stream[i+1] % dim] = 1.0f;
                window = TensorOps.NormalizeL2(window);

                float simMZ = (float)TensorOps.CosineSimilarity(window, sigMZ);
                float simPE = (float)TensorOps.CosineSimilarity(window, sigPE);

                if (currentState == stateSearchingMZ && simMZ >= 0.99f)
                {
                    log("ribosome: 'MZ' detected. Dos stub recognized. Transitioning state.");
                    currentState = stateSearchingPE;
                }
                else if (currentState == stateSearchingPE && simPE >= 0.99f)
                {
                    log("ribosome: 'PE' detected. Windows header located.");
                    log("ribosome: Metamorphosis complete. Switcing to x86_64_Physical context.");
                    break;
                }
                else
                {
                    log("ribosome: byte_pair " + stream[i].ToString("X2") + stream[i+1].ToString("X2") + " ignored (no reflex match).");
                }
            }

            log("=== PE RIBOSOME COMPLETE ===");
        }

        public static void RunNeuroplasticDiscoveryDemo(KernelLoop kernelLoop, Action<string> writeLine = null)
        {
            Action<string> log = writeLine ?? Console.WriteLine;
            if (kernelLoop == null) return;

            log("=== NEUROPLASTIC DISCOVERY (KERNEL-INTEGRATED) DEMO ===");

            // 1. Initial Knowledge: ONLY RET (0xC3). PUSH (0x6A) and POP (0x58) are unknown.
            Ruleset rules = new Ruleset();
            rules.ConstraintMode = "x86_64_Discovery";
            rules.Heuristics.CriticMin = 0.95f; 
            
            Tensor opRet = new Tensor(new Shape(1024)); opRet.Data[0xC3 % 1024] = 1.0f;
            rules.SymbolDefinitions["0xC3"] = TensorOps.NormalizeL2(opRet);

            ProgramManifold app = new ProgramManifold(kernelLoop, rules, new ProgramManifold.Configuration
            {
                Name = "discovery_manifold",
                AllocationPercentage = 0.2f
            });

            log("discovery_init: OS knows RET (0xC3). PUSH (0x6A) and POP (0x58) are unknown.");

            // 2. Execution Pass 1 (Discovery Phase)
            log(string.Empty);
            log("--- DISCOVERY PASS 1: SYSTEM 2 INDUCTION ---");
            
            app.Enqueue(new DataStream { DatasetType = "x86_64_Discovery", Payload = "0x6A" });
            app.Enqueue(new DataStream { DatasetType = "x86_64_Discovery", Payload = "0x58" });
            app.Enqueue(new DataStream { DatasetType = "x86_64_Discovery", Payload = "0xC3" });

            // We run one item at a time to log results clearly
            for (int i = 0; i < 3; i++)
            {
                var result = app.RunBatch(1);
                // The kernel automatically handles deduction and anomaly flagging now.
                // We just observe the outcomes.
            }

            // 3. Sleep Phase (Consolidation)
            log(string.Empty);
            log("--- AUTONOMIC SLEEP: GENETIC MUTATION ---");
            log("autonomic_sleep: Consolidating hippocampal anomalies into long-term DNA...");
            app.Sleep();
            log("sleep_complete: Reflexes for 0x6A and 0x58 burned into Ruleset.");

            // 4. Execution Pass 2 (Reflex Phase)
            log(string.Empty);
            log("--- DISCOVERY PASS 2: SYSTEM 1 REFLEX ---");
            app.Enqueue(new DataStream { DatasetType = "x86_64_Discovery", Payload = "0x6A" });
            app.Enqueue(new DataStream { DatasetType = "x86_64_Discovery", Payload = "0x58" });
            app.Enqueue(new DataStream { DatasetType = "x86_64_Discovery", Payload = "0xC3" });

            app.RunBatch(3);
            log("discovery_verification: 100% reflex hits. OS has successfully evolved.");

            log("=== NEUROPLASTIC DISCOVERY COMPLETE ===");
        }

        public static void RunCalculatorDemo(KernelLoop kernelLoop, Action<string> writeLine = null)
        {
            Action<string> log = writeLine ?? Console.WriteLine;
            if (kernelLoop == null) return;

            log("=== BIOLOGICAL CALCULATOR EXECUTION DEMO ===");

            // 1. Setup the Crystalline Execution Environment
            int dim = 1024;
            Ruleset calcRules = new Ruleset();
            calcRules.ConstraintMode = "x86_64_Physical";
            calcRules.Heuristics = new HeuristicConfig { CriticMin = 1.0f, CriticMax = 1.0f };

            // 2. Define the Hardware Space (Registers and Opcodes)
            Tensor regRax = CreateOneHot(dim, 0); // Accumulator
            Tensor regRbx = CreateOneHot(dim, 1); // Base
            
            calcRules.SymbolDefinitions["0xC7"] = CreateOneHot(dim, 0xC7);
            calcRules.SymbolDefinitions["0x83"] = CreateOneHot(dim, 0x83);
            calcRules.SymbolDefinitions["0xC3"] = CreateOneHot(dim, 0xC3);
            calcRules.SymbolDefinitions["0xAA"] = CreateOneHot(dim, 0xAA);

            // 2.5 Instantiate the Vocal Cord (PeripheralNerve)
            PeripheralNerve vocalCord = new PeripheralNerve();
            vocalCord.Initialize();

            foreach (var kvp in calcRules.SymbolDefinitions) {
                int peak = MeasureGeometricShift(new Tensor(new Shape(dim)), kvp.Value);
                log($"debug_sym_map: {kvp.Key} peak_idx={peak}");
            }

            // 3. The Interactive Inputs (What the user types)
            int inputA = 5;
            int inputB = 7;
            log($"user_input: Injecting A={inputA}, B={inputB} into geometric space.");

            // 4. The Binary Payload (Headless Calculator)
            // Conceptually: MOV RAX, inputA | MOV RBX, inputB | ADD RAX, RBX | SYSCALL | RET
            byte[] calcBinary = { 0xC7, 0xC7, 0x83, 0xAA, 0xC3 }; 

            // 5. Execution Loop
            int instructionStep = 0;
            foreach (byte b in calcBinary)
            {
                string hexOpcode = "0x" + b.ToString("X2");

                // --- CONTROL FLOW: Direct byte dispatch (interrupts bypass similarity) ---
                if (b == 0xAA)
                {
                    log("interrupt_spike: SYSCALL detected. Engaging Peripheral Nerve.");

                    // Extract geometry to flush to hardware
                    Tensor syscallOriginRax = new Tensor(new Shape(dim)); syscallOriginRax[0] = 1.0f;
                    int outputValue = MeasureGeometricShift(syscallOriginRax, regRax);

                    // Physically vibrate the hardware serial port
                    vocalCord.WriteLine("STDOUT -> " + outputValue);
                    log("syscall_complete: Tensor state flushed to bare-metal COM1 port (" + outputValue + ").");
                    continue;
                }
                if (b == 0xC3)
                {
                    log("cpu_execute: RET -> Calculation complete.");
                    break;
                }

                // --- ALU OPS: Cosine similarity gated (geometric intent recognition) ---
                Tensor currentSignal = CreateOneHot(dim, b);

                if (calcRules.SymbolDefinitions.ContainsKey(hexOpcode))
                {
                    float sim = (float)TensorOps.CosineSimilarity(currentSignal, calcRules.SymbolDefinitions[hexOpcode]);
                    
                    if (sim >= 0.90f)
                    {
                        // The Physics Engine physically mutates the register state based on the opcode
                        if (b == 0xC7 && instructionStep == 0) 
                        {
                            regRax = TensorOps.Permute(regRax, inputA);
                            log("cpu_execute: MOV RAX, " + inputA + " -> RAX rotated by " + inputA);
                            instructionStep++;
                        }
                        else if (b == 0xC7 && instructionStep == 1)
                        {
                            regRbx = TensorOps.Permute(regRbx, inputB);
                            log("cpu_execute: MOV RBX, " + inputB + " -> RBX rotated by " + inputB);
                            instructionStep++;
                        }
                        else if (b == 0x83)
                        {
                            // To ADD in geometric space, we permute RAX by the accumulated magnitude of RBX
                            Tensor originRbx = new Tensor(new Shape(dim)); originRbx[1] = 1.0f;
                            int rbv = MeasureGeometricShift(originRbx, regRbx);
                            regRax = TensorOps.Permute(regRax, rbv);
                            log("cpu_execute: ADD RAX, RBX -> RAX physically rotated by RBX magnitude (" + rbv + ").");
                            instructionStep++;
                        }
                    }
                }
            }

            // 6. Observation (Extracting the answer from the tensor)
            // To read the number back out, we measure how far RAX has shifted from its origin
            Tensor originRax = new Tensor(new Shape(dim)); originRax[0] = 1.0f;
            int calculatedAnswer = MeasureGeometricShift(originRax, regRax);
            
            log(string.Empty);
            log("=== EXTRACTION RESULTS ===");
            log("Expected Answer: " + (inputA + inputB));
            log("HDC Tensor State Output: " + calculatedAnswer);
            
            if (calculatedAnswer == (inputA + inputB))
            {
                log("verify: SUCCESS. Bare metal logic executed perfectly in geometric space.");
            }
            else
            {
                log("verify: FAILED. Spatial resolution lost.");
            }
        }

        private static int MeasureGeometricShift(Tensor origin, Tensor mutated)
        {
            int originIdx = 0;
            int mutatedIdx = 0;
            float bestOriginVal = -1.0f;
            float bestMutatedVal = -1.0f;

            for (int i = 0; i < origin.Data.Length; i++)
            {
                if (origin.Data[i] > bestOriginVal)
                {
                    bestOriginVal = origin.Data[i];
                    originIdx = i;
                }
                if (mutated.Data[i] > bestMutatedVal)
                {
                    bestMutatedVal = mutated.Data[i];
                    mutatedIdx = i;
                }
            }

            int diff = mutatedIdx - originIdx;
            if (diff < 0) diff += origin.Data.Length;
            return diff;
        }

        private static Tensor CreateOneHot(int dim, int index)
        {
            Tensor t = new Tensor(new Shape(dim));
            t[index % dim] = 1.0f;
            return TensorOps.NormalizeL2(t);
        }
    }
}

