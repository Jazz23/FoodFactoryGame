# Food Factory — Bevy port (in progress)

An independent Bevy application alongside the original Unity project. The original character sandbox is being expanded
with a Rust authoritative simulation, SQLite persistence, native UI and Blender-exported game art. **This is not yet
an entire-game or visual-parity port.** The Unity project and its application saves remain intact.

Controls: WASD/arrows move, Left Shift sprint, Space jump, mouse orbit, scroll zoom, C overhead camera,
E inventory, P company screen, B equipment, R rotate held equipment, Q clear placement, and Esc menu.

The development menu starts or resumes a **separate Bevy world**. Use an explicit isolated database for verification:

```powershell
cargo run -- --play --save ./verification/manual/world.db
```

The default database is `bevy/saves/bevy-world.db`. Do not use an existing Unity database: its snapshot format is not
the Rust format, and a verified Unity-save importer has not been implemented. See [port contracts](docs/architecture.md),
[core interface and gaps](docs/core-interface.md), and [Blender CLI asset pipeline](docs/assets.md).

## Windows setup (with hot reloading)

Run every command in PowerShell from this `bevy` folder.

### 1. Install the tools (one time)

1. **Visual Studio C++ build tools.** Install
   [Build Tools for Visual Studio](https://visualstudio.microsoft.com/visual-cpp-build-tools/) (or Visual Studio
   Community) with the **Desktop development with C++** workload. Rust uses its MSVC linker and the Windows SDK.
2. **Rust.** Install rustup from [rustup.rs](https://rustup.rs/) and accept the defaults
   (`x86_64-pc-windows-msvc`). Open a new terminal afterwards so `cargo` is on `PATH`. The first `cargo` command in
   this folder installs the Rust version pinned in `rust-toolchain.toml` automatically.
3. **Git LFS.** The character model `assets/models/player.glb` is stored in Git LFS. Without it the file is a small
   text pointer and the character does not appear:

   ```powershell
   git lfs install
   git lfs pull
   ```

4. **Dioxus CLI (`dx`)**, which drives hot patching. Its version must match the `subsecond` version in `Cargo.lock`
   (currently 0.7.10). Installing it compiles from source and takes a few minutes:

   ```powershell
   cargo install dioxus-cli@0.7.10 --locked
   dx --version
   ```

   If a Bevy update changes the version, check it with `Select-String -Path Cargo.lock -Pattern 'name = "subsecond"' -Context 0,1`
   and install the matching `dioxus-cli`.

### 2. Run with hot patching

```powershell
dx serve --hot-patch --no-default-features --features hotpatch
```

The first run compiles all of Bevy and takes about 7 minutes; the progress bar shows how far along it is. Later
launches reuse that build. When the window opens, edit the body of any system and save: dx shows
`Hot-patching...` and the running game picks up the change within a second or two. To try it, change the title text
in `show_fps` in `src/main.rs`. Files under `assets/` also reload when they change.

Some edits cannot be patched into the running game. Press `r` in the dx terminal to rebuild and restart after:

- changing a system's parameters, or adding or removing a system
- editing a `Startup` system (it already ran)
- changing a struct or component definition
- editing code in a crate other than this one

Why the command looks like this:

- `--no-default-features` drops the `dev` feature, so Bevy is linked statically. dx cannot launch a dynamically
  linked Bevy on Windows: the game exits with `0xc0000135` (DLL not found,
  [DioxusLabs/dioxus#4693](https://github.com/DioxusLabs/dioxus/issues/4693)). Patches rebuild only this crate, so they
  stay fast without dynamic linking.
- The `hotpatch` feature turns on Bevy's `hotpatching` and `file_watcher` features, and reads assets straight from
  this folder's `assets/`. dx launches the game from `target/dx/...`, so you do not need to set `BEVY_ASSET_ROOT`.
- dx builds into its own `target/x86_64-pc-windows-msvc/desktop-dev` folder with its own compiler flags, so it does
  not share the `cargo build` cache.

### 3. Run without hot patching

```powershell
cargo run
```

Bevy is dynamically linked here, so the first build takes about 2.5 minutes and rebuilds after an edit take about
3 seconds. Each change needs a restart.

### Troubleshooting

- **`linking with dx.exe failed ... lld is a generic driver`:** something set the Windows linker to `rust-lld.exe`
  (for example in `.cargo/config.toml` or a user-wide `~/.cargo/config.toml`). dx wraps the linker and cannot use
  `rust-lld.exe`, so remove that setting and use MSVC's default `link.exe`.
- **Game exits with `0xc0000135`:** the `dev` feature is on under dx. Use the exact command above, including
  `--no-default-features`.
- **Character missing:** run `git lfs pull` (step 1.3).

## Linux / WSL

```bash
sudo apt install lld clang libxkbcommon-x11-0   # one-time; winit needs libxkbcommon-x11 under WSLg/X11
cargo run
```

The first build compiles Bevy (~4 min); after that, rebuilds take about a second. Build on the Linux filesystem
(for example `~/`), not under `/mnt/`: Windows drives mounted into WSL are much slower to compile on.

### Windows release build from WSL

One-time setup: `sudo apt install mingw-w64`, then run `rustup target add x86_64-pc-windows-gnu` from this folder.

```bash
cargo build --release --target x86_64-pc-windows-gnu --no-default-features --features gamepad
```

To ship it, put `target/x86_64-pc-windows-gnu/release/food_factory_bevy.exe` next to a copy of `assets/`. The exe
depends only on Windows system DLLs, and release builds open without a console window.

## Fast compiles

- `dev` feature (on by default): Bevy is dynamically linked, and asset files hot-reload. Turn it off for a
  standalone build: `cargo build --release --no-default-features`.
- `.cargo/config.toml`: links with LLD through clang on Linux. Native Windows keeps MSVC's `link.exe`, because
  `dx serve --hot-patch` fails with `rust-lld.exe` ("lld is a generic driver").
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
