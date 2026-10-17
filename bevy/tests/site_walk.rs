// Isolated geometry checks for customer presentation: front-door entry, routed queues, table approaches and blocked paths.
use food_factory_bevy::goods::{self, Building, Equipment, EquipmentState, GridCell, SiteLayout, Snapshot, Structure};
use food_factory_bevy::site_walk::{Point, SiteWalk};

fn fixture() -> Snapshot {
    Snapshot { site_layouts: vec![SiteLayout { site_id: "site".into(), width: 12, depth: 12 }],
        buildings: vec![Building { site_id: "site".into(), cell_x: 2, cell_z: 2, width: 8, depth: 8,
            doors: vec![GridCell { x: 5, z: 2 }], ..Default::default() }],
        equipment: vec![Equipment { id: "register".into(), site_id: "site".into(), kind: goods::COUNTER_KIND.into(),
            state: EquipmentState::Placed, cell_x: 5, cell_z: 6, width: 2, depth: 1, rotation: 2, ..Default::default() }],
        ..Default::default() }
}

fn assert_open_segments(walk: &SiteWalk, points: &[Point]) {
    for segment in points.windows(2) {
        for i in 0..=100 {
            let t = i as f32 / 100.0;
            let x = segment[0].0 + (segment[1].0 - segment[0].0) * t;
            let z = segment[0].1 + (segment[1].1 - segment[0].1) * t;
            assert!(walk.walk.get(x.floor() as i32, z.floor() as i32), "blocked at {x}, {z}");
        }
    }
}

#[test]
fn street_to_register_and_table_goes_through_front_door() {
    let view = fixture();
    let walk = SiteWalk::new(&view, "site", None).unwrap();
    let line = walk.service_line(&view.equipment[0]);
    assert_eq!(line[0], (5.5, 5.5));
    let entry = walk.entrance(line[0]).unwrap();
    let path = walk.route(entry, line[0], false).unwrap();
    assert_open_segments(&walk, &path);
    assert!(path.windows(2).any(|s| s[0].0 == 5.5 && s[1].0 == 5.5 && s[0].1 <= 2.5 && s[1].1 >= 2.5));
    let path = walk.route(line[0], (7.5, 7.5), true).unwrap();
    assert_open_segments(&walk, &path);
    assert_eq!(path.last(), Some(&(7.5, 7.5)));
    for pair in line.windows(2) { assert_eq!((pair[0].0 - pair[1].0).abs() + (pair[0].1 - pair[1].1).abs(), 1.0); }
}

#[test]
fn back_door_is_not_a_customer_entrance() {
    let mut view = fixture();
    view.buildings[0].structures.push(Structure { kind: goods::DOOR_STRUCTURE.into(), role: goods::SERVICE_DOOR_ROLE.into(), x: 5, z: 2, ..Default::default() });
    let walk = SiteWalk::new(&view, "site", None).unwrap();
    assert!(!walk.walk.get(5, 2));
    assert!(walk.service_line(&view.equipment[0]).is_empty());
    assert!(walk.route((5.5, 0.5), (5.5, 5.5), false).is_none());
}

#[test]
fn topology_change_invalidates_routes_and_closed_rooms_stay_unreachable() {
    let mut view = fixture();
    let before = SiteWalk::new(&view, "site", None).unwrap();
    view.buildings[0].doors.clear();
    let after = SiteWalk::new(&view, "site", None).unwrap();
    assert!(!before.same_ground(&after));
    assert!(after.route((5.5, 0.5), (5.5, 5.5), false).is_none());
    assert!(after.entrance((5.5, 5.5)).is_none());
}

#[test]
fn seat_can_end_on_occupied_cell_but_queue_cannot() {
    let view = fixture();
    let walk = SiteWalk::new(&view, "site", None).unwrap();
    let seat = (5.5, 6.5);
    assert!(walk.route((5.5, 4.5), seat, false).is_none());
    let path = walk.route((5.5, 4.5), seat, true).unwrap();
    assert_open_segments(&walk, &path[..path.len() - 1]);
    assert_eq!(path.last(), Some(&seat));
    let approach = path[path.len() - 2];
    assert_eq!((approach.0 - seat.0).abs() + (approach.1 - seat.1).abs(), 1.0);
}

#[test]
fn street_side_and_rotated_register_control_the_queue() {
    let mut view = fixture();
    view.equipment[0].rotation = 1;
    let offer = goods::PropertyOffer { width: 12, depth: 12, access_x: 12, access_z: 6, ..Default::default() };
    let walk = SiteWalk::new(&view, "site", Some(&offer)).unwrap();
    let line = walk.service_line(&view.equipment[0]);
    assert_eq!(line[0].0, 6.5);
    assert_eq!(line.last().unwrap().0, 11.5);
    assert_eq!(walk.entrance(line[0]).unwrap().0, 12.5);
    assert_open_segments(&walk, &line);
}

