# 🏛️ Compile-Time Reference Assemblies (Metadata Only)

This directory contains **metadata-only reference assemblies** generated using `JetBrains.Refasmer`.

## Legal & Compliance Notes
1. **Zero Proprietary Code:** All method bodies, algorithms, and private implementation details have been completely stripped out (`throw null;` / empty IL instructions).
2. **Zero Game Assets:** No textures, audio, dialogues, or map data (`.xnb`) are present.
3. **Compile-Time Only:** These assemblies cannot be executed at runtime. They exist solely to allow the C# compiler in CI/Docker build stages to resolve public method signatures and types without redistributing ConcernedApe's game binaries.
4. **Runtime Requirement:** At runtime, the LiteValley Headless Server strictly requires the user to mount a genuine, legally purchased copy of Stardew Valley 1.6+ to `/data/game`.
