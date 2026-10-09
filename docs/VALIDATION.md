# Reproducing validation

## Inside Unity

Use a disposable project with the exact Editor/URP version you intend to support. Install the helper and precision patch, then use:

- **Tools > Stable Projection > Run installer preflight tests**: exact/ambiguous/missing blocks, preflight source preservation, already-patched inputs, path containment and unsupported versions.
- **Run GPU precision validation**: 1,536 projection samples plus 128 samples through the installed URP camera-motion shader; camera rig precision, projection restoration/repeated setup and motion-history advancement/reset.
- **Compile representative URP shaders**: standard Lit/SimpleLit/Unlit/BakedLit, particles, terrain, SpeedTree and camera motion, with available instancing/DOTS/alpha/shadow variants.
- **Create comparison demo scene**: visually inspect the generated scene in Play Mode and a Player build.

Reports are written under `ValidationResults` in that disposable project. The shader check runs one compilation per Editor update; leave it running until its completion report appears. Do not start a package operation or enter Play Mode during it.

The GPU check requires a real graphics device. It deliberately fails when invoked with `-nographics`. Numerical tolerances are in pixels at a 1,248-pixel reference height. Very small reported errors are numerical comparisons, not claims about final display sharpness or all possible coordinates.

## Batch runs

With the helper already installed in a disposable URP project, invoke the matching Unity Editor executable using separate processes:

```text
Unity -batchmode -projectPath <project> -executeMethod StableProjection.Editor.BatchValidation.Install -logFile <install.log>
Unity -batchmode -projectPath <project> -executeMethod StableProjection.Editor.BatchValidation.Validate -logFile <validate.log>
Unity -batchmode -projectPath <project> -executeMethod StableProjection.Editor.ShaderValidation.Run -logFile <shaders.log>
Unity -batchmode -projectPath <project> -executeMethod StableProjection.Editor.BatchValidation.Restore -logFile <restore.log>
```

Do **not** add `-quit`: these entry points wait for asynchronous work and exit the Editor themselves. Do not run them against a project already open in another Editor. Select `-force-d3d11`, `-force-d3d12` or `-force-vulkan` when testing those backends; inspect the recorded actual graphics API.

A successful process exits 0. Installation and GPU results are JSON; shader results are a text report. Batch installation and restoration persist their state across domain reloads. Verify restoration removes the helper-generated direct Core reference when Core was originally transitive.

## Demo capture and Player smoke check

The batch entry point `StableProjection.Editor.DemoValidation.CreateCaptureAndBuild` creates the comparison scene, captures 48 offscreen frames and builds a Windows Development Player. Run it only in a disposable project; it saves the generated scene. A Windows build-support module is required.

Launch that Player with `-stableProjectionSmoke <output-directory> -force-d3d11 -batchmode`. The opt-in smoke runner requests offscreen renders explicitly, counts 120 rendered camera frames, checks that both colored grids are present, records runtime errors, writes a PNG/JSON report and exits. It is disabled when this command-line flag is absent. This verifies startup and rendered content, not long-session stability or performance.

## Source checks without Unity

```text
python Tools/verify_sources.py
```

Python 3.10+ and the standard library are sufficient. This downloads only the affected files from immutable official Unity Graphics commits, verifies reference and resulting SHA-256 hashes, tests CRLF normalization and rejects double application. The repository's GitHub workflow runs this same command without Unity credentials. It does not claim to compile or render Unity projects.

## Performance

Use the same warmed Player workload, graphics settings, camera path and frame limiter state before/after. Restore/install the complete patch for the comparison. Record GPU frame time, CPU render time, frame-time distribution, resolution, visible vertices, shadow workload and renderer path. An editor viewport or a visually unchanged FPS counter is not a controlled overhead measurement.
