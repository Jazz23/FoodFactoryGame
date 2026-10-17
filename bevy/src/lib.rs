// Headless game: world generation, the authoritative simulation, persistence, employees and networking. Rendering and input
// (the binary) are projections over it and never own gameplay state.
pub mod content;
pub mod employees;
pub mod goods;
pub mod net;
pub mod persistence;
pub mod protocol;
pub mod server;
pub mod site_walk;
pub mod traffic;
pub mod recipes;
pub mod world;
