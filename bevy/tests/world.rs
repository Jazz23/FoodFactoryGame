// World generator port: valid, deterministic layouts with a starting restaurant and a drivable road network.
use food_factory_bevy::world::{generator, roads::RoadNetwork, settings::WorldSettings, layout::Ownership};

#[test]
fn generates_a_valid_deterministic_world() {
    let settings = WorldSettings::default();
    let a = generator::generate("20260927", 20260927, &settings).expect("valid world");
    let b = generator::generate("20260927", 20260927, &settings).expect("valid world");
    assert_eq!(serde_json::to_string(&a.layout).unwrap(), serde_json::to_string(&b.layout).unwrap());
    let layout = &a.layout;
    assert!(layout.buildings.len() > 500, "buildings {}", layout.buildings.len());
    let start = layout.building(&layout.start_restaurant_id).expect("start");
    assert_eq!(start.ownership, Ownership::Player);
    assert!(start.service_yard.is_some());
    assert_eq!(layout.lots.len(), layout.buildings.iter().filter(|b| b.has_lot()).count());
    let network = RoadNetwork::new(layout).unwrap();
    let lot = layout.lot_of_site(&start.site_id).unwrap();
    let from = network.locate(lot.access.x, lot.access.z).expect("start on network");
    let far = layout.lots.last().unwrap();
    let to = network.locate(far.access.x, far.access.z).expect("far lot on network");
    let legs = network.route(from, to, |s, _| network.segments[s].length as i64 * 100, |_, _| 0).expect("route");
    assert!(!legs.is_empty());
    println!("buildings {} lots {} trees {} attempts {}", layout.buildings.len(), layout.lots.len(), layout.trees.len(), a.attempts.len());
}

#[test]
fn several_seeds_generate() {
    let settings = WorldSettings::default();
    for seed in 1..6u64 {
        generator::generate(&seed.to_string(), seed, &settings).expect("valid world");
    }
}
