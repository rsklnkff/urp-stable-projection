# Installation, source control and recovery

## Package Manager Git installation

Use **Add package from Git URL** with:

```text
https://github.com/rsklnkff/urp-stable-projection.git?path=/Packages/com.voidlogistics.urp-stable-projection#v0.1.0
```

The URL pins the `v0.1.0` release for reproducible builds. Git must be installed and available to Unity. Let Package Manager finish before opening the installer.

This package expects URP to already be installed. It deliberately does not declare a URP dependency that would force all users onto one version. Core and URP must have matching versions. Embedded Core/URP overrides and already-patched precision forks are refused; preserve those edits and restore their original sources before proceeding.

## Package Manager disk installation

Download `urp-stable-projection-0.1.0.zip` from the [GitHub release](https://github.com/rsklnkff/urp-stable-projection/releases/tag/v0.1.0), extract it outside your Unity project, then choose **Add package from disk** in Package Manager. Select `Packages/com.voidlogistics.urp-stable-projection/package.json` inside the extracted folder and follow the installer steps in the README.

Keep that folder available while the dependency is installed. For shared projects and CI, use the pinned Git URL or vendor the helper at a stable relative path; an absolute Downloads-directory dependency is not portable.

## What installation does

1. Read registered Core/URP paths and versions and select the matching adapter.
2. Validate every replacement block for an exact, unique match, in memory.
3. Record original direct package references and whether Core was only transitive.
4. Copy both packages to a unique `Temp/StableProjection` staging directory and modify those copies.
5. Use `UnityEditor.PackageManager.Client.Pack` to produce archives.
6. Add both archives together using `Client.AddAndRemove` and verify the resulting registration.

No edits are made to original PackageCache files. Unrelated package contents, GUIDs, dependencies, versions and notices are preserved. The only persistent outputs are compressed archives, the Package Manager changes and `ProjectSettings/StableProjection.install.json`.

## Uninstall

Choose **Restore original packages** while the helper is still installed. Wait for package resolution and compilation. You may then remove the helper through Package Manager. Unused archives can be removed from source control after verifying that nothing references them. Keep the restore record until you have confirmed restoration.

If package references have been changed by somebody else since installation, automatic restoration refuses to overwrite them. Read the original references in `ProjectSettings/StableProjection.install.json` and restore deliberately through Package Manager.

If compilation prevents the installer opening, use your version-control checkpoint to restore the previous manifest/lock together. The restore record also documents the original URP version and whether Core had a direct dependency. Do not delete unrelated package files or reset the entire project.

If the Editor is closed during copying/packing, the next load marks preparation as interrupted and permits a retry; active references were not changed. If interrupted during package resolution, let Package Manager finish, then validate or restore. The record survives domain reloads and Editor restarts.

## Upgrades

Restore first, upgrade Unity/URP normally, reopen the installer and run compatibility checking. A checked source-block match handles benign patch-release differences, but it cannot establish correctness for arbitrary modifications outside the patched blocks. The installer reports those differences; perform compiler/GPU and gameplay tests before shipping.

Unity can change package contents without changing their visible package version. The source checks are intentional. Unsupported versions should fail rather than receive a best-effort text replacement.
