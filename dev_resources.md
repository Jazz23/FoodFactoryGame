# Development Resources

Last reviewed: 2026-09-21. This file records resource facts, configuration, and open setup work; it does not certify a working build. Gameplay requirements belong in [Food_Factory_Restaurant_GDD.md](Food_Factory_Restaurant_GDD.md).

## Priorities

1. Minimize subscription consumption per accepted feature, including review and rework.
2. Preserve code quality through clear contracts, capable task ownership, and relevant verification.
3. Reduce elapsed time where it does not undermine the first two goals.

## Environment and Dependencies

Repository declarations below were inspected; successful installation and runtime integration require baseline verification.

| Resource | Recorded state / source |
|----------|-------------------------|
| Workspace | `E:\Projects\Unity\FoodFactoryGame`; Git repository on Windows |
| Unity Editor | `6000.5.9f1` (`b57deb96f08d`), declared in `ProjectSettings/ProjectVersion.txt` |
| Render pipeline | URP `17.5.0`, declared in `Packages/manifest.json` |
| Input System | `1.20.0`; project includes `Assets/InputSystem_Actions.inputactions` |
| AI Navigation | `2.0.14`, declared dependency |
| Test Framework | `1.7.0`, declared dependency |
| uGUI | `2.5.0`, declared dependency; final UI approach TBD |
| SQLite | `com.gilzoide.sqlite-net`, Git reference `1.3.2`; intended persistence role TBD |
| MoonSharp | Git reference `upm/beta/v3.0`; scripting/modding requirement TBD |
| FishNet | Vendored `Assets/FishNet` version `4.7.3`, including metadata, license, and third-party notices; root ignore removed. The snapshot contains 2,014 FishNet files. Original upstream commit/channel is unknown; do not claim byte identity beyond this repository snapshot |
| FishNet configuration | `Assets/FishNet.Config.XML` is retained as project authoring/configuration and its `.meta` is present |
| Package resolution | `Packages/packages-lock.json` is tracked; an isolated source snapshot resolved packages and passed the baseline EditMode suite |
| Unity automation | Unity CLI MCP connected to Editor PID `5068`, Unity `6000.5.9f1`, project path `E:\Projects\Unity\FoodFactoryGame`; registered commands were discovered and exercised |
| OpenCode | CLI is available; the three configured model IDs and their selected variants were found through `opencode models openai --verbose` |

Other declared dependencies remain in `Packages/manifest.json`; declaration alone is not a decision to use a package for gameplay.

## Hardware, Assets, and Budget - To Fill In

| Item | Status |
|------|--------|
| Development CPU / GPU / RAM | TBD |
| Target PC CPU / GPU / RAM | TBD; owner requested mid-range PC |
| Target resolution / quality settings | TBD |
| Subscriptions, quotas, reset windows, and provider routing | TBD; do not infer subscription consumption from model catalog API prices |
| Owned art, animation, audio, UI, and tooling assets | Inventory and usage/license constraints TBD |
| Hosting/service budget | TBD after multiplayer hosting decision |

## Agent Configuration and Usage Policy

These are initial settings, not a measured cost ranking. Model IDs are retained from the existing agent definitions. Restart OpenCode after changing agent configuration.

| Agent | Model | Default variant | Ownership |
|-------|-------|-----------------|-----------|
| Coordinator | `openai/gpt-5.6-luna` | `medium` | Scope, risk-based routing, communication, acceptance |
| Investigator | `openai/gpt-5.6-luna` | `medium` | Focused read-only discovery and diagnostic evidence |
| Worker | `openai/gpt-5.6-luna` | `high` | Bounded implementation using established contracts |
| Mid-level developer | `openai/gpt-5.6-terra` | `high` | Ordinary feature ownership from investigation through verification |
| Senior developer | `openai/gpt-6-astra` | `medium` | Foundational contracts, high-risk implementation, difficult diagnosis, independent review |

- Route directly to the appropriate owner; a task does not need to visit every agent.
- Use senior judgment before consequential cross-system implementation, rather than only after repeated failures.
- Keep known-file lookups with the current owner. Use an investigator when focused discovery meaningfully reduces duplicated exploration.
- Reuse a related agent session when its context remains useful; pass concise contracts and results instead of entire transcripts.
- Default to one implementation owner. Parallelism is optional and subject to the ownership and Editor rules in `AGENTS.md`.
- Collect a small representative sample (about ten tasks) before retuning models or reasoning effort.

Configuration verification (2026-09-21): `opencode debug agent <name>` successfully resolved all five project agents with the defaults above. Resolved permissions allow coordinator questions and restrict investigator to read-only discovery/web fetching (edits, shell execution, delegation, and unlisted tools denied). This validates configuration loading, not model output quality or subscription savings.

Suggested measurement record per task: task/risk, agent/model/variant, observable usage or quota change, handoff count, first-pass acceptance, rework, elapsed time, and verification artifacts. Mark unavailable usage as unknown; do not invent token costs or subscription savings.

## Scale and Open Product Decisions

The authoritative targets and statuses are in GDD section 28: multiplayer; approximately 20 active workers, 1,000 customers, thousands of goods, 100 vehicles, and 20 sites; 60 FPS on a mid-range PC; distant operations continue while the world runs.

World-wide population totals are a planning assumption awaiting confirmation. Player count, hosting/disconnect behavior, goods representation, precise benchmark goods count, and target hardware remain open. Physical-goods batching is a proposal, not an approved requirement. The accepted technical starting architecture for authority, simulation timing, visibility, and persistence is [decision 0002](docs/decisions/0002-authoritative-multiplayer-foundation.md); it is not yet implemented.

## Baseline and Tooling Backlog

These items are pending, not verified workflows:

- Verify current Editor compilation, console baseline, and registered automation commands. Old `factory_*` commands are not established in this version; the generic Pipeline commands are documented in `docs/development.md`.
- Document FishNet acquisition/version and prove dependency restoration from a clean source snapshot. The local snapshot check passed; an empty-machine/global-cache check remains pending.
- Maintain `docs/architecture.md` for accepted contracts and implemented/planned status, `docs/development.md` for tested setup/compile/test/build procedures, and `docs/decisions/` for consequential technical decisions.
- Establish exact test commands, filters, isolated save locations, run IDs, and artifact paths. The current baseline now has four passing EditMode checks; gameplay/stateful tests do not exist yet.
- Produce and launch a real player build; the baseline Windows Development Player succeeded and launched. Future build regressions must retain the BuildReport and player log.
- Implement and verify the authoritative multiplayer foundation in decision 0002, then add the documented server/client smoke verification.
- Define repeatable server/client performance scenarios and record measured results on specified hardware.

Upgrade policy: propose Editor/package changes explicitly, keep declared versions and lock files consistent, and verify compile/tests/build before adopting an upgrade. Review mutable Git references during reproducibility work; no dependencies were upgraded as part of this documentation setup.
