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
  - `Build\<Configuration>\Client\` — same layout as `GameData\KSPMultiplayer\` (root `KSPMultiplayer.version` for KSP-AVC, plus Plugins, Button, Localization, PartSync, Icons, Flags), ready to copy into KSP
  - `Build\<Configuration>\Server\` — `dotnet publish` output for the standalone server

### Publish a GitHub Release (zips + `gh`)

Prerequisites: 7-Zip, MSBuild/dotnet, [GitHub CLI](https://cli.github.com/) (`gh auth login`).

- Package AppVeyor-matching zips: `.\Scripts\PackageKspmpReleaseZips.cmd` or `pwsh -File .\Scripts\PackageKspmpReleaseZips.ps1` (zips use 7-Zip, env `7Z_EXE`, or Windows `tar` fallback; add `-Configuration Debug` or `-IncludeMasterServer` as needed)
- One command — build, zip, and create a release: `.\Scripts\PublishGitHubRelease.cmd` or `pwsh -File .\Scripts\PublishGitHubRelease.ps1` (do **not** use `cmd /c something.ps1`; arguments will not apply). Tag defaults from [KSPMultiplayer.version](KSPMultiplayer.version) `VERSION`; use `-Draft` then **Publish** on GitHub so `releases/latest` works. Long-form notes: e.g. `-NotesFile ReleaseNotes\FirstRelease_0.32.0.md` (update or duplicate per version).

### One-click full local test stack

- Run `Scripts\BuildAndDeployTestStack.bat Debug`
- Run `Scripts\BuildAndDeployTestStack.bat Release`

## Required Toolchain

- `msbuild` for `LmpClient\LmpClient.csproj`
- `.NET Framework 4.7.2` targeting pack/reference assemblies for the client
- `dotnet` SDK/runtime capable of publishing `Server\Server.csproj` (`net5`)
- Optional for scripted releases: [GitHub CLI](https://cli.github.com/) (`gh`) and 7-Zip for [Scripts\PublishGitHubRelease.ps1](Scripts/PublishGitHubRelease.ps1) / [Scripts\PackageKspmpReleaseZips.ps1](Scripts/PackageKspmpReleaseZips.ps1)
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

### Automated tests (local / CI)

- **Shared + protocol (classic MSBuild / VSTest):** build `LmpCommonTest\LmpCommonTest.csproj` and run the test assembly (e.g. Visual Studio Test Explorer or `vstest.console.exe` on `LmpCommonTest\bin\Debug\LmpCommonTest.dll`).
- **Dedicated server:** `dotnet test ServerTest\ServerTest.csproj -c Debug` (SDK-style `net5.0`, references `Server`). AppVeyor runs the same `dotnet test` in the `test_script` phase.

### KSP manual smoke (after mod or server changes)

Use a throwaway or backup KSP install when possible.

1. Deploy client to `GameData\KSPMultiplayer\` (e.g. `Scripts\BuildClientAndCopy.bat` or copy from `Build\<Config>\Client\`) and ensure `KSPMultiplayer.version` is at the mod root.
2. Start KSP: confirm the mod list shows the plugin as **KSPMP**, disclaimer/options show **KSP Multiplayer** copy, and the toolbar / button assets load.
3. **Save migration (optional):** with only `saves\LunaMultiplayer\` present and no `saves\KSPMultiplayer\`, launch once; confirm the game creates/uses `saves\KSPMultiplayer\` and prior data is preserved (or migrate manually if you disabled auto-migration).
4. Start the **same build** dedicated server (Lidgren app id **KSPMP**), connect from the in-game server browser or direct address, complete a short join (handshake, load into flight or space center as designed).
5. Do **not** expect a match with servers or clients still on the legacy **LMP** wire id; this is a coordinated client + server release.

## Scenario Sync Domain Contract

Every piece of career/science/contract/facility state we sync through PersistentSync is a *scenario domain*. These rules are mandatory for all new scenario-domain work and for any edit to the existing eleven domains. They exist because every "random multiplayer bug" we have hit in the scenario layer reduces to breaking one of them.

### Mandatory rules

- **Must inherit one of the sanctioned templates.** New server-side scenario domains inherit `ScenarioSyncDomainStore<TCanonical>`. Pure projection domains (no independent scenario state, route through an owner domain) inherit `ProjectionSyncDomain<TOwner>`. New client-side scenario domains inherit `ScenarioSyncClientDomain<TCanonical>`. Direct implementations of `IPersistentSyncServerDomain` / `IPersistentSyncClientDomain` are forbidden; the regression gate `AllServerDomainsInheritOneOfTheSanctionedTemplates` enforces this on the server.
- **One scenario, one domain.** No two registered domains may write the same scenario node path. If a single scenario node (e.g. `ResearchAndDevelopment`) needs two facets, they must be owned by one domain with a compound `TCanonical`, or one must be a pure read-only projection via `ProjectionSyncDomain<TOwner>`.
- **Canonical state is typed, not text.** `TCanonical` must be a structural type (record/class of fields, `IReadOnlyDictionary`, ordered list, etc.). Substring rewriting of scenario files is forbidden. `WriteCanonical` rebuilds the `ConfigNode` graph via the LunaConfigNode graph API; when a text-backed cache prevents `RemoveNode`/`AddNode` from flushing through `ToString()`, construct a fresh name-only `ConfigNode` and attach the desired children (see `ContractsPersistentSyncDomainStore.WriteCanonical`).
- **Equality short-circuits revisions.** `AreEquivalent` must be implemented and correct. The base class asserts that equivalent reductions do not bump `Revision` or rewrite the scenario. Unconditional `Revision++` in a domain is forbidden.
- **Authority is declared once and enforced at the registry gate.** `IPersistentSyncServerDomain.AuthorizeIntent` is the authoritative per-intent gate and is declared `abstract` on both sanctioned templates (`ScenarioSyncDomainStore<TCanonical>` and `ProjectionSyncDomain<TOwner>`) so every concrete domain MUST explicitly declare its gate — authority is never silently inherited. Simple policy-based domains override with one line: `public override bool AuthorizeIntent(ClientStructure client, byte[] payload, int numBytes) => AuthorizeByPolicy(client);`. Domains with mixed per-intent authority (e.g. Contracts) decode the payload in the override and dispatch. Hand-rolled `LockQuery` / player-name checks inside `ReduceIntent` are forbidden — the reducer is pure state-transition logic, never a secondary authority gate.
- **Echo suppression uses the scope.** Client apply paths silence peer Share systems by declaring `PeersToSilence` on the client domain; the base class runs `ScenarioSyncApplyScope.Begin(PeersToSilence)` around `ApplyCanonicalToStock`. Hand calls to `StartIgnoringEvents` / `StopIgnoringEvents` outside the scope or an outbound send wrapper are forbidden.
- **Enabled check is centralized.** `Share*MessageSender` gates sends with `PersistentSync.IsLiveForDomain(id)` only. Per-domain predicates (`IsPersistentSyncLiveForContracts`, inline `PersistentSyncSystem.Singleton.Enabled`, missing checks) are forbidden.
- **Intents are typed from day one.** New domains ship a typed intent serializer under `LmpCommon/Message/Data/PersistentSync/` or `LmpCommon/PersistentSync/`. Raw `ConfigNode` byte blobs as intents are forbidden.

### Forbidden patterns (review-reject list)

- Text-based scenario rewriting (`scenarioText.IndexOf("CONTRACTS\n{")`, manual brace counting, regex substitution on save files).
- Two writers on one scenario node path.
- Direct `IPersistentSyncServerDomain` / `IPersistentSyncClientDomain` implementations (must inherit the scenario-owning or projection template).
- Per-domain `ApplyingX` / `SuppressX` flags independent of `ScenarioSyncApplyScope`.
- Legacy `ShareProgress*MsgData` raw-relay fallbacks. The template is the single live path.
- `Revision++` without a preceding `AreEquivalent` guard.
- Inlining lock-ownership / player-name checks inside `ReduceIntent`. Per-intent authority lives in `AuthorizeIntent`, not the reducer.

### Adding a new scenario domain (checklist)

1. Add an entry to `PersistentSyncDomainId`.
2. Define `TCanonical` (typed, structural).
3. Implement the server domain by inheriting `ScenarioSyncDomainStore<TCanonical>` (or `ProjectionSyncDomain<TOwner>` if the domain owns no scenario state) and overriding `LoadCanonical`, `WriteCanonical`, `AreEquivalent`, `ReduceIntent`, `SerializeSnapshot`. `AuthorizeIntent` is abstract and MUST be overridden — use `=> AuthorizeByPolicy(client)` for simple policy-based gates or decode the payload for mixed per-intent authority. For projection domains, remember the revision contract: the projection's snapshot `Revision` is the owner's `Revision` (enforced by `ProjectionRevisionTracksOwnerRevisionAfterMutation`); do NOT maintain an independent counter.
4. Implement the client domain by inheriting `ScenarioSyncClientDomain<TCanonical>` and overriding `DeserializeSnapshot`, `ReadyToApply`, `ApplyCanonicalToStock`, and declaring `PeersToSilence`.
5. Register both sides (`PersistentSyncRegistry.Register` / `PersistentSyncSystem.RegisterClientDomain`).
6. Route all outbound intents through `Share*MessageSender` guarded by `PersistentSync.IsLiveForDomain(id)`.
7. Add a gate test under `ServerPersistentSyncTest` covering equality short-circuit, authority rejection (for non-AnyClientIntent domains; for per-intent-authority domains, cover each dispatch branch), and server-restart persistence.

## Stage Completion Review

At the end of any architectural stage, explicitly review:

- `Implemented as planned`
- `Remaining shortcuts`
- `Remaining leaked details`
- `What would still let this stage be bypassed`

For any change that touches scenario sync, additionally review:

- No new direct `IPersistentSyncServerDomain` / `IPersistentSyncClientDomain` implementations (must inherit `ScenarioSyncDomainStore<>` or `ProjectionSyncDomain<>`; the regression gate `AllServerDomainsInheritOneOfTheSanctionedTemplates` enforces this on the server).
- No new scenario text rewriters; `WriteCanonical` uses the `ConfigNode` graph API.
- Authority lives at the registry gate: `AuthorizeIntent` reflects real enforcement (no hand-rolled lock checks in `ReduceIntent`).
- Every `Share*MessageSender` edited in this stage uses `PersistentSync.IsLiveForDomain(id)`.
- Client apply paths use `ScenarioSyncApplyScope` (in `LmpCommon.PersistentSync`), not raw `StartIgnoringEvents`.

If any remaining shortcut violates the intended ownership/contract, the stage is not complete.
