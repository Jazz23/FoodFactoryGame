// Pre-transpiles the superui game UI's .tsx to assets/ui/game/.superui/build/*.js, which builds without the `hmr` feature
// load (with `hmr` the .tsx is transpiled live and hot-reloads instead).
fn main() {
    println!("cargo:rerun-if-changed=assets/ui/game");
    supersolid::build::transpile_dir("assets/ui/game");
}
