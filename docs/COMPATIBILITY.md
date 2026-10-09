# Compatibility and test evidence

Checked on 2026-10-09. All entries are non-XR. Source-block compatibility is not equivalent to a complete runtime certification.

| Unity release | URP reference | Adapter | Validation performed |
|---|---|---|---|
| 2022.3 LTS | 14.0.12 | `urp-14` | Fresh 2022.3.62f3 project: installer, C# compilation, GPU precision, camera lifecycle/history and camera-motion shader passed on DX11; 634 shader stage/variant checks and restore |
| 6000.0 | 17.0.4 | `urp-17.0` | Pinned upstream source and patch invariants verified; matching-Editor runtime validation not performed |
| 6000.1 | 17.1.0 | `urp-17.1` | Pinned upstream source and patch invariants verified; matching-Editor runtime validation not performed |
| 6000.2 | 17.2.0 | `urp-17.2` | Fresh 6000.2.10f1 project: installer, C# compilation, GPU precision, camera lifecycle/history and camera-motion shader passed on DX11 and Vulkan; 706 D3D shader stage/variant checks |
| 6000.3 LTS | 17.3.0 | `urp-17.3` | Fresh 6000.3.11f1 project: installer, C# compilation, GPU precision, lifecycle/history, 706 shader stage/variant checks, restore/reinstall; Windows DX11 Development Player built and rendered 120 smoke-test frames with zero errors |

Other patch versions within these families must pass all source checks. Unexpected source blocks, mismatched Core/URP versions, embedded overrides and already-patched forks are refused. URP 12/15/16 and future 17.x minor versions have no adapter in this release.

## Devices and graphics APIs

Automated standalone GPU checks used a Windows NVIDIA GeForce RTX 5080 on Direct3D 11 (URP 14, 17.2 and 17.3) and Vulkan (URP 17.2). The originating game developer separately reports successful Player testing with no noticeable performance degradation on their devices, including Steam Deck and a laptop RTX 3050. Those reports apply to the originating URP 17.3 implementation, not every public adapter. Their exact graphics APIs, frame-time deltas and benchmark settings were not supplied.

**Known DX12 limitation:** the default compiler path on Unity 6000.2.10f1/6000.3.11f1 with the RTX 5080 did not preserve all compensated arithmetic residuals in the numerical probe. The final adapter's Unity 6.2 run failed a shadow-view sample at 0.0244 reference pixels against a 0.002-pixel tolerance. This is not proof of visible artifacts in every game, but DX12 is not claimed supported. A private DXC probe corrected the initially isolated case; that alone does not certify all shaders or justify silently changing a project's compiler settings. The installer does not change graphics APIs or compiler settings.

Steam Deck is a desktop/Linux-class device; a laptop GPU is not a phone GPU. Android/iOS, GLES/WebGL, Metal, consoles and XR have not been validated. Do not infer support from their absence of compiler errors on a different backend.

## Rendering coverage

The shared object projection applies to orthographic and perspective draws. The optional precise-camera correction applies only to non-XR orthographic cameras. Numerical tests cover ordinary supplied camera, UI/custom and shadow views, object motion and camera-only motion.

Standard URP/Shader Graph helper paths are patched. Custom raw matrix math, VFX/world-space vertex modifications, pretransformed/batched geometry and CPU world-space calculations can retain their existing precision limits. Render Graph and compatibility-mode integration differ by version; shader compilation alone does not validate every render feature, camera stack or temporal upscaler.

The practical regression checklist includes main/depth/depth-normal/shadow/motion passes, camera stacking, UI clipping, transparent effects, decals, particles/trails, dynamic/static batching, skinned meshes, instancing, DOTS, outlines and temporal upscaling. Test the combinations your game actually uses.

## Recorded reports

- [URP 14 DX11](validation/urp14-d3d11.json), [shader compilation](validation/urp14-d3d11-shaders.txt)
- [URP 17.2 DX11](validation/urp17.2-d3d11.json), [Vulkan](validation/urp17.2-vulkan.json), [shader compilation](validation/urp17.2-d3d11-shaders.txt)
- [URP 17.3 DX11](validation/urp17.3-d3d11.json), [shader compilation](validation/urp17.3-d3d11-shaders.txt), [Player smoke check](validation/urp17.3-player-d3d11.json)
- [Known DX12 failure](validation/urp17.2-d3d12.json), [source checks](validation/source-validation.json)
