# Station jobs (domain) verification — 2026-09-22

Branch `station-jobs`. Editor: Unity 6000.5.9f1, project `E:\Projects\Unity\FoodFactoryGame`, driven through Unity CLI MCP. The Pipeline returns no native run ID and `StatusPath: null`, so the run identities below are **assigned** for traceability. Unity writes only one `TestResults.xml` and overwrites it on each run. The copy preserved here is from the last run (baseline).

## Runs

| Assigned run identity | Requested filter / type / mode | Matched | Passed | Failed | Skipped | Artifact |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `station-jobs-20260922-r1-goods-editmode` | `FoodFactoryGame.Goods.EditModeTests` / assembly / editor | 32 | 32 | 0 | 0 | this file; synchronous Pipeline response, duration 1.18 s |
| `station-jobs-20260922-r1-listen-playmode` | `FoodFactoryGame.Goods.PlayModeTests` / assembly / playmode (async) | 1 | 1 | 0 | 0 | this file; Pipeline response, duration 2.6 s (XML overwritten by the next run) |
| `station-jobs-20260922-r1-baseline` | `FoodFactoryGame.Baseline.EditModeTests` / assembly / editor | 4 | 4 | 0 | 0 | [`artifacts/station-jobs-20260922-baseline-editmode.xml`](artifacts/station-jobs-20260922-baseline-editmode.xml) (start 2026-09-23 03:01:52Z) |

`recompile`: `completed`, `compilationFailed: false`, no errors. After clearing the console, the only warnings were the two existing FishNet `CS0618` obsolete-API warnings in `UpgradeFromMirrorMenu.cs`.

### Pre-merge re-run (r2) on the final code

This re-run happened after the machine-pickup rule was added, so all three suites were checked against the code being merged. `recompile` returned `up_to_date`, and the console was cleared before the runs.

| Assigned run identity | Requested filter / type / mode | Matched | Passed | Failed | Skipped | Artifact |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `station-jobs-20260922-r2-goods-editmode` | `FoodFactoryGame.Goods.EditModeTests` / assembly / editor | 32 | 32 | 0 | 0 | this file; synchronous Pipeline response, duration 0.49 s |
| `station-jobs-20260922-r2-listen-playmode` | `FoodFactoryGame.Goods.PlayModeTests` / assembly / playmode (async) | 1 | 1 | 0 | 0 | this file; Pipeline response, duration 2.59 s (XML overwritten by the next run) |
| `station-jobs-20260922-r2-baseline` | `FoodFactoryGame.Baseline.EditModeTests` / assembly / editor | 4 | 4 | 0 | 0 | [`artifacts/station-jobs-20260922-r2-baseline-editmode.xml`](artifacts/station-jobs-20260922-r2-baseline-editmode.xml) (test-run id 2, start 2026-09-23 03:22:28Z) |

The console showed two errors, both FishNet's `SpawnablePrefabs is null on goods-test-host/remote`. FishNet's editor `Reset` emits them during the PlayMode fixture setup, and the test declares both with `LogAssert.Expect`. There were no other errors or warnings.

The 32 goods EditMode tests are the 17 existing `GoodsWorldTests` plus 15 new `StationJobTests`. One existing test changed: `CorruptLatestRecoversPreviousAndNewerSchemaIsRejected` now uses `CurrentSchema + 1` as its "newer schema", because v2 is now current.

## Coverage (`StationJobTests`)

- Start consumes the most-exposed inputs and completion emits `<jobId>:out` with the right item, quantity, owner, location and fresh exposure. Consumed goods stay accounted for inside the job. `View` shows the site's stations and jobs.
- Rejections leave goods, reservations, stations and jobs unchanged: no grant, unknown station, wrong station kind, unknown recipe, insufficient inputs, spoiled-only inputs, reserved inputs, busy station. Reserved quantity is never consumed.
- A replayed request returns the same job ID. Another player reusing the request ID gets their own outcome.
- One 12-second step equals twelve 1-second steps, including 7 s of ambient overshoot exposure and zero exposure for a refrigerated output.
- A full output location blocks the job, which then emits once room appears, with nothing lost.
- Failed saves during start, tick and pickup roll back with nothing acknowledged.
- A job saved mid-run, reloaded without registered recipes, and replayed completes exactly once. The same holds after a corrupt latest file falls back to `.previous`.
- Pickup: a running job returns its inputs, with frozen exposure, to the carried location; a blocked job hands over its output; `capacity`, `forbidden` and `invalid-route` leave the job running; a durable pickup survives reload and replay.
- A hand-built v1 save loads as v2 with empty stations and jobs and is written back as v2.
- `Validate` rejects a duplicate station, a cross-site station location, an orphan job, two jobs on one station, remaining time out of range, blocked-before-completion, and an output ID collision.

## Not verified

- Any network command, placed station, visuals, or multiplayer path for jobs. The listen-server test only confirms that the bridge still works on schema v2.
- Scale, or behaviour with real recipe content.
