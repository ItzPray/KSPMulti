# AGENTS.md

Repository source of truth for working in `KSPMulti`.

## Purpose

- `LmpClient`: KSP client mod targeting `.NET Framework 4.7.2`.
- `Server`: standalone multiplayer server targeting `net5`.
- `LmpCommon`, `LmpGlobal`, `Lidgren.*`, `uhttpsharp`: shared libraries used by client/server.
- `Scripts`: canonical local build and deploy entrypoints.

## First Steps

1. Read this file before making changes.
2. Prefer root-cause fixes and minimal diffs.
3. Preserve backward compatibility unless explicitly told otherwise.
4. For non-trivial work, write a short plan first with:
   - `Owner`
   - `Contract`
   - `Must Remove`
   - `No New Shortcuts`
   - `Acceptance Criteria`
   - `Validation`

## Local Environment Contract

- Machine-specific paths belong only in `Scripts/SetDirectories.bat`.
- `Scripts/SetDirectories.bat` is intended to stay local and may be marked `skip-worktree`.
- Do not commit personal install paths.
- Treat bootstrap path metadata as configuration only, never as runtime behavior switches in code.

## Canonical Commands

### Verify setup

- Run `Scripts\VerifyEnvironment.bat`

### Build and deploy client mod

- Run `Scripts\BuildClientAndCopy.bat Debug`
- Run `Scripts\BuildClientAndCopy.bat Release`

### Publish and deploy test server

- Run `Scripts\PublishServerToTest.bat Debug`
- Run `Scripts\PublishServerToTest.bat Release`

### Build client + server into repo `Build\` staging (no KSP / test-server deploy)

- Run `Scripts\BuildOnly.bat Debug` or `Scripts\BuildOnly.bat Release`
- Output layout:
  - `Build\<Configuration>\Client\` — same layout as `GameData\LunaMultiplayer\` (root `LunaMultiplayer.version` for KSP-AVC, plus Plugins, Button, Localization, PartSync, Icons, Flags), ready to copy into KSP
  - `Build\<Configuration>\Server\` — `dotnet publish` output for the standalone server

### One-click full local test stack

- Run `Scripts\BuildAndDeployTestStack.bat Debug`
- Run `Scripts\BuildAndDeployTestStack.bat Release`

## Required Toolchain

- `msbuild` for `LmpClient\LmpClient.csproj`
- `.NET Framework 4.7.2` targeting pack/reference assemblies for the client
- `dotnet` SDK/runtime capable of publishing `Server\Server.csproj` (`net5`)
- KSP/Unity DLLs in `External\KSPLibraries`
- Harmony dependency in `External\Dependencies\Harmony\000_Harmony\0Harmony.dll`

If `msbuild` or `dotnet` are not on `PATH`, set these in `Scripts/SetDirectories.bat`:

```bat
SET MSBUILD_EXE=C:\Path\To\MSBuild.exe
SET DOTNET_EXE=C:\Path\To\dotnet.exe
```

## Setup Files

### `Scripts\SetDirectories.bat`

Supported variables:

- `KSPPATH`: primary KSP install for custom client deployment
- `KSPPATH2`: optional second KSP install to mirror client deployment
- `LMPSERVERPATH`: test server deployment folder
- `COPYHARMONY`: set to `true` only when intentionally overwriting `GameData\000_Harmony`
- `MSBUILD_EXE`: optional explicit path to `MSBuild.exe`
- `DOTNET_EXE`: optional explicit path to `dotnet.exe`

## Engineering Rules

- Keep behavior deterministic and reproducible.
- Centralize shared logic instead of duplicating it.
- If contracts or schemas change, update all affected layers.
- Add or update focused tests when behavior changes.
- Avoid unrelated refactors unless the current architecture is blocking correct fixes.

## Shortcut Prevention

- Do not branch runtime behavior on provenance flags such as `rootPage`, `mainPage`, `original tab`, or equivalent bootstrap metadata.
- Prefer explicit capability/state objects over inferred role checks.
- If repeated fixes in the same subsystem require more special cases, stop patching and refactor the owning contract first.

## Debugging Workflow

Use this sequence:

1. Reproduce
2. Isolate
3. Identify exact root cause
4. Apply the smallest correct fix
5. Validate with the same harness or reproduction path

For bug work, explicitly report:

- observed symptom
- diagnostic added or used
- reproduction method
- exact root cause
- failing test or harness
- fix applied
- validation results
- residual risks or follow-ups

Remove temporary diagnostics before finishing unless asked to keep them.

## Validation Expectations

- Prefer focused tests in `LmpCommonTest` or `ServerTest` when applicable.
- For client/server integration changes, validate with the relevant script under `Scripts`.
- For environment/setup work, validate with `Scripts\VerifyEnvironment.bat`.

## Stage Completion Review

At the end of any architectural stage, explicitly review:

- `Implemented as planned`
- `Remaining shortcuts`
- `Remaining leaked details`
- `What would still let this stage be bypassed`

If any remaining shortcut violates the intended ownership/contract, the stage is not complete.
