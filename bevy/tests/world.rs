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

#[test]
fn full_validator_passes_generated_worlds_and_catches_broken_ones() {
    use food_factory_bevy::world::{layout::JunctionControl, validator};
    let settings = WorldSettings::default();
    for seed in ["1", "2", "3"] {
        let layout = generator::generate(seed, 20260927, &settings).unwrap().layout;
        assert_eq!(validator::validate(&layout, &settings), Vec::<String>::new(), "seed {seed}");
    }
    let good = generator::generate("1", 20260927, &settings).unwrap().layout;
    let has = |layout: &food_factory_bevy::world::layout::WorldLayout, needle: &str| validator::validate(layout, &settings).iter().any(|p| p.contains(needle));
    // A junction without a control, a dropped level crossing, a tree inside a building, a shared ID.
    let mut broken = good.clone();
    let junction = broken.nodes.iter_mut().find(|n| n.control != JunctionControl::None).unwrap();
    junction.control = JunctionControl::None;
    assert!(has(&broken, "control None at"));
    let mut broken = good.clone();
    if !broken.crossings.is_empty() { broken.crossings.remove(0); assert!(has(&broken, "without a level crossing")); }
    let mut broken = good.clone();
    let b = broken.buildings[0].clone();
    broken.trees[0].x = b.x; broken.trees[0].z = b.z;
    assert!(has(&broken, "trees: 1 misplaced"));
    let mut broken = good.clone();
    broken.roads[1].id = broken.roads[0].id.clone();
    assert!(has(&broken, "id: blank, spaced or duplicate"));
}
