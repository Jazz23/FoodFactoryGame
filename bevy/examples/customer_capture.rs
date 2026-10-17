// Creates an isolated SQLite presentation fixture with known customer states (one diner leaves after 40 s), at noon; refuses to overwrite any existing directory.
// Usage: cargo run --example customer_capture -- ABSOLUTE_NEW_WORLD_DIRECTORY. This is synthetic capture data, not a sale test.
use std::{path::PathBuf, sync::Arc};
use food_factory_bevy::{content::GameContent, goods::{self, Customer, CustomerState}, server::{ServerOptions, WorldServer}};

fn main() {
    let directory = PathBuf::from(std::env::args().nth(1).expect("explicit new world directory required"));
    assert!(directory.is_absolute() && !directory.exists(), "use a new absolute scratch directory");
    let mut server = WorldServer::open(&ServerOptions { save_directory: directory, world_id: "customer-walk".into(),
        seed_text: "20260927".into(), only_secret: None }, Arc::new(GameContent::bundled().unwrap())).unwrap();
    let site = server.start_site().to_string();
    let state = &mut server.world.state;
    let counter = state.equipment.iter().find(|e| e.site_id == site && e.kind == goods::COUNTER_KIND).unwrap().clone();
    let table = state.equipment.iter().find(|e| e.site_id == site && e.kind == goods::TABLE_KIND).unwrap().clone();
    let district = state.districts[0].id.clone();
    for (i, status, seconds) in [(0, CustomerState::Ordering, 28), (1, CustomerState::Queued, 0), (2, CustomerState::Travelling, 18), (3, CustomerState::Eating, 40)] {
        state.customers.push(Customer { id: format!("capture-customer-{i}"), restaurant_id: site.clone(), district_id: district.clone(),
            state: status, counter_id: if status == CustomerState::Ordering { counter.id.clone() } else { String::new() }, table_id: table.id.clone(), dine_in: true, seat: i,
            remaining_seconds: seconds, patience_seconds: 600, ticket: i as i64, appearance: i, ..Default::default() });
    }
    // The competitor nearest the start lot gets three queued customers and one arriving, for the frontage presentation.
    let lot = server.layout.lots.iter().find(|l| state.properties.iter().any(|p| p.site_id == site && p.lot_id == l.id)).map(|l| (l.x, l.z)).unwrap_or_default();
    if let Some(competitor) = state.competitors.iter().filter(|c| !c.lot_id.is_empty()).min_by_key(|c| (c.map_x - lot.0).abs() + (c.map_z - lot.1).abs()).cloned() {
        for (i, status, seconds) in [(4, CustomerState::Queued, 0), (5, CustomerState::Queued, 0), (6, CustomerState::Queued, 0), (7, CustomerState::Travelling, 8)] {
            state.customers.push(Customer { id: format!("capture-customer-{i}"), restaurant_id: competitor.id.clone(), district_id: district.clone(),
                state: status, remaining_seconds: seconds, patience_seconds: 600, ticket: i as i64, appearance: i, ..Default::default() });
        }
        println!("Competitor {} on {}", competitor.id, competitor.lot_id);
    }
    state.next_customer_number = state.next_customer_number.max(8);
    // Noon: competitors have customers and streets have traffic.
    state.clock_seconds = 12 * food_factory_bevy::world::clock::HOUR_SECONDS;
    state.revision += 1;
    println!("Fixture site {site}; register ({}, {}); table ({}, {})", counter.cell_x, counter.cell_z, table.cell_x, table.cell_z);
    for b in state.buildings.iter().filter(|b| b.site_id == site) { println!("Building ({}, {}) {}x{}; doors {:?}", b.cell_x, b.cell_z, b.width, b.depth, b.doors); }
    server.shutdown();
}