#[test]
fn north_and_south_corner_entries_stay_on_the_street_side() {
    for north in [false, true] {
        for x in [0, 3] {
            let z = if north { 3 } else { 0 };
            let view = Snapshot { site_layouts: vec![SiteLayout { site_id: "site".into(), width: 4, depth: 4 }], ..Default::default() };
            let offer = goods::PropertyOffer { width: 4, depth: 4, access_x: 2, access_z: if north { 4 } else { -1 }, ..Default::default() };
            let walk = SiteWalk::new(&view, "site", Some(&offer)).unwrap();
            let target = (x as f32 + 0.5, z as f32 + 0.5);
            let entry = walk.entrance(target).unwrap();
            assert_eq!(entry, (target.0, if north { 4.5 } else { -0.5 }));
            assert!(walk.route(entry, target, false).is_some());
        }
    }
}

#[test]
fn seat_against_partition_never_steps_into_wall() {
    let mut view = fixture();
    view.buildings[0].structures.push(Structure { kind: goods::PARTITION_STRUCTURE.into(), x: 7, z: 7, ..Default::default() });
    let walk = SiteWalk::new(&view, "site", None).unwrap();
    assert!(walk.route((5.5, 5.5), (7.5, 7.5), true).is_none());
    assert!(walk.route((5.5, 5.5), (7.5, 6.5), true).is_some(), "table area is still reachable");
}

#[test]
fn old_site_pose_cannot_become_an_unchecked_inter_site_segment() {
    let view = fixture();
    let walk = SiteWalk::new(&view, "site", None).unwrap();
    let target = walk.service_line(&view.equipment[0])[0];
    assert!(walk.route((-50.5, 0.5), target, false).is_none());
    let entry = walk.entrance(target).unwrap();
    assert!(walk.route(entry, target, false).is_some(), "new site presentation starts at its own entrance");
}

#[test]
fn departure_from_a_seat_walks_back_out_the_front_door_to_the_street() {
    let view = fixture();
    let walk = SiteWalk::new(&view, "site", None).unwrap();
    let seat = (7.5, 7.5);
    let exit = walk.entrance(seat).unwrap();
    let path = walk.route(seat, exit, false).unwrap();
    assert_eq!(path.last(), Some(&exit));
    // Every leg except the final step off the street edge stays on open cells, and it leaves through the door at (5, 2).
    assert_open_segments(&walk, &path[..path.len() - 1]);
    assert!(path.windows(2).any(|s| s[0].0 == 5.5 && s[1].0 == 5.5 && s[0].1 >= 2.5 && s[1].1 <= 2.5));
}

#[test]
fn seat_in_a_wall_row_is_not_reachable() {
    let view = fixture();
    let walk = SiteWalk::new(&view, "site", None).unwrap();
    assert!(walk.seat_reachable((6.5, 8.5)));
    assert!(!walk.seat_reachable((6.5, 9.4)), "the building's back wall row");
    assert!(!walk.seat_reachable((-0.5, 4.0)), "outside the site grid");
}

#[test]
fn competitor_frontage_queues_along_the_facade_and_bends_back() {
    use food_factory_bevy::{goods::PropertyOffer, site_walk::Frontage};
    let offer = PropertyOffer { width: 10, depth: 12, building_x: 0, building_z: 2, building_width: 10, building_depth: 10,
        doors: vec![GridCell { x: 4, z: 2 }], ..Default::default() };
    let f = Frontage::new(&offer).unwrap();
    let near = |a: Point, b: Point| (a.0 - b.0).abs() < 1e-4 && (a.1 - b.1).abs() < 1e-4;
    assert!(near(f.door, (4.5, 1.7)) && near(f.apron, (4.5, 0.3)) && near(f.kerb, (4.5, -0.6)), "{f:?}");
    assert!(near(f.queue_spot(0), (5.3, 1.2)));
    // Six fit along the wider side; the seventh starts the next row out at the far end.
    assert!(near(f.queue_spot(6), (9.3, 0.4)));
    assert!(f.street.iter().all(|p| near((0.0, p.1), (0.0, -0.6))));
    assert_eq!(f.arrive((5.3, 1.2)), vec![f.kerb, f.apron, (5.3, 1.2)]);
    assert!(Frontage::new(&PropertyOffer::default()).is_none());
}
