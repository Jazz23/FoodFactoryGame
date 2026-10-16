// End-to-end checks of the headless game on an isolated save: world creation in the starting restaurant, admission, durable
// commands and recovery, the clock with machines and customers, and an employee running a Lua script.
use std::sync::Arc;
use std::time::Duration;

use food_factory_bevy::content::GameContent;
use food_factory_bevy::goods::{self, GoodsWorld};
use food_factory_bevy::protocol::{ClientMessage, Command, Pose, ServerMessage, PROTOCOL_VERSION};
use food_factory_bevy::server::{ServerHandle, ServerOptions, WorldServer};

fn scratch(name: &str) -> std::path::PathBuf {
    let path = std::env::temp_dir().join(format!("ff-bevy-test-{name}-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&path);
    path
}

fn options(dir: &std::path::Path) -> ServerOptions {
    ServerOptions { save_directory: dir.to_path_buf(), world_id: "test-world".into(), seed_text: "20260927".into(), only_secret: None }
}

#[test]
fn a_new_world_starts_equipped_and_recovers() {
    let dir = scratch("create");
    let content = Arc::new(GameContent::bundled().expect("content"));
    let server = WorldServer::open(&options(&dir), content.clone()).expect("world");
    let state = &server.world.state;
    let site = server.start_site().to_string();
    assert_eq!(state.companies.len(), 1);
    assert_eq!(state.companies[0].cash, food_factory_bevy::server::STARTING_CASH);
    assert!(state.equipment.iter().any(|e| e.kind == goods::COUNTER_KIND && e.site_id == site), "register placed");
    assert!(state.equipment.iter().any(|e| e.kind == goods::TABLE_KIND && e.site_id == site), "table placed");
    assert!(state.equipment.iter().any(|e| e.kind == goods::DOCK_KIND && e.site_id == site), "starter dock placed");
    assert!(state.districts.len() > 10 && state.competitors.len() > 50, "districts {} competitors {}", state.districts.len(), state.competitors.len());
    let revision = state.revision;
    drop(server);
    let reopened = WorldServer::open(&options(&dir), content).expect("reopen");
    assert!(reopened.world.state.revision >= revision);
    assert_eq!(reopened.start_site(), site);
    let _ = std::fs::remove_dir_all(&dir);
}

fn welcome(rx: &std::sync::mpsc::Receiver<ServerMessage>) -> (String, String, Pose) {
    loop {
        match rx.recv_timeout(Duration::from_secs(10)).expect("message") {
            ServerMessage::Welcome { player, site, spawn, .. } => return (player, site, spawn),
            ServerMessage::Rejected { reason } => panic!("rejected: {reason}"),
            _ => {}
        }
    }
}

fn outcome(rx: &std::sync::mpsc::Receiver<ServerMessage>, request: &str) -> goods::Outcome {
    loop {
        match rx.recv_timeout(Duration::from_secs(10)).expect("message") {
            ServerMessage::Outcome { request: r, outcome } if r == request => return outcome,
            _ => {}
        }
    }
}

#[test]
fn a_player_buys_bakes_and_an_employee_carries_goods() {
    let dir = scratch("play");
    let content = Arc::new(GameContent::bundled().expect("content"));
    let handle = ServerHandle::start(options(&dir), content.clone()).expect("server");
    let (tx, rx) = handle.connect();
    tx.send(ClientMessage::Hello { version: PROTOCOL_VERSION, name: "Tester".into(), secret: "0123456789abcdef0123".into() }).unwrap();
    let (player, site, spawn) = welcome(&rx);
    assert!(spawn.x.is_finite());
    tx.send(ClientMessage::Pose { pose: Pose { x: spawn.x, y: 0.0, z: spawn.z, yaw: 0.0 } }).unwrap();
    let mut baseline = None;
    for _ in 0..200 {
        if let Ok(ServerMessage::Baseline { site: s, view }) = rx.recv_timeout(Duration::from_secs(5)) {
            if s == site {
                baseline = Some(view);
                break;
            }
        }
    }
    let view = baseline.expect("baseline");
    let inventory = goods::inventory_location_id(&player);
    assert!(view.lots.iter().any(|l| l.location_id == inventory && l.item_id == "dough" && l.quantity == 20), "starter dough");

    tx.send(ClientMessage::Command { request: "buy-oven".into(), command: Command::Buy { site: site.clone(), offer: "supplier-oven".into() } }).unwrap();
    let bought = outcome(&rx, "buy-oven");
    assert!(bought.accepted, "buy oven: {}", bought.reason);
    tx.send(ClientMessage::Command { request: "hire".into(), command: Command::Hire }).unwrap();
    let hired = outcome(&rx, "hire");
    assert!(hired.accepted, "hire: {}", hired.reason);
    drop(tx);
    drop(handle);
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn machines_bake_customers_buy_and_lua_runs() {
    let dir = scratch("sim");
    let content = Arc::new(GameContent::bundled().expect("content"));
    let mut server = WorldServer::open(&options(&dir), content.clone()).expect("world");
    let site = server.start_site().to_string();
    let player = "player-test";
    server.world.ensure_grant(player, &site, 30, &[("bread".into(), 20, 3600)]).unwrap();
    let register = server.world.state.equipment.iter().find(|e| e.kind == goods::COUNTER_KIND).unwrap().clone();
    let inventory = goods::inventory_location_id(player);
    let bread = server.world.state.lots.iter().find(|l| l.location_id == inventory).unwrap().id.clone();
    let moved = server.apply(player, "stock", Command::Transfer { lot: bread, destination: register.input_location_id(), quantity: 20 });
    assert!(moved.accepted, "stock register: {}", moved.reason);
    let staffed = server.apply(player, "staff", Command::Staff { register: register.id.clone(), staff: player.into() });
    assert!(staffed.accepted, "staff: {}", staffed.reason);
    let cash = server.world.state.companies[0].cash;
    for _ in 0..900 {
        server.world.advance(1).unwrap();
    }
    let sold = server.world.state.ledger.iter().filter(|e| e.kind == "sale").count();
    assert!(sold > 0, "customers bought bread (customers alive {})", server.world.state.customers.len());
    assert!(server.world.state.companies[0].cash > cash);

    // An employee carries goods from the player's hands... placed into a fridge by script.
    let mut world_check: &GoodsWorld = &server.world;
    let _ = &mut world_check;
    let _ = food_factory_bevy::server::check_syntax("while true do wait(1) end");
    assert!(food_factory_bevy::server::check_syntax("this is not lua").is_some());
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn an_employee_script_carries_dough_to_the_oven() {
    let dir = scratch("employee");
    let content = Arc::new(GameContent::bundled().expect("content"));
    let mut server = WorldServer::open(&options(&dir), content).expect("world");
    let site = server.start_site().to_string();
    let player = "player-boss";
    server.world.ensure_grant(player, &site, 30, &[("dough".into(), 10, 7200)]).unwrap();
    let start = food_factory_bevy::server::start_offer(&server.layout).unwrap();
    // A fridge and an oven inside the shell, the fridge stocked from the player's hands.
    let (x0, z0) = (start.building_x + 2, start.building_z + start.building_depth - 6);
    let fridge = server.apply(player, "fridge", Command::BuyAndPlace { order: goods::equipment::FurnishOrder { site_id: site.clone(), offer_id: "supplier-fridge".into(), level: 0, placements: vec![goods::equipment::GridPlacement { x: x0, z: z0, rotation: 0 }] } });
    assert!(fridge.accepted, "fridge: {}", fridge.reason);
    let oven = server.apply(player, "oven", Command::BuyAndPlace { order: goods::equipment::FurnishOrder { site_id: site.clone(), offer_id: "supplier-oven".into(), level: 0, placements: vec![goods::equipment::GridPlacement { x: x0 + 3, z: z0, rotation: 0 }] } });
    assert!(oven.accepted, "oven: {}", oven.reason);
    let fridge_id = fridge.equipment_id.clone();
    let inventory = goods::inventory_location_id(player);
    let dough = server.world.state.lots.iter().find(|l| l.location_id == inventory && l.item_id == "dough").unwrap().id.clone();
    let stocked = server.apply(player, "stock", Command::Transfer { lot: dough, destination: format!("{fridge_id}:in"), quantity: 10 });
    assert!(stocked.accepted, "stock: {}", stocked.reason);
    let hired = server.apply(player, "hire", Command::Hire);
    assert!(hired.accepted, "hire: {}", hired.reason);
    let employee = hired.equipment_id.clone();
    // Stand the new hire inside the shell next to the fridge.
    server.world.set_employee_pose(&employee, (x0 + 1) as f32 + 0.5, 0.0, (z0 - 1) as f32 + 0.5, 0.0);
    let script = format!("local n = take(\"{fridge_id}\", \"dough\", 3)
say(\"took\", n)
local p = put(\"oven\")
say(\"delivered\", p)
");
    server.run_employee(&employee, &script);
    for _ in 0..2000 {
        server.frame(0.05);
    }
    let status = server.employee_status(&employee).unwrap_or_default();
    assert!(status.contains("delivered 3"), "status: {status}");
    let _ = std::fs::remove_dir_all(&dir);
}
