# Project Instructions

## Authority and Scope

- `Food_Factory_Restaurant_GDD.md` is the authoritative gameplay design; `gdd.md` is superseded historical reference. Distinguish confirmed requirements, proposals, and open decisions. Do not silently promote proposals to requirements.
- `dev_resources.md` records resource facts, agent defaults, and setup gaps. Do not infer working systems from old instructions, generated projects, or test artifacts.
- Maintain accepted contracts and implementation status in `docs/architecture.md`, verified workflows in `docs/development.md`, and consequential technical decisions in `docs/decisions/`. These currently describe the development baseline, not completed gameplay foundations. Update them when contracts/workflows change, labeling implemented, planned, and undecided behavior.

## C# and Authoring

- Use `var` for local variables.
- Use Input System action maps; do not hard-code input.
- Treat required serialized references as configured at runtime; validate that contract through authoring checks or relevant tests. Add runtime null checks only when null is valid or lifecycle-dependent.
- Use null pattern matching for ordinary managed objects. For `UnityEngine.Object` references, use Unity-aware null checks when destroyed-object semantics matter; `is not null` does not detect a destroyed native object.
- Add a concise purpose comment at the top of project-authored C# files that explains responsibility or a non-obvious invariant rather than repeating the class name.

## Multiplayer and State

- The server owns gameplay state. Clients request actions; validate inventory transfers, payments, and job claims authoritatively.
- Client visibility, camera position, and presentation scene loading must not determine whether a site's simulation exists or progresses.
- Preserve stable identities and gameplay state across persistence and recovery. Failed/cancelled operations must not silently delete goods or equipment or duplicate inventory/payments.
- Distant-site operation while the world runs does not imply offline progression or host migration. Player count, hosting/disconnect behavior, and physical-goods representation remain open in the GDD.

## Unity Operations and Verification

- Do not use the `unity-mcp-efficient` skill or its facade workflow in this project.
- Use Unity CLI MCP (`unity mcp --project-path E:\Projects\Unity\FoodFactoryGame`) for live Editor operations. Do not manually edit scene or prefab YAML when MCP can make the change safely.
- Discover currently registered commands and arguments before using them. Prefer verified project-specific commands over equivalent generic sequences; do not assume the old `factory_*` tooling exists.
- Run save migration/reconciliation as a dry run first when supported, with an explicit isolated save/database path unless the user requests modification of application data.
- After C# changes, wait for compilation, check for new console errors, and run relevant tests.
- Stateful tests must use isolated save paths and must not modify the application database.
- Establish a baseline before attributing a regression; after a failure, capture diagnostics and state an evidence-backed hypothesis before adding waits or changing authoring.
- Assign one owner to live Editor mutations, compilation, and test execution; serialize them. Parallelize independent reads. Parallel code work requires explicit disjoint ownership; concurrent Editor verification requires separate project instances/worktrees.
- Do not change gameplay authoring to satisfy test fixture assumptions; use isolated test fixtures for test-specific dimensions and records.
- Verification must report its requested filter, run identity, matched test count, and artifact path. Zero matched tests are a failure.
- Match verification to risk: domain tests for state rules, PlayMode tests for Unity integration, actual multiplayer checks for replication, and running-game captures for visual acceptance.
- Mark acceptance criteria complete only when the required evidence exists; a build dry run is not a successful player build.
- Keep tool responses concise and retrieve full logs only for relevant failures.

## Task Ownership and Efficiency

- When delegation is authorized, route by uncertainty and consequences: worker for bounded existing-pattern changes; mid-level developer for ordinary end-to-end features; senior developer directly for foundational contracts, high-risk implementation, or difficult diagnosis. Senior involvement does not require prior worker failure.
- Keep small known-file lookups with the current owner. Use an investigator for focused discovery that avoids substantial duplicated exploration. Do not send every task through every role.
- A handoff must state the goal, relevant decisions/contracts, owned files/systems, acceptance criteria, required verification, deferred scope, and escalation conditions. Reuse relevant context without copying whole transcripts.
- Implementation owners inspect, implement, and verify within scope. Return changed files/behavior, verification evidence, remaining issues, and decisions requiring attention. Do not mark unverified work complete.
- After failure, collect evidence and identify a supported cause before a targeted correction. Escalate unresolved uncertainty or recurrence of the same failure; do not repeat speculative fixes or escalate trivial understood errors.
- Require independent review for high-risk or visual changes, with concrete evidence. The reviewer must be distinct from the implementation owner; review is not a substitute for execution evidence.
- Optimize subscription use per accepted feature, including rework. Avoid blanket maximum reasoning, unnecessary handoffs, duplicate investigations, and repeated broad tests without new evidence. Defaults and measurement guidance are in `dev_resources.md`.
