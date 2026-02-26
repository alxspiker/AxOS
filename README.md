<p align="center">
  <h1 align="center">🧬 AxOS — A Biologically-Inspired Operating System</h1>
  <p align="center">
    <em>A bare-metal OS with a biologically-inspired cognitive runtime (Hyperdimensional Computing) layered on top of x86 hardware.</em>
  </p>
</p>

> AxOS is an experimental research operating system exploring Hyperdimensional Computing as a systems architecture primitive.

---

## License

Copyright (c) 2025–2026 alxspiker.

Licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)** — see [LICENSE](LICENSE) file.

This means:
- You are free to use, modify, and share AxOS **as long as** your modifications remain open source under the same license.
- If you run a modified version on a server or network (including cloud services, AI platforms, APIs, or internal tools), you **must** make the full modified source code available to users.
- Commercial use is allowed **only** if you comply with the license.

For commercial licensing (closed-source use, proprietary integrations, or removal of AGPL restrictions), contact via [GitHub Issues](https://github.com/alxspiker/AxOS/issues).

---

## Table of Contents

1. [License](#license)
2. [What Is AxOS?](#what-is-axos)
3. [Why Does AxOS Exist?](#why-does-axos-exist)
4. [Scope & Reality Check](#scope--reality-check)
5. [Terminology](#terminology)
6. [What the current build demonstrates](#what-the-current-build-demonstrates-qemu-boot-log)
7. [Architecture at a Glance](#architecture-at-a-glance)
8. [The Seven Subsystems](#the-seven-subsystems)
   - [Core - The Physics Engine](#1-core--the-physics-engine)
   - [Brain - The Cognitive Layer](#2-brain--the-cognitive-layer)
   - [Kernel - The Autonomic Nervous System](#3-kernel--the-autonomic-nervous-system)
   - [Hardware - The Sensory Organs](#4-hardware--the-sensory-organs)
   - [Storage - The Holographic File System](#5-storage--the-holographic-file-system)
   - [Shell - The Exoskeleton](#6-shell--the-exoskeleton)
   - [Diagnostics - The Immune System](#7-diagnostics--the-immune-system)
9. [How It Actually Works (End-to-End)](#how-it-actually-works-end-to-end)
10. [Key Concepts for Non-Programmers](#key-concepts-for-non-programmers)
11. [Building & Running](#building--running)
12. [Demo Suite (Annotated)](#demo-suite---annotated-proof-of-architecture)
13. [Project Status & Roadmap](#project-status)
14. [File Reference](#file-reference)

---

## What Is AxOS?

AxOS is a **bare-metal operating system** written in C# using the [Cosmos](https://github.com/CosmosOS/Cosmos) framework. Cosmos compiles C# directly into native x86 machine code via its IL2CPU compiler - there is no underlying Windows, Linux, or any other OS beneath it. When AxOS boots, **it is the only software running on the hardware**.

What makes AxOS fundamentally different from every other operating system is that it does **not** use traditional data structures like lookup tables, if-else chains, or opcode switch statements to understand the world. Instead, it uses **Hyperdimensional Computing (HDC)** - a mathematical framework inspired by how the human brain encodes, stores, and retrieves information using high-dimensional vectors (called *hypervectors*).

In plain English: **AxOS treats every piece of data - a keystroke, a byte of machine code, a file - as a point in a giant geometric space, and it "thinks" by rotating, blending, and comparing these points.**

---

## Why Does AxOS Exist?

Traditional operating systems are built on the **Von Neumann architecture**: fetch an instruction, decode it, execute it, repeat. This model is rigid - traditional operating systems rely on explicit dispatch logic. If an instruction or input is not recognized or properly handled, the system faults or aborts.

AxOS proposes a radically different approach:

| Traditional OS (Von Neumann dispatch) | AxOS |
|---|---|
| **Crashes** on unknown input | **Learns** from unknown input |
| Uses lookup tables for opcodes | Uses geometric similarity for recognition |
| Hardware drivers are written by humans | Hardware profiles are *trained* like reflexes |
| Files are stored by name/path | Files are stored and retrieved by *meaning* |
| Sleep mode saves power | Sleep mode **consolidates learned knowledge** |
| Programs run in isolated memory | Programs run in isolated *cognitive manifolds* |

AxOS models itself after biological systems:

- **System 1 (Reflexes)**: Fast, automatic responses to recognized patterns - like catching a ball without thinking.
- **System 2 (Deep Thinking)**: Slow, deliberate exploration when something is unfamiliar - like solving a math problem.
- **Sleep Cycles**: Periodic consolidation of short-term experience into long-term "muscle memory."
- **Metabolism**: A finite energy budget that depletes with computation, forcing the system to sleep and recharge.

---

## Scope & Reality Check

AxOS is a **real, bootable proof-of-concept** bare-metal OS built with Cosmos (C# compiled to native x86 via IL2CPU, no underlying OS).

- The system runs on conventional x86 hardware, so it is still Von Neumann at the silicon level. The "biological" layer is a cognitive architecture built *on top* of it.
- "Learning new opcodes" happens inside **isolated ProgramManifolds** using a **toy symbolic ISA** defined in Rulesets. The Neuroplastic Discovery demo shows real self-modification of that Ruleset during sleep - not arbitrary real x86 machine code.
- HardwareSynapse is a **developer framework** for HDC-based signal recognition and fault tolerance. Live keyboard/serial input uses Cosmos primitives; full HDC drivers for complex devices (USB, disk, etc.) are future work.
- Unknown input is handled gracefully: it triggers System 2 thinking, fatigue, or sandboxing inside a manifold. It does not crash the kernel.

Everything in the Demo Suite has been run on real QEMU boots.

- `Tensor` validates shape/data invariants in **DEBUG builds** to catch shape/length mismatches early (fails fast in development, zero cost in release).
- QEMU may exit with a non-zero code after the demo suite completes due to an intentional shutdown path; treat the boot log completion as the success criterion.

## Terminology

AxOS uses biological metaphors (metabolism, sleep, DNA, reflexes) as names for concrete mechanisms:
energy budgets, consolidation passes, ruleset mutation/promotion, and cached triggers.
They are metaphors for code paths — not claims of literal biology.

## What the current build demonstrates (QEMU boot log)

AxOS currently boots and runs an end-to-end demo suite that demonstrates:

- **Tensor correctness + invariants**: L2 normalization, cosine similarity, bundling behavior, and DEBUG-only invariant validation (`Tensor.ValidateInvariants()`).
- **Metabolism & sleep**: bounded energy budget, fatigue behavior, and sleep recharge inside the kernel loop.
- **Manifold isolation**: `ProgramManifold` runs with an allocated energy budget and does not drain the host kernel beyond that allocation.
- **Deterministic (crystalline) vs tolerant recognition**: exact-match gating can reject tiny corruption (similarity drops below 1.0).
- **Raw byte ingestion**: a toy binary stream is fetched/decode/handled through a ruleset mapping.
- **Neuroplastic “learning”**: unknown tokens/opcodes can be consolidated into reflex triggers across a sleep cycle (within the demo’s constrained discovery context).
- **Geometric calculator**: a minimal instruction sequence produces a correct result (`5 + 7 = 12`) and flushes it to COM1.
- **Noisy opcode classification**: learns 8 prototype “opcodes” from noisy 32-byte pulses, achieves **100% forced Top-1** (in this controlled demo configuration) on the held-out set, and **rejects an alien pulse** using similarity+margin gates.

> Note: “x86” demos here are **conceptual virtualization experiments** inside HDC space. They do not claim to decode arbitrary real x86 binaries into semantics.

### Real Demo Output (Headlines)

```text
--- TENSOR MATH SANITY SUITE ---
sanity_identity: sim(t1, t1) = 1.0000 | norm=1.0000 | peak=50
sanity_bundle:   sim(bundled, t1) = 0.7071 | sim(bundled, t2) = 0.7071

=== NOISY OPCODE LEARNING DEMO ===
post_sleep_accuracy: 96/96 (100%)
alien_pulse: CORRECTLY REJECTED as unknown opcode.

=== BIOLOGICAL CALCULATOR EXECUTION DEMO ===
STDOUT -> 12
verify: SUCCESS. Bare metal logic executed perfectly in geometric space.
```

---

## Architecture at a Glance

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

## The Seven Subsystems

### 1. Core - The Physics Engine

> **Biological Analogy**: The laws of physics that govern how neurons fire and signals travel.

The Core layer provides the raw mathematical primitives that power everything above it. There are no "operating system concepts" here - only geometry.

#### `Shape.cs` - Dimension Descriptor
Defines the shape (dimensions) of a tensor. A `Shape(1024)` means a 1-dimensional array of 1,024 floats. A `Shape(32, 32)` means a 2D grid. The `Total` property gives the total number of elements across all dimensions.

#### `Tensor.cs` - The Universal Data Container
A `Tensor` is AxOS's equivalent of a "variable." It is an N-dimensional array of floating-point numbers. Every piece of information in AxOS - a letter, a machine code byte, a keystroke, a file - is eventually represented as a Tensor.

Key features:
- **Explicit zero-initialization**: Every new Tensor starts as all zeros to prevent noise in the bare-metal environment (no garbage collector to clean up).
- **Flatten**: Collapses any multi-dimensional tensor into a 1D vector.
- **Copy**: Deep-copies the tensor to prevent accidental mutation.
- **Indexer**: `tensor[i]` accesses element `i` directly.

#### `TensorOps.cs` - Geometric Operations
The mathematical heart of AxOS. These operations are borrowed directly from Hyperdimensional Computing (HDC) theory:

| Operation | What It Does | Biological Analogy |
|---|---|---|
| **Bind** | Element-wise multiplication of two vectors | Linking two concepts together (e.g., "red" + "car" = "red car") |
| **Bundle** | Element-wise addition, optionally normalized | Superimposing multiple concepts into one representation |
| **Permute** | Circular shift of all elements by N positions | Changing the *context* or *order* of a concept |
| **CosineSimilarity** | Measures the angle between two vectors (-1 to +1) | "How similar are these two thoughts?" |
| **NormalizeL2** | Scales a vector to unit length | Ensuring all thoughts have equal "volume" |
| **Subtract** | Element-wise subtraction | Computing the *difference* between two states |
| **RandomHypervector** | Generates a pseudo-random unit vector from a seed | Creating a unique, reproducible identity for a concept |

**Why this matters**: In a 1024-dimensional space, two random vectors are *almost always* nearly orthogonal (i.e., cosine similarity ≈ 0). This means you can store thousands of distinct symbols without them interfering with each other. When you Bundle multiple symbols together and then compare the result against each original, the most similar one "wins" - this is how AxOS recognizes patterns without lookup tables.

#### `HdcAlgorithms.cs` - Advanced Search
More sophisticated algorithms that build on the basic operations:

- **Hopfield Relaxation**: Iteratively refines a noisy input toward the nearest stored memory (like how a blurry image gets sharper as your brain processes it).
- **Manifold Search**: Searches for the best match across a set of stored vectors using a fitness function.
- **Cosine Search**: Simple top-k nearest-neighbor search via cosine similarity.
- **Quantum Anneal**: A simulated annealing optimizer that explores solutions by randomly perturbing candidates and keeping improvements.

---

### 2. Brain - The Cognitive Layer

> **Biological Analogy**: The cerebral cortex, hippocampus, and limbic system.

This is where AxOS goes from "math library" to "thinking machine." The Brain layer is responsible for encoding sensory input, remembering experiences, recognizing patterns, and making decisions.

#### `HdcSystem.cs` - The Central Nervous System Hub
The `HdcSystem` is the master container that owns all cognitive subsystems:

- `Memory` (EpisodicMemory) - What the system has experienced
- `Symbols` (SymbolSpace) - The vocabulary of known concepts
- `Reflexes` (ReflexStore) - Trained automatic responses
- `Sequence` (SequenceEncoder) - The ability to understand ordered sequences
- `SignalPhase` (SignalPhaseAligner) - Real-time signal processing

It also handles **persistence**: the `SaveMapper` / `LoadMapper` methods serialize the entire learned state (symbols + reflexes) to a custom binary format (`BCMAPBIN`), allowing the system to remember its training across reboots.

#### `SymbolSpace.cs` - The Vocabulary
Every concept AxOS knows is represented as a high-dimensional vector in the SymbolSpace. When the system encounters a new word or token it hasn't seen before, it **automatically generates** a unique, deterministic vector for it using an FNV-1a hash of the token string as a random seed.

This means:
- The token `"hello"` always maps to the same vector, even across reboots.
- Two different tokens (e.g., `"cat"` and `"car"`) will generate nearly orthogonal vectors (very low similarity).
- The system never needs a hand-crafted dictionary - it builds its vocabulary on-the-fly.

#### `SequenceEncoder.cs` - Ordered Pattern Encoding
Encodes *ordered sequences* of tokens into a single hypervector using position-aware permutation. For example, the sequence `["the", "cat", "sat"]` is encoded differently from `["sat", "the", "cat"]` because each token's vector is rotated by its position index before being bundled together.

Also supports **k-mer tokenization** for raw string sequences (e.g., DNA strings, binary data), splitting the input into overlapping windows of `k` characters.

#### `CognitiveAdapter.cs` - The Decision Maker
This is the **System 1 / System 2 gateway** - the component that decides whether to fire a fast reflex or engage slow deliberation.

**How it works:**

1. **Analyze Heuristics**: When new data arrives, the CognitiveAdapter computes a `SignalProfile` - statistics about the input including mean, standard deviation, skewness, sparsity, entropy, and unique-value ratio.

2. **Dynamic Thresholds**: Based on the input's entropy and sparsity, it dynamically adjusts three thresholds:
   - `System1SimilarityThreshold`: How similar the input must be to a known reflex to trigger System 1 (fast path).
   - `CriticAcceptanceThreshold`: How good a System 2 result must be to be accepted.
   - `DeepThinkCostBias`: How metabolically expensive deep thinking will be.

3. **Route Dynamic Connectome**: When System 2 (deep thinking) is engaged, this method either:
   - Blends the input with the best matching memory from the working cache (`cache_bundle` strategy), or
   - Self-permutes and bundles the input to explore novel representations (`self_permute` strategy).
   - If the system is stuck after 32+ iterations and fitness is still low, it triggers **`discovery_induction`** - flagging the anomaly for later consolidation during sleep.

4. **DeduceGeometricGap**: Calculates the geometric difference between where the system *is* and where it *needs to be* - this is the "spatial differential" that gets recorded as an anomaly for sleep consolidation.

5. **Consolidate Memory (Sleep)**: During sleep, the adapter scans the working memory for high-fitness, low-burn entries and promotes them into permanent reflexes. This is how short-term learning becomes long-term knowledge.

#### `WorkingMemoryCache.cs` - Short-Term Memory (Hippocampus)
An LRU (Least Recently Used) cache with a twist - entries are ranked not just by recency, but by a composite score of **fitness**, **decay**, **metabolic efficiency**, and **hit count**.

Key features:
- **Cosine Similarity Hit**: When new input arrives, the cache checks if any existing entry is geometrically similar enough to trigger a System 1 reflex.
- **Time Decay**: Unused memories slowly fade (decay score multiplied by 0.97 each cycle), but never below a floor of 0.35.
- **Anomaly Flagging**: Entries flagged as anomalies (things the system couldn't understand) are held for consolidation during the next sleep cycle.
- **Metabolic Burn Tracking**: Each entry tracks how much energy it cost to compute, allowing the system to prioritize efficient reflexes.

#### `EpisodicMemory.cs` - Long-Term Memory (Log-Structured Trace)
A hierarchical, logarithmic trace structure inspired by skip lists. Memories are stored at multiple levels of granularity:

- **Level 0**: Individual experiences (exact tensors).
- **Level 1**: Merged pairs of experiences.
- **Level 2**: Merged pairs of Level 1 summaries.
- ... and so on up to 32 levels.

This means the system can recall:
- **Recent** experiences at full resolution (from the recent queue of up to 256 entries).
- **Older** experiences as progressively blurrier summaries (from the hierarchical trace).
- **By similarity**: "What have I seen that looks like this?"
- **By time**: "What happened N steps ago?"

#### `ReflexStore.cs` - Learned Automatic Responses (Muscle Memory)
Stores trained pattern→response mappings as vector + metadata entries. Each reflex has:
- A unique ID (e.g., `hw_key_a_4f2e8a01`)
- A vector (the geometric fingerprint of the trigger pattern)
- A stability score (how reliable this reflex is)
- An optional SymbolId (compact integer reference)
- Arbitrary metadata (label, source, creation info)

Reflexes can be queried by label, by target, or globally, sorted by stability. Duplicate detection uses SHA-1 hashing of the input sequence to avoid redundant entries.

#### `HeuristicConfig.cs` - The DNA Tuning Knobs
A configuration object that controls the thresholds and weights for the cognitive pipeline. These parameters define the "personality" of the system:

- **System 1** settings: How easily a reflex fires (base threshold, entropy/sparsity weights)
- **Critic** settings: How strict the quality gate is for accepting deep-think results
- **DeepThink** settings: How expensive deliberation is (base cost, entropy/sparsity scaling)
- **Consolidation** settings: Minimum fitness and maximum burn for sleep promotion
- **Metabolism** settings: Fatigue and zombie-mode activation ratios

#### `SignalPhaseAligner.cs` - Active Noise Cancellation
A real-time signal processing component that uses nearest-neighbor matching in state space to predict and cancel noise in incoming frames. Features adaptive gain control, lag estimation via cross-correlation, Tukey windowing for smooth edge tapering, and warm-up/ramp phases. This is designed for continuous data streams (e.g., audio, sensor data).

---

### 3. Kernel - The Autonomic Nervous System

> **Biological Analogy**: The brainstem, hypothalamus, and autonomic nervous system - the systems that keep you alive without conscious thought.

#### `KernelLoop.cs` - The Heartbeat
The central processing loop that orchestrates everything. Every piece of input flows through the `ProcessIngestPipeline`:

```
Input → Heuristic Analysis → Encode to Tensor → Remember
    ↓
Is it similar to something in WorkingMemory?
    ├── YES → System 1 Reflex (fast, cheap)
    └── NO  → System 2 Deep Thinking (slow, expensive)
              ↓
         Does the result pass the Critic?
              ├── YES → Cache it for future reflex use
              └── NO  → Was it a total anomaly?
                          ├── YES → Flag for Discovery
                          └── NO  → Fatigue / Zombie mode
```

The KernelLoop also:
- **Monitors substrate resources** (RAM, CPU) via the SubstrateMonitor and auto-scales its energy budget.
- **Auto-triggers sleep** when metabolic energy drops below thresholds or cognitive entropy (working memory diversity) gets too high.
- **Builds cache keys** using FNV-1a hashing of the input payload for efficient deduplication.

#### `KernelLoopContracts.cs` - Data Types
Defines the four key data structures that flow through the kernel:

- **`DataStream`**: Raw input (type, ID, payload string, dimension hint).
- **`SignalProfile`**: Statistical fingerprint of the input (entropy, sparsity, skewness, etc.).
- **`TensorOpCandidate`**: A candidate response with fitness score, similarity, cost, and strategy label.
- **`IngestResult`**: The complete result of processing an input (success, reflex hit, deep think, zombie, sleep, discovery flags).

#### `SystemMetabolism.cs` - The Energy Budget
Models the system's finite computational energy as a biological metabolism:

- **MaxCapacity**: Total energy budget (e.g., 1000 units).
- **CurrentEnergyBudget**: Remaining energy (depletes with every computation).
- **FatigueThreshold**: When energy drops below this (default 28% of max), System 2 thinking stops.
- **ZombieActivationThreshold**: When energy drops below this (default 20% of max), the system enters "zombie mode" - it can still process reflexes but with an extremely high critic threshold (0.95), meaning only near-perfect matches are accepted.
- **Recharge**: Sleep restores energy to full capacity.
- **CanDeepThink**: Returns `false` if energy is below fatigue or zombie mode is active.

#### `SleepCycleScheduler.cs` - The Circadian Clock
Determines when the system should sleep based on three triggers:

1. **Metabolic Drain**: Energy dropped below 8% of capacity.
2. **Cognitive Overload**: Working memory entropy exceeded 72% capacity (too many diverse, unresolved thoughts).
3. **Idle Consolidation**: The system has been idle for 30+ seconds and hasn't slept in 120+ seconds.

During sleep:
- Hardware interrupts are locked (no new input accepted).
- Working memory anomalies are consolidated into permanent reflexes.
- Time decay is applied to all working memory entries.
- Energy is fully recharged.

#### `ProgramManifold.cs` - Application Isolation (Programs as Organisms)
Each "program" in AxOS runs inside its own **ProgramManifold** - a completely isolated cognitive environment with its own:
- HdcSystem (separate vocabulary, reflexes, and memory)
- KernelLoop (separate energy budget, allocated from the host kernel)
- BatchController (separate input queue)
- Ruleset (its own "DNA" - the set of symbols and reflex triggers that define its behavior)

**Critical feature - Runtime Evolution**: When a ProgramManifold sleeps, it calls `EvolveRulesetDuringSleep()`, which:
1. Scans working memory for flagged anomalies.
2. For each anomaly, **mutates the Ruleset** by adding the anomaly's deduced constraint as a new symbol definition.
3. Creates new `ReflexTrigger` entries so the system will recognize the anomaly instantly next time.

This means **programs effectively mutate their own rulesets during sleep** based on what they couldn't handle while awake.

#### `Ruleset.cs` - Program DNA
A simple configuration object that defines a program's genetic makeup:
- `ConstraintMode`: A label for the execution context (e.g., `"x86_64_Physical"`, `"semantic_logic"`).
- `Heuristics`: The cognitive tuning parameters for this program.
- `SymbolDefinitions`: Pre-trained token→vector mappings (the program's innate vocabulary).
- `ReflexTriggers`: Pre-wired pattern→action triggers (the program's instincts).

#### `RulesetParser.cs` - DNA Transcription
Parses text-based Ruleset definitions (`.ax` files) into Ruleset objects. Supports sections for symbols (token = comma-separated float values) and reflex triggers (similarity threshold → action intent).

#### `BatchController.cs` - The Enteric Nervous System (Gut)
A simple FIFO queue that feeds DataStreams into the KernelLoop one at a time, aggregating statistics about the batch (processed, succeeded, reflex hits, deep think hits, zombie events, sleep cycles).

---

### 4. Hardware - The Sensory Organs

> **Biological Analogy**: Eyes, ears, skin - the organs that transduce physical stimuli into neural signals.

#### `HardwareSynapse.cs` - The Sensory Cortex (Developer API)
A **developer-facing reference framework** showing how raw hardware signals can be mapped into geometric HDC space. It is not currently wired into the live keyboard input path - keyboard input flows through Cosmos's built-in `Console.ReadKey()` and the `PeripheralNerve` serial driver. Instead, `HardwareSynapse` exists as a reference implementation that developers can use to HDC-encode any arbitrary hardware: USB devices, GPIO pins, sensor arrays, network packets, or anything else that produces raw bytes.

**How it works**: Given a byte array (a "raw pulse"), the `HardwareSynapse`:
1. Builds a `DataStream` from the pulse and runs heuristic analysis via the `CognitiveAdapter`.
2. Encodes the pulse into an 8192-dimensional hypervector, L2-normalizes it, and computes an FNV-1a hash.
3. Stores the vector + intent label as a permanent reflex in the `ReflexStore`.

**Recognition**: When a new pulse arrives, recognition works in two tiers:
1. **Exact match** (hash lookup): If the pulse hash matches a trained signal, return instantly.
2. **Cosine similarity search**: Compare the encoded pulse against all stored hardware vectors, return the best match if it exceeds the recognition threshold (default 0.90).

**Pre-built profile**: The `TrainStandardKeyboardProfile()` method seeds the full USB HID keyboard layout (108 keys: A-Z, 0-9, F1-F12, modifiers, arrows, numpad, etc.) as a demonstration. This proves the encoding pipeline works end-to-end but is not currently integrated into the shell's live input loop.

**Key insight**: Because recognition is geometric (cosine similarity), the system can recognize *similar-but-not-identical* signals - e.g., a slightly noisy scan code from degrading hardware. This means developers can map *any* hardware interface without writing traditional byte-matching driver code. Define the training pulses, call `TrainSignal()`, and the HDC engine handles recognition automatically.

#### `PeripheralNerve.cs` - The Vocal Cord / Motor Nerve
Direct hardware I/O driver for the COM1 serial port. This is how AxOS communicates with the outside world at the lowest level:
- Configures the UART registers (baud rate divisor, line control, FIFO, modem control).
- `Write(string)`: Sends bytes character-by-character, waiting for the transmit buffer to clear.
- `TryReadLine(string)`: Non-blocking line reader with backspace support.

This is the "vocal cord" used by the SYSCALL demo to prove that AxOS can bridge internal geometric computation to real-world hardware output.

#### `SubstrateMonitor.cs` - Body Awareness (Proprioception)
Reads the actual physical state of the hardware:
- **RAM**: Total and available memory via `Cosmos.Core.CPU.GetAmountOfRAM()`.
- **CPU**: Cycle count (when available).
- **RTC**: Real-time clock (hour, minute, second).
- **Budget Computation**: Automatically calculates a recommended kernel energy budget based on available RAM, CPU speed, memory pressure, and uptime. This budget is used to auto-scale the metabolism so the system adapts to the hardware it's running on.

---

### 5. Storage - The Holographic File System

> **Biological Analogy**: Declarative memory - the ability to store and recall facts by their *meaning*, not their location.

#### `HolographicFileSystem.cs` - Intent-Based Storage
In a traditional filesystem, you save a file at `/home/user/notes.txt` and retrieve it by that exact path. In AxOS's Holographic File System (HFS), you save a file with an **intent** (a natural language description of what it is) and retrieve it by **asking for what you want**.

**How it works:**

1. **Write**: `hfs write "grocery list" "milk, eggs, bread"` → The intent and content are each tokenized, encoded into hypervectors, and bound together. The composite vector is stored to disk as a binary `.hfs` file with a hash-based filename.

2. **Read**: `hfs read "shopping items"` → The query `"shopping items"` is encoded into a hypervector and compared against all stored entries using a blended similarity score (75% intent similarity + 25% payload similarity). The best match is returned.

3. **Search**: Returns ranked results, so you can see how well each stored entry matches your query.

The filesystem maintains a binary index file (`index.axidx`) that lists all entry IDs, and each entry's vectors are persisted alongside its content for fast recall after reboot.

---

### 6. Shell - The Exoskeleton

> **Biological Analogy**: The skin and skeletal system - the outer interface between the organism and the environment.

#### `Exoskeleton.cs` - The User Interface
A 2,343-line Cosmos kernel class that provides the interactive shell. It supports both VGA console input (keyboard) and serial (COM1) input, with automatic detection of which interface the user is connected through.

**Available Commands:**

| Command | Description |
|---|---|
| `help` | List all commands |
| `cls` / `clear` | Clear screen |
| `echo <text>` | Print text |
| `hdc stats` | Show HDC system statistics |
| `hdc remember "text"` | Encode text and store in episodic memory |
| `hdc recall "query"` | Find the closest memory to a query |
| `hdc ago <N>` | Recall what happened N steps ago |
| `hdc symbol <token>` | Show/generate the vector for a token |
| `hdc encode "text"` | Encode text into a hypervector |
| `hdc sim "a" "b"` | Compute similarity between two texts |
| `hdc seq <sequence>` | Encode a raw sequence with k-mer tokenization |
| `hdc promote <id> <label>` | Promote a symbol to a reflex |
| `hdc query <label>` | Query reflexes by label |
| `hfs init [path]` | Initialize the Holographic File System |
| `hfs write "intent" "content"` | Write a holographic file |
| `hfs read "intent"` | Read by intent similarity |
| `hfs search "intent"` | Search with ranked results |
| `hfs list [N]` | List recent entries |
| `save "intent" "content"` | Shortcut for `hfs write` |
| `find "intent"` | Shortcut for `hfs read` |
| `run "intent"` | Load a `.ax` program from HFS and spawn a ProgramManifold |
| `holopad [dim]` | Interactive associative memory editor |
| `mapper save/load <path>` | Save/load the trained brain state |
| `algo <name> <args>` | Run HDC algorithms (hopfield, anneal, cosine search) |
| `kernel status` | Show kernel metabolism and sleep state |
| `kernel ingest "text"` | Feed text through the full cognitive pipeline |
| `kernel sleep` | Manually trigger a sleep cycle |
| `synapse seed` | Train the standard keyboard profile |
| `synapse pulse <hex>` | Send a raw hardware pulse for recognition |
| `appdemo` | Run the full demo suite |
| `kerneltest` | Run biological stress tests |
| `reboot` / `shutdown` | System power control |

**HoloPad** is a special interactive mode - a "thought notepad" where you write free-form text and the system stores it holographically, then you can recall related thoughts by intent.

---

### 7. Diagnostics - The Immune System

> **Biological Analogy**: The immune system - continuous self-testing to make sure everything is working.

#### `KernelTest.cs` - Biological Stress Test
A comprehensive diagnostic that exercises every subsystem:
1. Reads substrate state (RAM, CPU, RTC).
2. Boots the metabolism from substrate-recommended budget.
3. Runs a smoke test through the ingest pipeline.
4. Polls the sleep scheduler.
5. Seeds the hardware synapse with all 108 keyboard keys.
6. Tests recognition of known and unknown hardware pulses.
7. Trains a new hardware signal and verifies post-training recognition.
8. Triggers a manual sleep cycle and verifies energy recharge.
9. Spawns a ProgramManifold and runs a batch through it.
10. Performs final health checks (physical budget > 0, energy valid, sleep advanced, smoke completed).

#### `AppDemo.cs` - The Proof-of-Concept Suite
Six demos that each prove a different fundamental capability of the architecture. See the [Demo Suite](#demo-suite) section below for full annotated output.

---

## How It Actually Works (End-to-End)

Here's what happens when you type a command while AxOS is running:

1. **Physical Input**: Keystrokes arrive via one of two paths - the VGA console (Cosmos's built-in `Console.ReadKey()`) or the COM1 serial port (`PeripheralNerve.TryReadLine()`). The `Exoskeleton` shell polls both sources simultaneously in its `ReadAutonomicLine()` loop and assembles a complete command string.

2. **Shell Dispatch**: The `Exoskeleton` tokenizes the command and routes it to the appropriate handler - `hdc`, `hfs`, `kernel`, `synapse`, `algo`, etc.

3. **Cognitive Pipeline** (when using `kernel ingest`): If a command feeds data through the full cognitive pipeline, the payload becomes a `DataStream` and enters the `KernelLoop.ProcessIngestPipeline()`.

4. **System 1 Check**: The kernel encodes the payload into a tensor & checks the WorkingMemoryCache. If a similar pattern exists above the adaptive similarity threshold → reflex hit. Cost: ~1 energy unit.

5. **System 2 (if no reflex)**: The CognitiveAdapter iterates up to 64 times, blending the input with cached memories or self-permuting to explore novel representations. Each iteration costs energy proportional to the input's entropy. If a candidate passes the critic threshold → success. If not → fatigue or zombie mode.

6. **Sleep (if triggered)**: If energy drops below 8%, or working memory entropy exceeds 72%, or the system is idle for 30+ seconds - sleep is triggered. Working memory is scanned, high-quality entries are promoted to permanent reflexes, anomalies mutate the program's Ruleset, and energy is recharged.

7. **Next time**: The same input arrives → System 1 reflex fires instantly. The system has learned.

> **Note on HardwareSynapse**: The `HardwareSynapse` is a developer API for mapping *any* raw hardware signal (bytes) into geometric HDC space. It is not currently in the live keyboard input path - it exists as a framework showing how future hardware drivers can be built using geometric recognition instead of traditional byte-matching. See the `synapse seed` and `synapse pulse` shell commands to experiment with it.

---

## Key Concepts for Non-Programmers

### What is a Hypervector?
Imagine a "direction" in a space with 1,024 dimensions (instead of the 3 dimensions we live in). Each concept - a letter, a word, an opcode - gets its own unique direction. Because there are so many dimensions, random directions almost never overlap. This is why AxOS can store thousands of concepts without them getting confused.

### What is Cosine Similarity?
It measures the "angle" between two hypervectors. If two vectors point in exactly the same direction → similarity = 1.0 (identical concepts). If they're perpendicular → similarity ≈ 0 (unrelated concepts). If they point in opposite directions → similarity = -1.0.

### What is Tensor Permutation?
Imagine a row of 1,024 colored marbles. "Permuting by 5" means sliding every marble 5 positions to the right (with the ones that fall off the end wrapping around to the beginning). This changes the *context* of a concept without changing its identity - the same marbles are there, just in a different arrangement.

### Why is Sleep Important?
Just like your brain consolidates memories during REM sleep, AxOS uses sleep cycles to:
- Move useful short-term memories into permanent reflexes.
- Apply decay to stale memories.
- Recharge the energy budget.
- Evolve program DNA based on unresolved anomalies.

---

## Building & Running

### Prerequisites
- [.NET 6.0 SDK](https://dotnet.microsoft.com/download) or later
- [Cosmos DevKit](https://github.com/CosmosOS/Cosmos) (Visual Studio extension or command-line tools)
- A virtual machine (QEMU, VMware, or Hyper-V) or a physical x86 machine for testing

### Build
```bash
# From the AxOS solution root
dotnet build AxOS.sln
```

### Run in QEMU
Cosmos integrates with QEMU. After building, launch the project through Visual Studio with the Cosmos debugger, or use the convenience script:
```bash
.\run.bat nobuild serial
```
Or manually:
```bash
qemu-system-x86_64 -cdrom AxOS.iso -serial stdio -m 256
```

The `-serial stdio` flag connects the COM1 serial port to your terminal, so you can interact via the serial shell.

---

## Demo Suite - Annotated Proof of Architecture

AxOS runs its full test and demo suite automatically on every boot. Below is **real terminal output** from a live bare-metal boot inside QEMU, with annotations explaining what each line proves and why it matters.

### Kernel Diagnostics - "Is the Organism Alive?"

The kernel test is the first thing that runs. It exercises every subsystem to answer one question: *does this living system have a heartbeat?*

```
=== KERNEL DIAGNOSTICS START ===

[SUBSTRATE]
substrate: recommended_budget=429.53, ram_mb=216/254, used_est=434176, rtc=09:34:01
```

> **What this proves**: The `SubstrateMonitor` read the actual physical hardware - 254 MB of total RAM, 216 MB available, real-time clock at 09:34:01 - and *automatically* computed an energy budget of ~430 units. No human configured this. The OS sensed its own body and set its metabolism accordingly.

```
[METABOLISM: BASELINE]
kernel_status: energy=429.52/429.52, energy_pct=100, fatigue=120.26, fatigue_ratio=0.28,
    zombie_at=85.90, zombie_ratio=0.2, zombie_critic=0.95, zombie=False
```

> **What this proves**: The metabolism booted at 100% energy with fatigue kicking in at 28% and zombie mode at 20%. These aren't magic numbers - they're derived from the physical substrate. Give AxOS more RAM, and it gets a bigger energy budget. Give it less, and it tightens its belt. **The OS energy model scales based on the underlying hardware.**

```
[INGEST: SMOKE TEST]
smoke: outcome=fatigue_limit, success=False, reflex=False, deep=True, zombie=False, energy=115.15
```

> **What this proves**: The kernel received a novel input (`SYS_BOOT_01 SYS_BOOT_02`), found no reflex match, engaged System 2 deep thinking for 64 iterations, burned ~314 energy units trying to understand it, and hit the fatigue limit. **This is correct behavior** - it proves the metabolic throttle works. The system didn't crash on unknown input; it *got tired trying to figure it out*, just like a brain.

```
[SLEEP: MANUAL]
sleep: energy_before=114.58, energy_after=427.31, sleeps=0->1
```

> **What this proves**: Sleep recharged energy from ~115 back to ~427 (full capacity) and advanced the sleep cycle counter. The OS literally went to sleep and woke up refreshed. During this sleep, any anomalies in working memory would have been consolidated into permanent reflexes.

```
[CHECKS]
check_physical_budget=True
check_energy_valid=True
check_sleep_advanced=True
check_smoke_outcome=True
kernel_diagnostics_pass=True
=== KERNEL DIAGNOSTICS COMPLETE ===
```

> **What this proves**: All four health checks passed - the organism is alive and functioning. The immune system gives it a clean bill of health.

---

### Demo 1: App Manifold Isolation - "Programs Are Organisms"

> **Why this matters**: In a traditional OS, programs share the same CPU and memory bus. If one program misbehaves, it can crash the whole system. In AxOS, each program runs inside its own **ProgramManifold** - an isolated runtime environment with its own dedicated brain, metabolism, and ruleset (DNA). This demo proves that isolation works.

```
=== APP MANIFOLD ISOLATION DEMO ===
global_before: energy=427.13/427.13, zombie=False
app_budget: allocation=15.0%, local_max=64.06
```

> The host kernel has ~427 energy. The app manifold is allocated only 15% of that (~64 units). The app gets its own private energy pool - it can't drain the host.

```
app_phase1: run_batch_start
app_run1: processed=1, deep=1, reflex=0, zombie=0
app_after_run1: energy=39.27, zombie=False
```

> **Pass 1**: The app encountered a novel input. No reflexes existed yet (this is a newborn organism), so it engaged System 2 deep thinking, burning ~25 energy units. It learned *something* but it was expensive.

```
app_phase2: sleep_start
app_phase2: sleep_complete
```

> The app goes to sleep. During sleep, its `EvolveRulesetDuringSleep()` method scans working memory for anomalies and **mutates its own Ruleset** - adding new symbol definitions and reflex triggers for whatever it struggled with during Pass 1.

```
app_phase3: run_batch_start
app_run2: processed=1, deep=0, reflex=1, zombie=0
app_after_run2: energy=62.81, zombie=False
```

> **Pass 2**: The **same input** is now processed via **System 1 reflex** (deep=0, reflex=1). The app recognized it instantly because it evolved the reflex during sleep. Energy cost was negligible (~1.2 units instead of ~25). **The app evolved its own handling logic.**

```
global_after: energy=426.54/426.54, zombie=False, delta=0.59
```

> **Critical proof**: The global kernel's energy barely changed (delta=0.59 out of 427). The app burned through its own budget, slept, evolved, and recovered - all without affecting the host. **Total isolation.**

---

### Demo 2: X86 Hardware Virtualization - "Geometric instruction representation experiment"

> **Why this matters**: This demo shows that instruction semantics can be represented and manipulated geometrically rather than via traditional opcode switch dispatch. AxOS can represent registers, opcodes, and execution as high-dimensional coordinates - meaning you could theoretically emulate *any* hardware architecture just by defining new tensor mappings.

```
=== X86 HARDWARE VIRTUALIZATION DEMO ===
x86_init: registers and opcodes mapped to geometric space.
x86_fetch: instruction bundled (OP_ADD + REG_RAX).
x86_decode: intent similarity to OP_ADD = 0.707107
```

> A CPU register (`RAX`) and an opcode (`ADD`) are each represented as a 1024-dimensional vector. When bundled together (superimposed), the resulting vector still has a 0.707 cosine similarity to the original `ADD` - enough for the system to recognize the *intent* of the instruction. This is how real neural circuits work: multiple signals overlap, and the brain decodes the dominant one.

```
x86_execute: RAX state physically rotated (ADD 5) via geometric reflex.
```

> The `ADD 5` operation is executed by **physically rotating the register's tensor by 5 positions** (permutation). This isn't a metaphor - the actual 1024-element float array is circular-shifted. The value `5` is now encoded in the *geometry* of the register, not in a binary integer.

```
--- x86 RIGID DETERMINISM TEST ---
x86_det_decode: identity match = 1
x86_det_execute: EXACT MATCH SUCCESS. Register state mutated.
x86_det_corrupt: similarity = 0.99995
x86_det_fail: SEGMENTATION FAULT DETECTED. Manifold isolated.
```

> **The killer proof**: An exact copy of the instruction vector has similarity = 1.0 (perfect). But with a *single float element* perturbed by 0.01, similarity drops to 0.99995 - and the crystalline-mode critic (threshold = 1.0) **rejects it**. AxOS detected a corruption of 0.005% in a 1024-dimensional space. The geometry acts as a high-dimensional integrity check — tiny perturbations reduce similarity and can be rejected under strict thresholds.

---

### Demo 3: Raw Binary Ingestion - "Feed It Machine Code"

> **Why this matters**: This proves that AxOS doesn't just work with text or semantic data - you can feed it **raw x86 machine code bytes** and it will fetch, decode, and execute them using geometric recognition. No traditional opcode switch dispatch is used in the execution path — recognition occurs via geometric similarity.

```
=== RAW BINARY INGESTION DEMO ===
binary_loader: loaded 3 bytes of machine code.
cpu_fetch: reading byte 0xC7
cpu_execute: EXACT MATCH for 0xC7. Reg state rotated by 10.
cpu_fetch: reading byte 0x83
cpu_execute: EXACT MATCH for 0x83. Reg state rotated by 5.
cpu_fetch: reading byte 0xC3
cpu_execute: RET instruction. Binary execution complete.
=== BINARY INGESTION COMPLETE ===
```

> Three raw bytes: `C7 83 C3` (MOV, ADD, RET). Each byte is converted to a one-hot tensor, compared against the Ruleset's symbol definitions via cosine similarity, and matched at ≥0.999 confidence. The system converted *arbitrary bytes into geometric intent and acted on it*. Want to support a different ISA? Just define new symbols - no new code needed.

---

### Demo 4: PE Ribosome - "Reading a Windows Executable Like DNA"

> **Why this matters**: A Windows `.exe` file starts with a `MZ` DOS header, followed eventually by a `PE` signature. Traditional parsers use byte offsets and magic-number checks. AxOS parses it using a **biological state machine** driven entirely by tensor similarity - the same way a ribosome reads codons from a template.

```
=== PE RIBOSOME (HEADER PARSING) DEMO ===
ribosome: ingesting 8-byte simulated stream.
ribosome: 'MZ' detected. Dos stub recognized. Transitioning state.
ribosome: byte_pair 0000 ignored (no reflex match).
ribosome: 'PE' detected. Windows header located.
ribosome: Metamorphosis complete. Switcing to x86_64_Physical context.
=== PE RIBOSOME COMPLETE ===
```

> The system ingests 8 bytes in 2-byte windows. The first window `4D 5A` encodes to a tensor that matches the `MZ` signature vector at ≥0.99 - state transitions to "searching for PE." The `00 00` padding is ignored (no reflex match, below threshold). Then `50 45` matches the `PE` signature, triggering "metamorphosis" into x86 execution mode.
>
> **The insight**: The system doesn't *compare bytes*. It compares *geometric shapes*. This means it could handle slightly malformed headers, novel file formats, or even completely unknown binary structures - by recognizing *approximate* matches rather than demanding exact byte sequences. This is how biological immune systems recognize novel viruses: not by exact match, but by shape similarity.

---

### Demo 5: Neuroplastic Discovery - "Runtime reflex promotion for unknown symbols"

> **Why this matters**: This is the signature demo of the entire architecture. It proves that AxOS can encounter **completely unknown data**, struggle with it, go to sleep, and wake up having permanently **mutated its own ruleset (DNA)** to handle that data instantly. No human intervention. No recompilation. The Ruleset was mutated at runtime based on flagged anomalies.

```
=== NEUROPLASTIC DISCOVERY (KERNEL-INTEGRATED) DEMO ===
discovery_init: OS knows RET (0xC3). PUSH (0x6A) and POP (0x58) are unknown.
```

> The manifold is born with only one reflex: `0xC3` (RET). It has never seen `0x6A` (PUSH) or `0x58` (POP).

```
--- DISCOVERY PASS 1: SYSTEM 2 INDUCTION ---
```

> Pass 1 feeds `0x6A`, `0x58`, and `0xC3` through the pipeline. The first two are **anomalies** - the CognitiveAdapter can't find any similar reflex in working memory, so it engages System 2 deep thinking. After 32+ failed iterations, it triggers `discovery_induction`, which computes a `DeduceGeometricGap` - the spatial differential between the current state and where it needs to be - and flags the anomaly in working memory. `0xC3` matches the existing reflex and fires System 1.

```
--- AUTONOMIC SLEEP: GENETIC MUTATION ---
autonomic_sleep: Consolidating hippocampal anomalies into long-term DNA...
sleep_complete: Reflexes for 0x6A and 0x58 burned into Ruleset.
```

> During sleep, `ProgramManifold.EvolveRulesetDuringSleep()` scans working memory for flagged anomalies. For each one, it:
> 1. Adds the anomaly's deduced constraint vector to `Ruleset.SymbolDefinitions` (new vocabulary).
> 2. Creates a new `ReflexTrigger` with the minimum critic threshold (instant fire).
>
> **The Ruleset (DNA) has been permanently mutated.** The organism that wakes up is genetically different from the one that fell asleep.

```
--- DISCOVERY PASS 2: SYSTEM 1 REFLEX ---
discovery_verification: 100% reflex hits. Ruleset successfully mutated.
=== NEUROPLASTIC DISCOVERY COMPLETE ===
```

> Pass 2 feeds the **exact same inputs**. This time: 100% reflex hits. Zero deep thinking. Near-zero energy cost. The system recognizes `0x6A` and `0x58` as instantly as it recognizes `0xC3`, because they are now part of its permanent muscle memory.
>
> **What this proves about the architecture**: Any data format or protocol can be learned and handled through structured rule definitions and encoding pipelines. This approach significantly reduces the need for explicit branching logic or hand-coded parsers. Expose the system to examples, let it struggle, let it sleep, and it will **consolidate its own reflex support** for the encoded signals. This is metabolic compilation - the system compiles its own adaptive behavior from environmental exposure.

---

### Demo 6: Biological Calculator - "Math Through Geometry, Output to Real Hardware"

> **Why this matters**: This is the end-to-end proof. It combines everything: tensor-based register operations, geometric opcode recognition, arithmetic via physical rotation, and a **real hardware SYSCALL that writes the answer to the bare-metal COM1 serial port**. The answer `12` didn't come from integer addition - it came from measuring how far a point rotated in 1024-dimensional space.

```
=== BIOLOGICAL CALCULATOR EXECUTION DEMO ===
debug_sym_map: 0xC7 peak_idx=199
debug_sym_map: 0x83 peak_idx=131
debug_sym_map: 0xC3 peak_idx=1022
debug_sym_map: 0xAA peak_idx=170
```

> Each opcode is mapped to a unique one-hot peak in the 1024-dimensional space. These are the "addresses" in geometric space - deterministic and collision-free.

```
user_input: Injecting A=5, B=7 into geometric space.
cpu_execute: MOV RAX, 5 -> RAX rotated by 5
cpu_execute: MOV RBX, 7 -> RBX rotated by 7
cpu_execute: ADD RAX, RBX -> RAX physically rotated by RBX magnitude (7).
```

> Two `MOV` instructions rotate registers RAX and RBX by 5 and 7 positions respectively. The `ADD` instruction measures how far RBX has shifted from its origin (= 7), then rotates RAX by that amount. RAX is now rotated 5 + 7 = 12 positions from its origin. While the silicon ALU still performs the underlying tensor math, the **high-level arithmetic is resolved end-to-end through geometric state.**

```
interrupt_spike: SYSCALL detected. Engaging Peripheral Nerve.
STDOUT -> 12
syscall_complete: Tensor state flushed to bare-metal COM1 port (12).
```

> The `0xAA` byte triggers a SYSCALL interrupt. The system extracts the answer by measuring RAX's geometric shift from origin (`MeasureGeometricShift` → 12), then **physically writes "STDOUT -> 12" to the COM1 serial port** via `PeripheralNerve.WriteLine()`. This isn't a simulation - those bytes traveled through real UART hardware registers to the host terminal.

```
cpu_execute: RET -> Calculation complete.

=== EXTRACTION RESULTS ===
Expected Answer: 12
HDC Tensor State Output: 12
verify: SUCCESS. Bare metal logic executed perfectly in geometric space.
```

> **The punchline**: Feed AxOS a set of rules (a Ruleset defining `MOV`, `ADD`, `SYSCALL`, `RET` as tensors), give it a binary payload of 5 bytes, and it will:
> 1. Recognize each byte geometrically
> 2. Perform arithmetic via tensor rotation
> 3. Output the result to real hardware
> 4. Return the correct answer
>
> The execution pipeline is architecture-agnostic given appropriate symbol and reflex definitions - whether it's DNA sequence analysis, neural network inference, or a Forth interpreter. The architecture doesn't care what the data means. It only cares about shape.

---

### Demo 7: Noisy Opcode Learning - "Robustness Through High-Dimensionality"

> **Why this matters**: Real-world signals are never perfect. This demo proves that AxOS can learn to classify complex, noisy data (32-byte pulses with random jitter) and achieve **100% accuracy** (in this controlled demo configuration) while maintaining the ability to reject completely unknown "alien" signals.

```text
=== NOISY OPCODE LEARNING DEMO ===
goal: learn 8 toy opcodes from 32-byte noisy pulses, classify unseen pulses after sleep.

--- POST-SLEEP EVALUATION ---
post_sleep_accuracy: 96/96 (100%)

--- UNKNOWN OPCODE DETECTION ---
alien_pulse: max_sim=0.566 margin=0.287 (closest=OP_0)
alien_verdict: CORRECTLY REJECTED as unknown opcode.

--- CONFUSION MATRIX (Strict-Gated with REJECT) ---
        OP_0 OP_1 OP_2 OP_3 OP_4 OP_5 OP_6 OP_7 REJECT 
OP_0    11     0     0     0     0     0     0     0     1  
...
strict_total: 96 / 96
reject_rate:  2/96 (2.08%)
```

> **The insight**: The system learns 8 prototype "opcodes" from noisy examples. The "baseline" is a **training-example nearest-neighbor** classifier on raw bytes (forced Top-1); AxOS reports both forced accuracy and a strict gated mode (with explicit REJECT). In this run, the HDC system achieved 100% accuracy on recognized samples and had a negligible 2% reject rate. It correctly identified the structured "alien" pulse as unknown because it lacked the high-dimensional geometric fingerprint of the trained set.

---

## Project status

AxOS is an experimental research OS and cognitive runtime. Current focus:
- **Correctness + Determinism**: Ensuring tensor invariants and numerical stability across reboots.
- **Demonstrable Learning Loops**: Refining sleep consolidation and reflex promotion for more complex patterns.
- **Hardware I/O Reliability**: Hardening the serial console and sensory ingestion pipeline.
- **Architectural Clarity**: Maintaining a strict separation between toy ISA discovery and bare-metal execution.

**Roadmap**:
- Realistic discovery tasks with measurable generalization.
- Stronger cognitive sandboxing between manifolds.
- Persistence + reproducible evaluation harnesses.

---

## File Reference

| Subsystem | File | Purpose |
|---|---|---|
| **Core** | `Shape.cs` | Tensor dimension descriptor |
| | `Tensor.cs` | N-dimensional data container |
| | `TensorOps.cs` | HDC vector operations (Bind, Bundle, Permute, Cosine, Normalize) |
| | `HdcAlgorithms.cs` | Advanced search algorithms (Hopfield, Anneal, Manifold) |
| **Brain** | `HdcSystem.cs` | Central brain hub + binary persistence |
| | `CognitiveAdapter.cs` | System 1/2 gateway, heuristic analysis, sleep consolidation |
| | `WorkingMemoryCache.cs` | LRU + fitness-prioritized short-term memory |
| | `EpisodicMemory.cs` | Hierarchical log-structured long-term memory |
| | `HeuristicConfig.cs` | Cognitive tuning parameters (DNA) |
| | `ReflexStore.cs` | Trained automatic response dictionary |
| | `SymbolSpace.cs` | Token → hypervector registry |
| | `SequenceEncoder.cs` | K-mer tokenization and HDC sequence encoding |
| | `SignalPhaseAligner.cs` | Real-time ANC signal processing |
| **Kernel** | `KernelLoop.cs` | Central processing pipeline |
| | `KernelLoopContracts.cs` | Data types (DataStream, SignalProfile, IngestResult) |
| | `SystemMetabolism.cs` | Energy budget and fatigue modeling |
| | `SleepCycleScheduler.cs` | Circadian sleep trigger logic |
| | `BatchController.cs` | FIFO input queue for batch processing |
| | `RulesetParser.cs` | Text-based Ruleset (DNA) parser |
| | `ProgramManifold.cs` | Isolated program execution environment |
| | `Ruleset.cs` | Program DNA (symbols + reflex triggers) |
| **Hardware** | `HardwareSynapse.cs` | Hardware-to-HDC signal encoder (Developer API) |
| | `PeripheralNerve.cs` | COM1 serial port driver |
| | `SubstrateMonitor.cs` | Physical resource monitoring (RAM, CPU, RTC) |
| **Storage** | `HolographicFileSystem.cs` | Intent-based holographic file storage |
| **Shell** | `Exoskeleton.cs` | Interactive shell (VGA + Serial) |
| **Diagnostics** | `KernelTest.cs` | Kernel stress test suite |
| | `AppDemo.cs` | Proof-of-concept demo suite |

**28 source files across 7 subsystems - a research OS prototype exploring self-modifying rule systems.**

---

<p align="center">
  <em>"It didn't crash. It just got exhausted and lost its train of thought."</em>
</p>
