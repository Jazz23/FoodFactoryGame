# 0001 - Preserve Existing Dependencies for a Reproducible Baseline

Date: 2026-09-21

Status: accepted for development setup. Verification evidence is recorded in [development.md](../development.md).

## Context

The project declared Unity and UPM versions but ignored its entire local FishNet installation. Tracked `DefaultPrefabObjects.asset` references vendor demo assets, so a checkout missing FishNet cannot restore the current authoring. The local package identifies itself as `com.firstgeargames.fishnet` version `4.7.3`.

## Decision

- Include the existing `Assets/FishNet` installation in source control, preserving paths, GUIDs, binaries, license, and third-party notices. Remove the root ignore rule for that directory.
- Use the repository's vendor snapshot as the installation source. No second FishNet UPM dependency is added, avoiding duplicate assemblies or a simultaneous dependency migration.
- Retain the existing Editor and UPM versions and tracked package lock. Git dependencies remain at their existing references, with resolved revisions recorded by the lock file.
- Keep generated test artifacts outside source control, including `Assets/TestResults.meta`.
- Add narrow EditMode checks for imported FishNet availability, network prefab references, starter input authoring, and enabled starter scene integrity.
- Use existing Pipeline commands for compilation, tests, and player builds instead of recreating a project-specific command suite.

## Consequences

- The initial vendor addition is approximately 2,000 files including metadata. Future vendor upgrades should be isolated changes with version/provenance notes and the same baseline checks.
- The upstream project is https://github.com/FirstGearGames/FishNet. The exact original download channel/commit of this existing installation is unknown; version metadata alone does not prove byte identity with an upstream release. Preserve this snapshot rather than claiming such identity.
- License terms are retained in `Assets/FishNet/LICENSE.txt` and `THIRD PARTY NOTICE.md`.
- A UPM migration can be considered later, after verifying source/edition compatibility and reference preservation. It is not required for this baseline.
- SQLite and MoonSharp remain available but do not establish a persistence or modding requirement.
- Clean-source verification must exclude `Library`, generated projects, user settings, and previous test/build artifacts. A local clean-source export checks missing repository inputs; it is not a new-machine, empty-global-cache test.
