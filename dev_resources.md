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
| MoonSharp | Git reference `upm/beta/v3.0`; runs PROTOTYPE employee Lua scripts (`EmployeeScript`, soft sandbox); wider scripting/modding requirement TBD |
| FishNet | Vendored `Assets/FishNet` version `4.7.3`, including metadata, license, and third-party notices; root ignore removed. The snapshot contains 2,014 FishNet files. Original upstream commit/channel is unknown; do not claim byte identity beyond this repository snapshot |
| FishNet configuration | `Assets/FishNet.Config.XML` is retained as project authoring/configuration and its `.meta` is present |
| Package resolution | `Packages/packages-lock.json` is tracked; an isolated source snapshot resolved packages and passed the baseline EditMode suite |
| Unity automation | Unity CLI MCP connected to Editor PID `5068`, Unity `6000.5.9f1`, project path `E:\Projects\Unity\FoodFactoryGame`; registered commands were discovered and exercised |
| OpenCode | A 2026-09-21 CLI check found the then-configured models and variants. The CLI is not on PATH in the current Codex shell (2026-09-22), so the new agent files have not been live-loaded here |

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

Codex and OpenCode use separate agent configurations. This section records each runtime's roster; shared project rules remain in `AGENTS.md`. Do not treat a role, model, or delegation rule from one runtime as the other's default. These settings are not a measured cost ranking.

### Codex

Project-local `.codex/config.toml` sets the default for new trusted-project sessions; `.codex/agents/*.toml` defines project subagents. Existing sessions may retain their selected model. The main agent may delegate an explicit user-requested scope to the coordinator subagent, which can spawn Codex specialists and returns its result to the main agent.

| Agent | Model | Reasoning effort | Ownership |
|-------|-------|-----------------|-----------|
| Main Codex session | `gpt-6-luna` | `medium` | Scope, risk-based routing, communication, acceptance, and local work |
| Coordinator subagent | `gpt-6-luna` | `high` | Orchestrate explicitly delegated work and spawn project specialists |
| Explorer | `gpt-6-luna` | `medium` | Focused read-only discovery and diagnostic evidence |
| Worker | `gpt-6-luna` | `high` | Ordinary feature ownership from investigation through verification |
| Senior developer | `gpt-6-sol` | `high` | Foundational contracts, high-risk implementation, difficult diagnosis, independent review |

- Codex `worker` owns ordinary features end to end; there is no Codex mid-level developer. Prefer a senior decision followed by worker implementation when the contract is stable and bounded; keep design-coupled critical code with the senior owner.

### OpenCode

The user-facing primary `coordinator` in `.opencode/agents/coordinator.md` delegates directly to OpenCode specialists. Its read-only discovery role remains named `investigator`; its `worker` owns ordinary features end to end. There is no OpenCode mid-level developer. Unlike Codex, the coordinator is the primary agent rather than a subagent.

| Agent | Model | Variant | Ownership |
|-------|-------|---------|-----------|
| Coordinator | `openai/gpt-6-luna` | `high` | User communication, routing, and acceptance |
| Investigator | `openai/gpt-6-luna` | `medium` | Focused read-only discovery |
| Worker | `openai/gpt-6-luna` | `high` | Ordinary feature ownership from investigation through verification |
| Senior developer | `openai/gpt-6-sol` | `high` | Foundational contracts, high-risk implementation, difficult diagnosis, independent review |

### Shared usage policy

- Route directly to the appropriate owner; a task does not need to visit every agent. Use senior judgment before consequential cross-system implementation, rather than only after repeated failures.
- Keep known-file lookups with the current owner. Use the runtime's investigator or explorer when focused discovery meaningfully reduces duplicated exploration.
- Reuse a related agent session when its context remains useful; pass concise contracts and results instead of entire transcripts.
- Default to one implementation owner. Parallelism is optional and subject to the ownership and Editor rules in `AGENTS.md`.
- Collect a small representative sample (about ten tasks) before retuning models or reasoning effort.

Configuration verification (2026-09-21): `opencode debug agent <name>` resolved the previous five-agent OpenCode setup. The four-agent setup above has only static validation until OpenCode CLI is available again. That earlier check does not validate the separate Codex configuration, model output quality, or subscription savings. Files ending in `.retired` under `.codex/` are historical references, not active configuration.

Suggested measurement record per task: task/risk, agent/model/variant or reasoning effort, observable usage or quota change, handoff count, first-pass acceptance, rework, elapsed time, and verification artifacts. Mark unavailable usage as unknown; do not invent token costs or subscription savings.

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
