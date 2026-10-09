# Contributing

Reproduce reports in a clean URP project using the comparison scene and GPU check. Include Unity/URP versions, renderer path, graphics API, GPU, AA/upscaler and camera mode. Distinguish geometry jitter, whole-object movement and temporal reconstruction artifacts.

Adapters live in `Packages/com.voidlogistics.urp-stable-projection/Editor/Patches`. Each file contains exact before/after blocks and normalized SHA-256 hashes. `docs/urp-*.patch` provides a readable diff; `docs/upstream-references.json` pins the official Unity source commits.

To add support, inspect the affected upstream implementation (especially motion history), produce a separate adapter when contracts differ, add immutable source references, run `python Tools/verify_sources.py`, then perform Unity compilation, GPU, install/restore, demo and Player tests on the matching Editor. Do not merely extend the accepted version range.

Use the Unity Companion License for Unity-derived changes and retain copyright/third-party notices. Avoid adding game assets, private project code, logs with personal paths, credentials, Library/Temp directories or packed entire Unity editors to this repository.
