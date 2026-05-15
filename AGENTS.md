# Repository Guidelines

## Project Structure & Module Organization
- `Code/` holds runtime logic: `Abstract/` contracts (e.g., `IManager`), `Features/` game systems and world scripts, `UI/` windows/prefabs/grids, `Utils/` helpers, `Libraries/` integration glue, `Patch/` Harmony patches, and `Effect/` visuals.
- `GameResources/` and `Locales/` carry data and translations bundled with the mod; `ABPackages/` stores asset bundles loaded at start; `Assemblies/` keeps third-party DLLs not shipped with the game install.
- `mod.json` and `icon.png` define the mod manifest; `Properties/AssemblyInfo.cs` holds assembly metadata; `bin/` contains build outputs; zipped archives (e.g., `GodTools-v1.1.0.5.zip`) are release drops.

## Build, Test, and Development Commands
- `dotnet restore GodTools.sln` — fetch NuGet dependencies (Newtonsoft.Json) before first build.
- `dotnet build GodTools.sln -c Release` — produce `bin/Release/net48/GodTools.dll` (targets .NET Framework 4.8). The project resolves Unity/WorldBox references two directories up (`../../worldbox_Data/...`), so keep the mod inside the game folder or update hint paths.
- `dotnet build GodTools.sln -c Debug` — enables unsafe blocks for debugging. Attach your debugger to WorldBox after hot-loading the built DLL.
- After building, ensure the DLL, `GameResources/`, `Locales/`, and `ABPackages/` remain beside `mod.json` for NeoModLoader to load.

## Coding Style and Naming Conventions
- C# 12, net48, implicit usings and nullable disabled—add explicit `using` directives and null checks.
- 4-space indentation; braces on the same line (K&R). Prefer PascalCase for types and public members; mirror existing snake_case/static field patterns for Unity objects and settings in this codebase.
- Keep feature managers implementing `IManager` and allow `Main.OnModLoad` to sort/initialize them. Place Harmony hooks in `Code/Patch`; general helpers belong in `Code/Utils`.

## Testing Guidelines
- No automated tests are present. Validate changes in-game: confirm asset bundles from `ABPackages/` load, UI windows render correctly (e.g., editors and grids), and localized strings resolve after edits.
- For data tweaks, cross-check keys in `Locales/` and resources in `GameResources/`; watch for serialization changes affecting save data.

## Commit and Pull Request Guidelines
- History mixes concise Chinese messages and occasional conventional prefixes (e.g., `feat(ui): ...`). Keep commits short (<72 chars) and scoped; using `feat/fix/chore` with an optional scope is appreciated.
- PRs should summarize the change, list touched areas (`Features/...`, `UI/...`, assets), note manual test steps/results, and include screenshots/GIFs for UI updates.
- When shipping a release, bump `version` in `mod.json`, refresh any zipped distribution, and document dependency or resource changes (see `OptionalDependencies` for upstream mod expectations).
