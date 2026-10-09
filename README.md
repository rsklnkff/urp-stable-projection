# Stable Projection for Unity URP

[![Source compatibility](https://github.com/rsklnkff/urp-stable-projection/actions/workflows/source-validation.yml/badge.svg)](https://github.com/rsklnkff/urp-stable-projection/actions/workflows/source-validation.yml)

Reduce vertex jitter at large world coordinates while keeping Unity's ordinary world-space and camera-matrix conventions.

This package patches shared Core/URP projection helpers and motion-vector paths. It includes a version-aware installer, restoration of the original packages, an optional precise orthographic camera, a comparison scene generator, and reproducible GPU checks. It originated in **Void Logistics**, where visible mesh distortion far from the origin was the problem being solved.

**Status:** initial public release, 0.1.0. DX11 and Vulkan have passing numerical checks. **DX12 is currently outside the supported backend scope:** a tested default compiler/device combination fails the precision tolerance. Use the [compatibility and validation table](docs/COMPATIBILITY.md) to distinguish runtime-tested configurations from source-checked adapters.

## What it improves

- Local mesh detail survives projection more accurately when objects and cameras are far from the origin.
- Standard URP shaders and shaders using the patched shared helpers benefit without per-material changes.
- Matching object and camera motion-vector paths preserve small camera corrections through temporal rendering.
- The optional orthographic camera component accumulates a local camera rig in doubles, or accepts your game's authoritative double position.
- Ordinary world positions, camera matrices, UI conventions and physics coordinates keep their meanings.

It does not repair imprecise physics, simulation, world-space particle data, CPU transforms, or vertex detail already lost before projection. It adds GPU arithmetic; it is not a universal performance improvement or an infinite-world coordinate system.

## Install

Start with a project that already uses a supported URP version and save/commit your work.

In Unity's Package Manager, choose **Add package from Git URL** and paste:

```text
https://github.com/rsklnkff/urp-stable-projection.git?path=/Packages/com.voidlogistics.urp-stable-projection#v0.1.0
```

1. Open **Tools > Stable Projection > Installer** and click **Check compatibility**.
2. Click **Install checked patch**, then let package resolution and shader imports finish. Unity may show its normal missing-signature notice for locally modified packages; review it normally.
3. Click **Validate installed patch**, then run **Tools > Stable Projection > Run GPU precision validation** in Edit Mode.

A [ZIP download](https://github.com/rsklnkff/urp-stable-projection/releases/tag/v0.1.0) and [disk-install instructions](docs/INSTALLATION.md#package-manager-disk-installation) are also available.

The installer creates compressed Core/URP archives from the versions already installed in your project. It uses Package Manager APIs and does not upgrade Unity, hand-edit manifests, overwrite PackageCache, edit your scenes or add loose render-pipeline source directories to your project.

For a shared/CI project, install this helper package from its public Git URL or keep a copy at a stable relative path. A local absolute dependency on your Downloads directory is not portable. See [installation and upgrades](docs/INSTALLATION.md).

## Try the demo

After installing the patch, choose **Tools > Stable Projection > Create comparison demo scene**. The generator asks about unsaved scenes and creates a new folder under `Assets/StableProjectionDemo`; it never overwrites an existing demo.

Press Play. The left grid uses conventional world-space vertex projection; the right grid uses the installed URP helpers. Both have identical local geometry and use the same camera. Toggle camera movement, origin distance and perspective, and adjust zoom. The default coordinate magnitude is 240,000 units. Temporal AA is disabled for the comparison camera so it does not conceal geometry jitter.

![Conventional orange grid and stable cyan grid](docs/media/comparison.gif)

Orange: conventional projection. Cyan: patched shared helpers. These are unretouched 1280x720 renders from the generated demo on DX11, with camera movement sampled at 12 fps and replayed forward/backward. Raster aliasing remains; the comparison concerns geometric distortion.

## Optional precise orthographic camera

Add `PreciseOrthographicCamera` to your orthographic camera. It owns that camera's automatic projection and restores it when disabled. Do not combine it with another component that owns a custom projection matrix.

With a parented camera rig, the component accumulates local transforms in doubles. If your game already has a double-precision camera position, supply it each frame after moving the camera:

```csharp
preciseCamera.SetPrecisePosition(cameraX, cameraY, cameraZ);
```

This must be the final **camera** position, including rig offsets. Supplying a target/ship position instead will move the projection incorrectly. The precision of object placement remains your game's responsibility. Perspective cameras receive the shared shader improvements but this optional component does not modify their projection. XR is outside the validated support scope.

## Version compatibility

Adapters exist for **URP 14.0 and 17.0, 17.1, 17.2, 17.3**, covering Unity 2022.3 and Unity 6 releases. Each adapter checks exact source blocks before any active package changes. Different patch releases are accepted only if all required blocks match; unexpected source changes are refused with the affected filename. A matching version number alone is never enough.

Reference source versions: 14.0.12, 17.0.4, 17.1.0, 17.2.0 and 17.3.0. Validation depth is documented per version, graphics API and device in [COMPATIBILITY.md](docs/COMPATIBILITY.md). URP 12/15/16, future 17.x minors, HDRP and the Built-in pipeline are not supported by this release.

## Restore and upgrade

Use **Restore original packages** before upgrading URP, removing this helper package, or installing another render-pipeline fork. It restores the original direct/transitive dependency arrangement through Package Manager. Generated archives are retained so restoration never deletes user data.

Commit these files together after installation:

- `Packages/manifest.json` and `Packages/packages-lock.json`
- `Packages/StableProjectionArchives/*.tgz`
- `ProjectSettings/StableProjection.install.json`

The restore record is portable project data; do not ignore it. After a URP upgrade, check compatibility again and generate new archives. Never apply an old archive over a newer Unity release just because its package version string is the same.

## Evidence and performance

The originating game developer reports no noticeable performance degradation in Player testing on their devices, including Steam Deck and a laptop RTX 3050. This is a qualitative user report, not a measured zero-overhead guarantee or a platform certification for every adapter.

The included GPU check measures projection error against a double-precision reference, including ordinary custom/UI/shadow views and camera motion. Numerical accuracy tests are distinct from gameplay regression testing. Compare warmed Player GPU frame times on your own hardware, using identical scenes/settings and complete install/restore for an A/B benchmark.

See [implementation](docs/IMPLEMENTATION.md), [validation](docs/VALIDATION.md), and [contributing](CONTRIBUTING.md).

## License

Unity-derived changes and this accompanying package are distributed under the [Unity Companion License](LICENSE.md) for Unity-dependent projects. Generated Core/URP packages retain their existing copyright, license and third-party notices. This is an independent community modification, not an official Unity package.
