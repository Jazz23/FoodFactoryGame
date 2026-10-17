// Headless acceptance for presentation rules: traffic discharge/queues, safe crosswalk phases, recipe batching/cycles and dry-run isolation.
use std::collections::{BTreeMap, HashSet};
use food_factory_bevy::{content::GameContent, employees, goods::{RecipeDefinition, RecipeInput}, recipes, traffic,
    world::{generator, roads::{self, RoadNetwork}, settings::WorldSettings, layout::{JunctionControl, RoadKind}}};

#[test]
fn cars_queue_at_lights_and_discharge_without_overlapping() {
    let layout = generator::generate("presentation", 20260927, &WorldSettings::default()).unwrap().layout;
    let network = RoadNetwork::new(&layout).unwrap();
    let s = network.segments.iter().find(|s| s.length > 70 && s.kind != RoadKind::Rural && network.nodes[s.to].control == JunctionControl::TrafficLight && network.stops(s.index, true)).unwrap();
    let mut stopped = false; let mut moved = false;
    for tick in 0..1200 {
        // Game hour 8 (one game hour is 60 s) is peak load, so lanes are full enough to queue at red.
        let seconds = 8.0 * 60.0 + tick as f64 * 0.1;
        let cars = traffic::lane(&network, s.index, true, 0, seconds, 10.0);
        for pair in cars.windows(2) { assert!(pair[0].along - pair[1].along >= traffic::GAP - 0.001); }
        for car in cars {
            if car.speed == 0.0 {
                stopped = true;
                assert!(car.along <= s.length as f32 - 10.0 - traffic::CAR_HALF_LENGTH + 0.001);
            } else { moved = true; }
        }
    }
    assert!(stopped && moved, "fixture must exercise red queues and moving cars: stopped={stopped} moved={moved} segment={} length={}", s.id, s.length);
}

#[test]
fn truck_and_person_holds_propagate_back_through_queue() {
    let mut cars = (0..5).map(|i| traffic::Car { along: 50.0 - i as f32 * 7.0, key: i, speed: 10.0, stop: f32::INFINITY }).collect();
    traffic::hold_behind(&mut cars, 48.0, 10.5, 7.0);
    assert_eq!(cars[0].along, 37.5);
    for pair in cars.windows(2) { assert!(pair[0].along - pair[1].along >= traffic::GAP - 0.001); }
    assert!(cars.iter().all(|c| c.speed == 0.0));
    traffic::hold_behind(&mut cars, 37.0, 4.5, 1.0);
    assert_eq!(cars[0].along, 32.5);
}

#[test]
fn crossing_requires_enough_green_to_finish_and_a_gap_without_lights() {
    let mut node = roads::Node { id: "cross".into(), x: 0, z: 0, control: JunctionControl::TrafficLight, segments: vec![], light_offset: 0 };
    assert!(traffic::may_cross(&node, true, 12.0, 1.3, 0.0, false));
    assert!(!traffic::may_cross(&node, true, 12.0, 1.3, 10.0, false));
    assert!(!traffic::may_cross(&node, false, 12.0, 1.3, 0.0, false));
    node.control = JunctionControl::StopSign;
    assert!(!traffic::may_cross(&node, true, 12.0, 1.3, 0.0, true));
    assert!(traffic::may_cross(&node, true, 12.0, 1.3, 0.0, false));
}

#[test]
fn recipe_tree_rounds_batches_and_stops_cycles() {
    let a = RecipeDefinition { id: "a".into(), output_item_id: "a".into(), output_quantity: 4,
        inputs: vec![RecipeInput { item_id: "b".into(), quantity: 3 }], ..Default::default() };
    let b = RecipeDefinition { id: "b".into(), output_item_id: "b".into(), output_quantity: 2,
        inputs: vec![RecipeInput { item_id: "a".into(), quantity: 1 }], ..Default::default() };
    let recipes = BTreeMap::from([("a".into(), a.clone()), ("b".into(), b)]);
    let expanded = HashSet::from(["/0".into(), "/0/0".into()]);
    let tree = recipes::ingredients(&recipes, &a, 5, &expanded);
    assert_eq!(tree.len(), 2);
    assert_eq!(tree[0].need, 6);
    assert_eq!(tree[0].batches, 3);
    assert_eq!(tree[1].need, 3);
    assert!(tree[1].cycle && !tree[1].expanded);
    assert_eq!(recipes::batch_text(5, 4), "2 batches of 4 (3 spare)");
    assert_eq!(recipes::batch_text(1, 1), "1 batch of 1 (0 spare)");
    let content = GameContent::bundled().unwrap();
    assert!(!content.recipes.is_empty());
}

#[test]
fn dry_run_detects_runtime_errors_and_bounds_infinite_programs() {
    assert!(employees::dry_run("local value = nil; value()").is_err());
    assert!(employees::dry_run("unknown_api()").is_err());
    assert!(employees::dry_run("while true do end").unwrap().contains("bounded prefix"));
    assert!(employees::dry_run("assert(require == nil); assert(loadstring == nil)").is_ok());
}

#[test]
fn dry_run_stand_ins_cover_employee_api_and_return_shapes() {
    assert!(employees::dry_run(r#"
        move_to("oven"); path({{1, 2}}); assert(take("fridge", "dough", 1) > 0); assert(put("oven") > 0)
        wait(1); place("oven", 1, 2); pick_up("oven"); place_belt(1, 2, 0)
        turn_on("oven"); turn_off("oven"); toggle("oven"); set_recipe("oven", "bread"); work("oven")
        assert(count("hands") > 0); assert(carrying() > 0); assert(room("oven") > 0); assert(is_on("oven"))
        assert(type(find("oven")) == "table"); assert(type(holding()) == "table"); assert(type(accepts("oven")) == "table")
        assert(position().x == position()[1]); say("done"); print("done")
    "#).is_ok());
}
