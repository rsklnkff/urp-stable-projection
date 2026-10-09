# Implementation

## Projection

The usual object-to-world-to-clip path can round a small local vertex offset while adding a large object translation. A later camera subtraction cannot recover those discarded bits.

The patched object helper first computes object-to-view rotation/scale, evaluates its translation with compensated FP32 products and sums, adds the small local vertex contribution, and applies the actual current projection. The compensation uses split products and magnitude-ordered FastTwoSum residuals; HLSL `precise` requests preservation of the residual arithmetic. The tested DX11/Vulkan paths preserve it; the DX12 limitation is recorded in the compatibility table. It does not require GPU FP64 or extra instance/mesh buffers.

The public meanings of world-space data and camera matrices are unchanged. This matters for UI and passes that bind custom view/projection matrices. Fragment-stage world projection helpers keep their existing path, avoiding compensated arithmetic in pixel-bound effects.

## Optional camera correction

For a float view matrix `V = [R,t]` and an accurate camera position `C`, compute `d = -R*C - t` in CPU doubles and set `P' = P * Translate(d)`. The remainder is small. The orthographic component shifts near/far values with the depth correction and publishes `P'` through ordinary `Camera.projectionMatrix` and `nonJitteredProjectionMatrix`.

Repeated setup first restores the automatic projection; disabling the component restores it as well. Explicit clip-distance changes made while the component is active are preserved when they differ from the component's last applied values. The component runs late and refreshes before rendering. Another system intentionally controlling that same custom projection needs an ownership agreement.

## Motion vectors

Object motion uses separate current/previous view and projection matrices, preserving small projection corrections that would disappear if rounded into a large VP matrix.

Camera motion uses clip-to-clip reprojection composed in CPU doubles and uploaded as a float matrix. This avoids reconstructing a large absolute world position per pixel. URP 17 already stores the required separated history. URP 14 adds four non-XR matrices per camera (256 bytes), advances them alongside its existing history, and uploads them from its motion pass after the history update.

## Shadows and Shader Graph

Standard, terrain and supported SpeedTree shadow paths apply the existing world-space bias as a small offset without first rounding the complete vertex into a large world position. Shader Graph uses the stable object path where it still owns object-space vertex data. External world-space vertex/VFX modifications retain their upstream path.

## Limits

- Small-world projects may see little visible benefit while paying the extra vertex arithmetic.
- This does not change Transform storage, object placement, physics, simulation, saves or world-space particle simulation.
- World-position shader effects and CPU screen/world queries retain ordinary float limitations.
- Arbitrary raw matrix expressions do not automatically use the new helpers.
- Dynamic/static batching that has already baked small geometry into large world coordinates can lose detail before these helpers run.
- Perspective shader projection is covered by the numerical test; the optional precise-camera component only corrects non-XR orthographic cameras.
- XR, WebGL/GLES, mobile phones, consoles, custom camera-stack features and arbitrary third-party shaders require separate validation. A laptop RTX 3050 is not an Android/iOS test device.

The numerical techniques are established. The contribution here is their integration into URP's shared projection, shadow and motion-vector paths with version-aware installation and reproducible checks.
