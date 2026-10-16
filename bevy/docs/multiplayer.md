# Bevy session runtime

## Implemented development adapter

`runtime::Runtime::host(store, credentials, address)` binds the network listener on a worker thread. The caller explicitly supplies the SQLite-backed `Store`, secret-to-actor credential map and bind address; this adapter does not choose seed, identity or product authentication policy. The host advances `Store::tick()` once per second even with no subscribers. Accepted TCP connections run independently and are terminated when the runtime shuts down.

`runtime::Client::connect(address, secret, site)` returns immediately and performs connect, authentication, subscription, baseline reads and command exchanges on a worker thread. `command(request_id, command)` is non-blocking and returns an error when its bounded queue is full or closed. `poll()` drains available `ClientEvent::{SiteView,Error,Outcome}` values without waiting for I/O. The client requests a fresh authorized baseline every 500 ms because the current protocol has no server-push updates. Event delivery is bounded; when the event queue is full, the adapter drops the newly arriving event rather than blocking the network worker. Shut down explicitly to join workers; dropping either handle also shuts it down.

The adapter uses the existing newline-delimited `network` protocol and its current authentication/subscription semantics. It is development transport only (no encryption or registration). It does not implement remote avatars, movement replication, presence, or Unity parity. Client baseline polling is a polling workaround, not a replication contract. The Bevy frontend must only call these APIs from its normal systems and must never perform socket I/O itself.

## Headless two-process smoke

`tools/network_smoke.ps1` launches a driver twice in separate OS processes, with distinct explicit database paths. The driver interface is intentionally external to this adapter: pass a headless executable implementing `--smoke-host` and `--smoke-client` as described by `-DriverHelp`. The host must create/open the supplied database using its own explicit test fixture, configure the supplied test credential/site, and print `HOST_READY` after binding. The client must connect with that credential, wait for a baseline, and print `CLIENT_BASELINE`. No application database or gameplay authoring is used by the script. The actual test seed/world fixture belongs to the driver, not this runtime layer.

Example invocation after a driver is wired:

```powershell
./tools/network_smoke.ps1 -Driver .\target\debug\food_factory_bevy.exe -DatabaseDirectory .\target\smoke
```

The current adapter and protocol do not expose an event stream or remote presence. A smoke proves only process-separated connection, authenticated site subscription and baseline delivery; command correctness and tick progression require additional explicit driver assertions.
