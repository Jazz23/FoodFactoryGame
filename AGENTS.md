# Project Instructions

- Use `var` for local variables.
- Use Input System action maps; do not hard-code input.
- Treat required serialized references as configured. Add null checks only when `null` is valid or lifecycle-dependent.
- Use null pattern matching, such as `player is not null`, instead of `player != null`.
- Add a concise purpose comment at the top of every project-authored C# file.
- Use Unity CLI MCP (`unity mcp --project-path E:\Projects\Unity\FoodFactoryGame`) for live Editor operations. Do not manually edit scene or prefab YAML when MCP can make the change safely.
- Prefer registered project-specific `factory_*` `[CliCommand]` commands over equivalent generic MCP sequences or ad hoc `eval` code. Use `unity list` to discover their arguments.
- Run `factory_reconcile_save` as a dry run first and provide an explicit isolated database path unless the user explicitly requests modification of the application database.
- After C# changes, wait for compilation, check for new console errors, and run relevant tests.
- Factory shell footprints include wall cells; usable interior size is `max(footprint - (2, 2), zero)`. Interior-only buildings retain their configured dimensions.
- Factory reconciliation must preserve identities and gameplay state. Overflow equipment remains recoverable and must never be silently deleted.
- Stateful tests must use isolated save paths and must not modify the application database.