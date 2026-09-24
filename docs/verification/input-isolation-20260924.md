# Verification: input isolation for the Editor and PlayMode tests (2026-09-24)

Goal: agents can work in the Editor, compiling and running tests, while the developer uses other controllers (a Logitech
G29 wheel in another game). Workflow notes are in [development.md](../development.md) under Session Bootstrap.

Changes:

- The Session PlayMode fixtures (`BeltPlacementTests`, `BuildingPresenterTests`, `EquipmentPlacementTests`,
  `SessionBootstrapTests`) each own an `InputTestFixture`. Its setup runs before `DevSite` loads, and its teardown always
  runs, in a `finally`. The per-test `editorInputBehaviorInPlayMode` / `backgroundBehavior` overrides were removed. They sent
  **all** device input (real devices too) to the Game view while the Editor was unfocused.
- `Assets/InputSystem.inputsettings.asset` (new, registered in `EditorBuildSettings` as `com.unity.input.settings`):
  Background Behavior Reset And Disable All Devices, Play Mode Input Behavior All Devices Respect Game View Focus.
- `FoodFactoryGame.Session.PlayModeTests.asmdef` references `Unity.InputSystem.TestFramework`. That assembly compiles when
  `com.unity.test-framework` is present, so the package does not need to be in `testables` (which would also pull in the
  Input System's own integration tests).

Compilation: live Editor `recompile` (PID 41528), completed with no errors. Afterwards the console had only the declared
`SpawnablePrefabs is null on …-test-remote` errors.

| Run | Filter | Matched | Result | Artifact |
| --- | --- | --- | --- | --- |
| Baseline before any change: live Editor `run_tests` playmode, async | assembly `FoodFactoryGame.Session.PlayModeTests` | 15 | 14 passed, 1 failed (`HotbarKeysOverAStackAndDropsOnTheHotbarAssignSlots`, "The pointer is over the dough stack") | status only |
| Live Editor `run_tests` playmode, async, final code | assembly `FoodFactoryGame.Session.PlayModeTests` | 15 | 15 passed | status only |
| Live Editor `run_tests` playmode, async, final code, three consecutive runs | testName `…EquipmentPlacementTests.HotbarKeysOverAStackAndDropsOnTheHotbarAssignSlots` | 1 each | 1 passed each | status only |
| Live Editor `run_tests` editor, final code | assembly `FoodFactoryGame.Baseline.EditModeTests` | 4 | 4 passed | status only |
| Live Editor `run_tests` editor, final code | assembly `FoodFactoryGame.Session.EditModeTests` | 50 | 50 passed | status only |

After the PlayMode run, `InputSystem.devices` listed the real `Keyboard` and `Mouse`, both enabled. The fixture restores
the Editor's input state, and `InputSystem.settings` resolves to the new asset.

The hover test failed on the committed code in several earlier records and is described as intermittent. Its hovered-slot
lookup reads the `Point` action, which is bound to any pointer. Hypothesis: with all device input sent to the Game view, the
developer's real mouse competed with the test's virtual mouse. After isolation it passed in four runs out of four. That is
consistent with the hypothesis but does not prove it.

Not covered: the `FoodFactoryGame.Goods.PlayModeTests` assembly reads no input and was not changed or re-run. The wheel
did not appear in `InputSystem.devices` during this session, so no check with the wheel connected was made. Whether the
racing game keeps force feedback while the Editor is open is not affected by these settings: the Editor still lists
devices while idle.
