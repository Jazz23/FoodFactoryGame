# Development Workflow

## Editor Rules

1. Confirm the connected Editor with `unity status --json --no-banner --project-path E:\Projects\Unity\FoodFactoryGame`.
2. Discover project commands with `unity list --json --no-banner --project-path E:\Projects\Unity\FoodFactoryGame`.
3. Use registered `factory_*` commands for recurring factory workflows.
4. Serialize live mutations, compilation, and test execution. Parallelize independent reads only.

## Verification Contract

`factory_verify` starts one named verification profile and returns a run ID. Poll its status command until it reaches `passed`, `failed`, or `infrastructure_failed`.

Every result records:

- Requested profile and filter.
- Run ID and source revision when available.
- Matched test count.
- Summary counts and failed test messages.
- Full-result artifact path.

Zero matched tests, compile failures, runner initialization failures, and timeouts are not successful verification. A test failure is distinct from an infrastructure failure.

## Failure Workflow

1. Establish or record the baseline.
2. Run one explicit profile with an isolated save path for stateful tests.
3. Capture structured player, floor, transition, presentation, UI, and relevant console state on failure.
4. State the evidence-backed hypothesis before changing code or adding waits.
5. Re-run the smallest profile that tests the hypothesis.

## Test Boundaries

- Domain tests call application commands directly and use isolated `FactoryTestWorld` state.
- Scene/network tests cover ownership, transitions, sharing, isolation, and disconnect behavior.
- UI tests deliberately verify binding, readiness, and displayed state.
- PlayMode fixtures own temporary database paths, network lifecycle, static/UI reset, stable identities, and cleanup.
- Tests must not resize gameplay buildings or modify the application database.
