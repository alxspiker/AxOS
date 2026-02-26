# AxOS Architecture Deep Dive

This document explains how AxOS is structured and how the HDC-based cognitive runtime is wired into the kernel-level flow.

For empirical proof (real boot logs), see: **[DEMOS.md](DEMOS.md)**.

---

## Table of contents

- [Architecture at a glance](#architecture-at-a-glance)
- [HDC in AxOS (concepts)](#hdc-in-axos-concepts)
- [End-to-end ingest pipeline](#end-to-end-ingest-pipeline)
- [The seven subsystems](#the-seven-subsystems)
- [Program manifolds and rulesets](#program-manifolds-and-rulesets)
- [Persistence](#persistence)
- [File reference](#file-reference)

---

## Architecture at a glance

```
┌──────────────────────────────────────────────────────────────┐
│                     EXOSKELETON (Shell)                      │
│            VGA Console / Serial (COM1) Interface             │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─────────────┐   ┌──────────────┐   ┌──────────────────┐  │
│  │  HARDWARE   │   │   KERNEL     │   │    STORAGE       │  │
│  │  SYNAPSE    │   │   LOOP       │   │    Holographic   │  │
│  │  Peripheral │   │   Metabolism │   │    File System   │  │
│  │  Nerve      │   │   Sleep      │   │                  │  │
│  │  Substrate  │   │   Manifolds  │   │                  │  │
│  │  Monitor    │   │   Batch Ctrl │   │                  │  │
│  └──────┬──────┘   └──────┬───────┘   └────────┬─────────┘  │
│         │                 │                     │            │
│         └────────┐        │         ┌───────────┘            │
│                  ▼        ▼         ▼                        │
│  ┌───────────────────────────────────────────────────────┐   │
│  │                    BRAIN (HDC System)                  │   │
│  │  ┌────────────┐ ┌──────────┐ ┌─────────────────────┐  │   │
│  │  │ Symbol     │ │ Reflex   │ │ Cognitive Adapter    │  │   │
│  │  │ Space      │ │ Store    │ │ (System 1 / 2 Gate)  │  │   │
│  │  ├────────────┤ ├──────────┤ ├─────────────────────┤  │   │
│  │  │ Episodic   │ │ Sequence │ │ Signal Phase         │  │   │
│  │  │ Memory     │ │ Encoder  │ │ Aligner              │  │   │
│  │  └────────────┘ └──────────┘ └─────────────────────┘  │   │
│  └───────────────────────┬───────────────────────────────┘   │
│                          ▼                                   │
│  ┌───────────────────────────────────────────────────────┐   │
│  │                 CORE (Tensor Math)                     │   │
│  │     Tensor  ·  Shape  ·  TensorOps  ·  HdcAlgorithms  │   │
│  └───────────────────────────────────────────────────────┘   │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│              COSMOS IL2CPU → Bare Metal x86 Hardware         │
└──────────────────────────────────────────────────────────────┘

```

---

## HDC in AxOS (concepts)

AxOS uses biological metaphors (metabolism, sleep, reflexes, DNA) as names for concrete mechanisms:
energy budgets, consolidation passes, ruleset mutation/promotion, and cached triggers. They are metaphors for code paths, not claims of literal biology.

### Hypervectors / tensors
A “symbol” (token, opcode, pulse, intent) is represented as a high-dimensional vector (e.g., 1024D). Two random high-dimensional vectors tend to be near-orthogonal, enabling a large symbol vocabulary with low interference.

### Core operations
- **Bind**: element-wise multiplication (associating two concepts)
- **Bundle**: superposition (adding concepts into one representation)
- **Permute**: circular shift (position/context encoding)
- **Cosine similarity**: primary metric for “recognition”
- **L2 normalize**: keeps magnitudes stable across operations

### Similarity gates (why they matter)
AxOS uses similarity thresholds in different modes:
- **Tolerant recognition**: accept near matches
- **Crystalline/strict recognition**: require extremely high similarity (optionally 1.0) to treat as valid

This enables “reject unknown” behavior without hard-coded switch tables.

---

## End-to-end ingest pipeline

This is the core runtime flow when something is intentionally fed through the cognitive pipeline (e.g., `kernel ingest` or internal demos):

```

Input → Heuristic Analysis → Encode → Remember
↓
WorkingMemoryCache similarity check
├── HIT  → System 1 reflex (cheap)
└── MISS → System 2 search (expensive)
↓
Critic gate
├── ACCEPT → cache/promote candidate
└── REJECT → fatigue / zombie / anomaly flag
↓
Sleep cycle (when triggered)
→ consolidate / promote / mutate ruleset

```

Key elements:
- **SignalProfile**: stats of the input (entropy/sparsity/etc.)
- **Adaptive thresholds**: similarity and critic thresholds adjust based on the profile
- **Metabolism**: budgets System 2 search; forces backoff/sleep
- **Sleep scheduler**: triggers consolidation + recharge

---

## The seven subsystems

### 1) Core (tensor math)
Provides the low-level primitives for all geometric operations.
- `Shape.cs`
- `Tensor.cs`
- `TensorOps.cs`
- `HdcAlgorithms.cs`

### 2) Brain (HDC system)
Implements the cognitive runtime: symbol generation, memory, reflexes, sequence encoding, gating.
- `HdcSystem.cs`
- `SymbolSpace.cs`
- `SequenceEncoder.cs`
- `WorkingMemoryCache.cs`
- `EpisodicMemory.cs`
- `ReflexStore.cs`
- `HeuristicConfig.cs`
- `CognitiveAdapter.cs`
- `SignalPhaseAligner.cs`

### 3) Kernel (orchestration + energy + sleep)
Owns the ingest pipeline contracts, energy model, and sleep scheduling.
- `KernelLoop.cs`
- `KernelLoopContracts.cs`
- `SystemMetabolism.cs`
- `SleepCycleScheduler.cs`
- `BatchController.cs`

### 4) Hardware (I/O surface + substrate awareness)
- `PeripheralNerve.cs` (COM1 driver)
- `SubstrateMonitor.cs` (RAM/RTC/budget estimation)
- `HardwareSynapse.cs` (developer-facing mapping of pulses → intents)

### 5) Storage (holographic file system)
Intent-based write/read by similarity.
- `HolographicFileSystem.cs`

### 6) Shell (interactive interface)
User commands, VGA/serial, dispatch to subsystems.
- `Exoskeleton.cs`

### 7) Diagnostics (self-tests + demos)
- `KernelTest.cs`
- `AppDemo.cs`

---

## Program manifolds and rulesets

### ProgramManifold
A **ProgramManifold** is an isolated runtime environment with its own:
- HDC system state (vocabulary, reflexes, memory)
- energy budget allocated from the host kernel
- batch controller
- ruleset (“DNA”) defining symbol/reflex scaffolding

The intent is: a program can “spend” its own budget, sleep, and consolidate without draining the host kernel beyond its allocation.

### Ruleset
A **Ruleset** defines:
- a constraint/execution mode label
- heuristic tuning values
- symbol definitions
- reflex triggers

During sleep, the manifold can promote patterns / anomalies into new reflex triggers (constrained to the demo discovery model).

---

## Persistence

AxOS supports saving/loading learned state (symbols + reflexes) via mapper routines in the HDC system (e.g., a custom binary format like `BCMAPBIN`).

Persistence matters because it turns “learning during a session” into “retained behavior across reboots.”

---

## File reference

| Subsystem | File | Purpose |
|---|---|---|
| Core | `Shape.cs` | Tensor dimension descriptor |
|  | `Tensor.cs` | N-dimensional data container |
|  | `TensorOps.cs` | Bind/Bundle/Permute/Cosine/Normalize |
|  | `HdcAlgorithms.cs` | Search/relaxation/annealing helpers |
| Brain | `HdcSystem.cs` | Central container + persistence |
|  | `CognitiveAdapter.cs` | System 1/2 gating + consolidation |
|  | `WorkingMemoryCache.cs` | short-term cache + hit logic |
|  | `EpisodicMemory.cs` | long-term memory trace |
|  | `HeuristicConfig.cs` | tuning parameters |
|  | `ReflexStore.cs` | reflex storage/query |
|  | `SymbolSpace.cs` | token → vector mapping |
|  | `SequenceEncoder.cs` | ordered encoding / k-mers |
|  | `SignalPhaseAligner.cs` | continuous stream denoising |
| Kernel | `KernelLoop.cs` | ingest pipeline orchestration |
|  | `KernelLoopContracts.cs` | DataStream/SignalProfile/etc. |
|  | `SystemMetabolism.cs` | energy model |
|  | `SleepCycleScheduler.cs` | sleep triggers + consolidation hooks |
|  | `BatchController.cs` | FIFO ingestion |
|  | `Ruleset.cs` | program DNA |
|  | `RulesetParser.cs` | `.ax` ruleset parsing |
|  | `ProgramManifold.cs` | isolated execution env |
| Hardware | `HardwareSynapse.cs` | pulse → intent encoding API |
|  | `PeripheralNerve.cs` | COM1 serial I/O |
|  | `SubstrateMonitor.cs` | RAM/RTC/budget |
| Storage | `HolographicFileSystem.cs` | intent-based storage |
| Shell | `Exoskeleton.cs` | command UI |
| Diagnostics | `KernelTest.cs` | system validation |
|  | `AppDemo.cs` | proof-of-concept demos |
