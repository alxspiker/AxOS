# AxOS

<p align="center">
  <em>A bare-metal OS prototype with a biologically-inspired cognitive runtime (Hyperdimensional Computing) layered on top of x86 via Cosmos.</em>
</p>

> AxOS is an experimental research operating system exploring Hyperdimensional Computing (HDC) as a systems-architecture primitive.

---

## Quick links

- **Architecture deep dive:** [ARCHITECTURE.md](ARCHITECTURE.md)
- **Annotated boot logs & demos:** [DEMOS.md](DEMOS.md)
- **License:** [LICENSE](LICENSE)

---

## What is AxOS?

AxOS is a **bootable bare-metal OS prototype** written in **C#** on top of the [Cosmos](https://github.com/CosmosOS/Cosmos) framework (IL2CPU → native x86). There is no underlying Windows/Linux once the kernel boots.

The key research idea: instead of relying exclusively on classical dispatch structures (opcode tables, switch chains, rigid parsers), AxOS models parts of “interpretation” and “recognition” as operations in a **high-dimensional geometric space** (hypervectors / tensors). Inputs can be encoded as vectors; decisions can be made by **similarity** (cosine) rather than only by exact byte equality.

---

## Scope & reality check (read this first)

AxOS is real and bootable, but it is not magic:

- **x86 is still x86.** The CPU remains Von Neumann at the silicon level. AxOS layers a cognitive runtime *on top of* conventional hardware.
- **“Learning opcodes” in AxOS means:** learning symbols/reflexes inside a **toy symbolic ISA / ruleset** used for research demos. It does **not** claim to rewrite arbitrary real x86 machine code semantics at runtime.
- **HardwareSynapse** is a **developer-facing reference framework** for HDC-based signal recognition. The live console path still uses Cosmos primitives (keyboard / serial). Full HDC drivers for complex devices are future work.
- **Unknown input handling:** unknown patterns route to System 2 (costly search), fatigue, or sandboxing inside a manifold. The intent is “don’t crash the kernel just because something is unfamiliar.”

If you want the empirical evidence, go to **[DEMOS.md](DEMOS.md)** (real boot logs + annotations).

---

## What the current build demonstrates

From real QEMU boots (see **DEMOS.md**):

- Tensor correctness & invariants (DEBUG-only checks)
- A bounded energy model (“metabolism”) + sleep/recharge
- Program-level isolation via `ProgramManifold` energy budgets
- Similarity-gated recognition (strict vs tolerant modes)
- Toy byte-stream ingestion mapped through a ruleset
- Reflex promotion / ruleset mutation during sleep in constrained discovery demos
- A geometric calculator demo that prints `5 + 7 = 12` to COM1
- Noisy prototype classification + explicit rejection of an “alien” pulse via similarity/margin gates

---

## Building & running

### Prerequisites

- Visual Studio 2022
- Cosmos DevKit / User Kit
- .NET 6 SDK or later
- QEMU / VMware / Hyper-V

### Build & run (typical workflow)

1. Clone the repo.
2. Open `AxOS.sln` in Visual Studio.
3. Set the `AxOS` project as Startup Project.
4. Press **F5** to build the ISO and launch.

### Optional CLI helpers (if present)

```powershell
.\run.bat serial         # Build + launch with COM1 redirected to console
.\run.bat nobuild serial # Launch last build without recompiling
```

---

## Repo map

* `README.md` → project overview + quick start
* `ARCHITECTURE.md` → subsystem deep dive + mechanics + file reference
* `DEMOS.md` → annotated boot logs / proof-of-architecture

---

## License (AGPL-3.0)

Copyright (c) 2025–2026 alxspiker.

Licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)** — see [LICENSE](LICENSE).

Practical summary (not legal advice):

* You can use, modify, and redistribute under AGPL-3.0 terms.
* If you modify AxOS and run it to provide functionality to users **over a network**, AGPL requires you to offer those users the **Corresponding Source** of your modified version.
* Commercial use is allowed if you comply with the license.

For alternative/commercial licensing (e.g., closed-source use), use [GitHub Issues](https://github.com/alxspiker/AxOS/issues).

---

<p align="center">
  <em>"It didn't crash. It just got exhausted and lost its train of thought."</em>
</p>
