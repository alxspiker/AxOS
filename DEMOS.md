# AxOS Demo Suite (Annotated Proof)

This file contains annotated excerpts of **real** QEMU boot output produced by the AxOS demo suite.

Notes:
- Some runs may end with a non-zero QEMU exit code due to an intentional shutdown path. Treat **demo completion** as success.
- “x86 virtualization” here refers to **conceptual experiments inside HDC space**, not a claim of arbitrary x86 binary semantic decoding.

---

## Table of contents

- [Kernel diagnostics](#kernel-diagnostics)
- [Demo 1: Manifold isolation](#demo-1-manifold-isolation)
- [Demo 2: Geometric instruction representation experiment](#demo-2-geometric-instruction-representation-experiment)
- [Demo 3: Raw binary ingestion (toy mapping)](#demo-3-raw-binary-ingestion-toy-mapping)
- [Demo 4: PE ribosome (signature detection)](#demo-4-pe-ribosome-signature-detection)
- [Demo 5: Neuroplastic discovery (reflex promotion)](#demo-5-neuroplastic-discovery-reflex-promotion)
- [Demo 6: Biological calculator (geometric arithmetic)](#demo-6-biological-calculator-geometric-arithmetic)
- [Demo 7: Noisy opcode learning (classification + reject)](#demo-7-noisy-opcode-learning-classification--reject)

---

## Kernel diagnostics

The kernel test validates substrate sensing, energy model initialization, sleep/recharge, and basic ingest behavior.

```text
=== KERNEL DIAGNOSTICS START ===

[SUBSTRATE]
substrate: recommended_budget=429.53, ram_mb=216/254, used_est=434176, rtc=09:34:01
```

Annotation: `SubstrateMonitor` reads RAM/RTC and derives a recommended energy budget.

```text
[METABOLISM: BASELINE]
kernel_status: energy=429.52/429.52, energy_pct=100, fatigue=120.26, fatigue_ratio=0.28,
    zombie_at=85.90, zombie_ratio=0.2, zombie_critic=0.95, zombie=False
```

Annotation: energy is initialized; fatigue and zombie thresholds are configured from budget ratios.

```text
[INGEST: SMOKE TEST]
smoke: outcome=fatigue_limit, success=False, reflex=False, deep=True, zombie=False, energy=115.15
```

Annotation: novel input routes to System 2 search, consumes budget, and hits fatigue limit without crashing.

```text
[SLEEP: MANUAL]
sleep: energy_before=114.58, energy_after=427.31, sleeps=0->1
```

Annotation: sleep recharges energy to capacity and advances the sleep counter.

---

## Demo 1: Manifold isolation

```text
=== APP MANIFOLD ISOLATION DEMO ===
global_before: energy=427.13/427.13, zombie=False
app_budget: allocation=15.0%, local_max=64.06
```

Annotation: the app receives a bounded local budget derived from the host.

```text
app_run1: processed=1, deep=1, reflex=0, zombie=0
app_after_run1: energy=39.27, zombie=False
```

Annotation: newborn manifold uses System 2 (no reflexes yet).

```text
app_phase2: sleep_complete
```

Annotation: manifold sleep allows consolidation within its own scope.

```text
app_run2: processed=1, deep=0, reflex=1, zombie=0
app_after_run2: energy=62.81, zombie=False
global_after: energy=426.54/426.54, zombie=False, delta=0.59
```

Annotation: the same input becomes a reflex hit after sleep; host energy changes minimally.

---

## Demo 2: Geometric instruction representation experiment

```text
=== X86 HARDWARE VIRTUALIZATION DEMO ===
x86_init: registers and opcodes mapped to geometric space.
x86_fetch: instruction bundled (OP_ADD + REG_RAX).
x86_decode: intent similarity to OP_ADD = 0.707107
```

Annotation: opcode/register concepts are encoded as vectors; bundling preserves partial similarity.

```text
x86_execute: RAX state physically rotated (ADD 5) via geometric reflex.
```

Annotation: arithmetic intent is represented via permutation/rotation in the vector state.

```text
--- x86 RIGID DETERMINISM TEST ---
x86_det_decode: identity match = 1
x86_det_corrupt: similarity = 0.99995
x86_det_fail: SEGMENTATION FAULT DETECTED. Manifold isolated.
```

Annotation: strict similarity thresholds can reject tiny perturbations under “crystalline” gating.

---

## Demo 3: Raw binary ingestion (toy mapping)

```text
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

Annotation: bytes are mapped via a constrained ruleset symbol table into geometric intents.

---

## Demo 4: PE ribosome (signature detection)

```text
=== PE RIBOSOME (HEADER PARSING) DEMO ===
ribosome: ingesting 8-byte simulated stream.
ribosome: 'MZ' detected. Dos stub recognized. Transitioning state.
ribosome: byte_pair 0000 ignored (no reflex match).
ribosome: 'PE' detected. Windows header located.
ribosome: Metamorphosis complete. Switching to x86_64_Physical context.
=== PE RIBOSOME COMPLETE ===
```

Annotation: the demo detects signatures by windowed ingestion + similarity gating, acting like a state machine.

---

## Demo 5: Neuroplastic discovery (reflex promotion)

```text
=== NEUROPLASTIC DISCOVERY (KERNEL-INTEGRATED) DEMO ===
discovery_init: OS knows RET (0xC3). PUSH (0x6A) and POP (0x58) are unknown.
```

```text
--- DISCOVERY PASS 1: SYSTEM 2 INDUCTION ---
```

Annotation: unknown symbols become anomalies and are flagged for consolidation.

```text
--- AUTONOMIC SLEEP: GENETIC MUTATION ---
autonomic_sleep: Consolidating hippocampal anomalies into long-term DNA...
sleep_complete: Reflexes for 0x6A and 0x58 burned into Ruleset.
```

Annotation: after sleep, the ruleset gains new symbol/reflex entries for previously unknown tokens (in this constrained demo model).

```text
--- DISCOVERY PASS 2: SYSTEM 1 REFLEX ---
discovery_verification: 100% reflex hits. Ruleset successfully mutated.
=== NEUROPLASTIC DISCOVERY COMPLETE ===
```

Annotation: the same inputs now route through System 1.

---

## Demo 6: Biological calculator (geometric arithmetic)

```text
=== BIOLOGICAL CALCULATOR EXECUTION DEMO ===
user_input: Injecting A=5, B=7 into geometric space.
cpu_execute: MOV RAX, 5 -> RAX rotated by 5
cpu_execute: MOV RBX, 7 -> RBX rotated by 7
cpu_execute: ADD RAX, RBX -> RAX physically rotated by RBX magnitude (7).
interrupt_spike: SYSCALL detected. Engaging Peripheral Nerve.
STDOUT -> 12
verify: SUCCESS. Bare metal logic executed perfectly in geometric space.
```

Annotation: arithmetic is performed as state transformations in the vector space; output is written via COM1.

---

## Demo 7: Noisy opcode learning (classification + reject)

```text
=== NOISY OPCODE LEARNING DEMO ===
post_sleep_accuracy: 96/96 (100%)

--- UNKNOWN OPCODE DETECTION ---
alien_pulse: max_sim=0.566 margin=0.287 (closest=OP_0)
alien_verdict: CORRECTLY REJECTED as unknown opcode.
```

Annotation: the demo reports forced classification metrics and also a strict mode that can explicitly reject unknown inputs based on similarity + margin gates.
