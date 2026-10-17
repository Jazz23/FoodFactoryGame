# Belt build-mode verification

- Run identity: `belts-build-20261017` (2026-10-17).
- Requested test filter: `belt`, binary `food_factory_bevy`, features `--no-default-features --features dev`.
- Matched: **6 tests, 6 passed, 0 failed**. Artifact: `tests.log`.
- Runtime: `cargo run --no-default-features --features dev`, DX12, isolated `final-data/` save root.
- Runtime artifacts: `final-capture.log`, `final/before.png`, `final/selected.png`, `final/placed.png`.
- Camera remained exactly `(focus=(0,0,0), yaw=0, pitch=90, distance=30.800001)` through selection and placement.
- Replicated belts: `(10,10,0)`, `(10,11,0)`, `(10,12,0)`, `(10,13,1)`, `(11,13,1)`, `(12,13,1)`, `(13,13,1)`;
  tuples are `(x,z,direction)`, all on level 0. Ten purchased belts became three carried plus seven placed.
- Repeat selection retained one purchased pack. Esc during a drag retained Belts, the next Esc cleared the tool,
  and B exited build mode. No placed belts were lost during cancellation.
- Independent code and visual review completed with no remaining blocker.

The initial old-binary capture could not provide a usable runtime baseline (loader/environment errors). The defect
baseline was the explicit `panels.open = None` catalog handoff in the original source. Successful new-client captures
used DX12 to avoid an installed Vulkan overlay error. Windows cursor-grab warnings were logged at startup/exit;
the free-pointer build flow completed. Remote multiplayer and Unity Editor were not changed or exercised.
