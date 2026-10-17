// Two independent TCP clients exercise the actual transport/admission/replication path on an isolated SQLite world.
// This localhost check is not the required two-physical-machine/menu acceptance check.
use std::{sync::{Arc, mpsc::Receiver}, time::{Duration, Instant}};
use food_factory_bevy::{content::GameContent, net, protocol::{ClientMessage, Command, Pose, ServerMessage, PROTOCOL_VERSION}, server::{ServerHandle, ServerOptions, WorldServer}};

fn wait(rx: &Receiver<ServerMessage>, predicate: impl Fn(&ServerMessage) -> bool) -> ServerMessage {
    let deadline = Instant::now() + Duration::from_secs(10);
    loop {
        let message = rx.recv_timeout(deadline.saturating_duration_since(Instant::now())).expect("TCP response before deadline");
        if let ServerMessage::Rejected { reason } = &message { panic!("TCP rejection: {reason}"); }
        if predicate(&message) { return message; }
    }
}

#[test]
fn two_tcp_clients_replicate_poses_and_durable_purchase_without_duplicate_charge() {
    let path = std::env::temp_dir().join(format!("ff-bevy-tcp-{}-{}", std::process::id(), std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    let options = ServerOptions { save_directory: path.clone(), world_id: "tcp-check".into(), seed_text: "20260927".into(), only_secret: None };
    let content = Arc::new(GameContent::bundled().unwrap());
    let mut server = ServerHandle::start(options.clone(), content.clone()).unwrap();
    let listener = net::Listener::start("127.0.0.1:0", server.inbound.clone(), server.connection_ids()).unwrap();
    let address = listener.address.to_string();
    let (a_tx, a_rx) = net::connect(&address); let (b_tx, b_rx) = net::connect(&address);
    for (tx, name, secret) in [(&a_tx, "TCP A", "tcp-secret-aaaaaaaaaaaa"), (&b_tx, "TCP B", "tcp-secret-bbbbbbbbbbbb")] {
        tx.send(ClientMessage::Hello { version: PROTOCOL_VERSION, name: name.into(), secret: secret.into() }).unwrap();
    }
    let ServerMessage::Welcome { player: a, site, spawn, .. } = wait(&a_rx, |m| matches!(m, ServerMessage::Welcome { .. })) else { unreachable!() };
    assert!(spawn.x.is_finite() && spawn.y.is_finite() && spawn.z.is_finite() && spawn.yaw.is_finite(), "welcome poses must survive JSON transport");
    let ServerMessage::Welcome { player: b, .. } = wait(&b_rx, |m| matches!(m, ServerMessage::Welcome { .. })) else { unreachable!() };
    assert_ne!(a, b);
    let pose = Pose { x: spawn.x + 1.0, y: spawn.y, z: spawn.z, yaw: 90.0 };
    a_tx.send(ClientMessage::Pose { pose }).unwrap();
    wait(&b_rx, |m| matches!(m, ServerMessage::Avatars { avatars } if avatars.iter().any(|v| v.player == a && v.pose == pose)));
    let purchase = ClientMessage::Command { request: "tcp-buy-once".into(), command: Command::Buy { site: site.clone(), offer: "supplier-oven".into() } };
    a_tx.send(purchase.clone()).unwrap();
    let ServerMessage::Outcome { outcome, .. } = wait(&a_rx, |m| matches!(m, ServerMessage::Outcome { request, .. } if request == "tcp-buy-once")) else { unreachable!() };
    assert!(outcome.accepted, "{}", outcome.reason);
    let equipment = outcome.equipment_id.clone();
    a_tx.send(purchase).unwrap();
    let ServerMessage::Outcome { outcome: replay, .. } = wait(&a_rx, |m| matches!(m, ServerMessage::Outcome { request, .. } if request == "tcp-buy-once")) else { unreachable!() };
    assert_eq!(replay, outcome);
    wait(&b_rx, |m| matches!(m, ServerMessage::Baseline { view, .. } if view.equipment.iter().any(|e| e.id == equipment)));
    a_tx.send(ClientMessage::Leave).unwrap(); b_tx.send(ClientMessage::Leave).unwrap();
    drop(a_tx); drop(b_tx); drop(listener); server.stop();
    let reopened = WorldServer::open(&options, content).unwrap();
    assert_eq!(reopened.world.state.equipment.iter().filter(|e| e.id == equipment).count(), 1);
    assert_eq!(reopened.world.state.outcomes.iter().filter(|o| o.request_id == "tcp-buy-once").count(), 1);
    drop(reopened); std::fs::remove_dir_all(path).unwrap();
}
