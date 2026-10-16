# Food Factory — Bevy character sandbox

A flat floor and the Food Factory player character, nothing else. Movement, orbit camera and animator behaviour are
ported from the Unity project (`PlayerAvatar`, `OrbitCameraRig`, `PlayerAnimation`, `Player.controller`).

## Run

```bash
sudo apt install libxkbcommon-x11-0   # one-time: winit needs it under WSLg/X11
cargo run
```

The first build compiles Bevy (~4 min); after that, rebuilds take about a second.

Controls: WASD/arrows to move, Left Shift to sprint, Space to jump, mouse to orbit, scroll to zoom. Esc frees the
cursor, and left click captures it again.

## Windows build

Cross-compiled from WSL. One-time setup: `sudo apt install mingw-w64`, then run
`rustup target add x86_64-pc-windows-gnu` from this folder.

```bash
cargo build --release --target x86_64-pc-windows-gnu --no-default-features --features gamepad
```

To ship it, put `target/x86_64-pc-windows-gnu/release/food_factory_bevy.exe` next to a copy of `assets/`. The exe
depends only on Windows system DLLs, and release builds open without a console window.

## Fast compiles

- `dev` feature (on by default): Bevy is dynamically linked. Turn it off for a standalone build:
  `cargo build --release --no-default-features`.
- `.cargo/config.toml`: links with LLD through clang.
- `[profile.dev]`: `opt-level = 1` for this crate and `3` for dependencies.
- `rust-toolchain.toml` pins Rust 1.99.1, because Bevy 0.20 needs 1.97.1 or newer.

Audio, gamepad and Wayland support are left out because their system dev libraries (ALSA, udev, wayland-client) are
not installed. After `sudo apt install libudev-dev`, `cargo run --features gamepad` adds gamepad support.

## Character asset

`assets/models/player.glb` is exported from `ArtSource/Player/Player_Asset.blend`, which is stored in Git LFS. Export it
again with Blender 5.2:

```bash
blender -b ArtSource/Player/Player_Asset.blend --python bevy/tools/export_player_glb.py -- "$PWD/bevy/assets/models/player.glb"
```

## Animation

`src/animation.rs` mirrors the Unity animator:

- Parameters come from measured motion: smoothed velocity, Speed damped over 0.1 s, and Grounded meaning within
  0.25 m of the floor.
- Locomotion is a phase-synced 1D blend of Idle, Walk and Run at 0 / 1.6 / 6 m/s.
- Jump, Fall and Land use Unity's transition conditions and crossfade times.

## Verification capture

`FF_CAPTURE_DIR=/some/dir cargo run` drives the character through idle, walk, run and jump, saves a screenshot of each
phase into that directory, and exits.
