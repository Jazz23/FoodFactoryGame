// The game UI (one module: superui 0.4 does not load cross-module imports yet), after the "Main Menu" and "Food Factory UI"
// designs. The game sends one frame per surface (menu, settings, hud, tooltip, panel) when it changes; the UI sends back
// intents (the same action strings the game’s screens handle), typed field values, and whether a text field has focus.
// Layers, back to front: the HUD, an open panel, the menus, settings.
import { createSignal, For, Index, onMount, render, Show, untrack } from "supersolid";


// ---- state ----
// The game’s state as the UI last received it (one signal per surface the game sends: menu, settings, hud, tooltip, panel)
// and the commands back: an intent (the same action strings the game’s screens handle), a typed field value, and whether
// a text field has focus so the game stops reading movement keys.
const [menu, setMenu] = createSignal(null);
const [settings, setSettings] = createSignal(null);
const [hud, setHud] = createSignal(null);
const [tooltip, setTooltip] = createSignal(null);
const [panel, setPanel] = createSignal(null);

function intent(action) {
  bevy.send("intent", { action: action });
}

function field(id, value) {
  bevy.send("field", { id: id, value: String(value) });
}

function typing(on) {
  bevy.send("typing", { on: on });
}

function listen() {
  bevy.on("menu", (m) => setMenu(m));
  bevy.on("settings", (s) => setSettings(s));
  bevy.on("hud", (h) => setHud(h));
  bevy.on("tooltip", (t) => setTooltip(t));
  bevy.on("panel", (p) => setPanel(p.panel));
  bevy.send("ready", {});
}

/// A design icon (a PNG rasterized from its SVG, in the colour the design draws it).
function Icon(props) {
  return <img class={"icon" + (props.size || 16) + " none"} src={"icons/" + props.name + "-" + (props.color || "ece8e1") + ".png"} />;
}

/// The menu backdrop: the design’s scene colour, its 64 px grid and (on the main menu) the vignette behind the column.
function Scene(props) {
  const columns = [];
  for (let x = 0; x < 3200; x += 64) columns.push(x);
  const rows = [];
  for (let y = 0; y < 1800; y += 64) rows.push(y);
  return (
    <div class="fill scene">
      {columns.map((x) => <div class="grid-v" style={"left: " + x + "px"} />)}
      {rows.map((y) => <div class="grid-h" style={"top: " + y + "px"} />)}
      {props.vignette ? <div class="fill vignette" /> : <div class="fill shade" />}
    </div>
  );
}

/// A text input that reports each edit as a field and tells the game while it has focus. Its starting value is read once,
/// so the game echoing typed text back never moves the caret.
function Field(props) {
  return (
    <input
      type="text"
      class={props.class || "field"}
      value={untrack(() => props.value)}
      placeholder={props.placeholder || ""}
      onInput={(e) => field(props.id, e.target.value)}
      onFocus={() => typing(true)}
      onBlur={() => typing(false)}
    />
  );
}


// ---- hud ----
// The in-world HUD (the "Food Factory UI" design’s HUD board): the site card, cash with its rate and the clock, alerts, the
// crosshair with the aimed object’s prompt, the hotbar and the key hints, plus the tooltip beside a free pointer. None of it
// takes clicks, so the world stays clickable under it.
// HUD lists are keyed by position (<Index>): each frame is fresh JSON, and rows update in place.
function Alert(props) {
  const a = props.alert;
  return (
    <Show when={a().kind === "warn"} fallback={
      <div class="row hud-card dot-card">
        <div class="dot none" style={"background-color: " + a().colour} />
        <div class="grow" style="font-size: 13px">{a().title}</div>
        <Show when={a().time}><div class="mono" style="font-size: 12px; color: #FF9A6B">{a().time}</div></Show>
      </div>
    }>
      <div class="row hud-card warn-card">
        <div style="margin-top: 1px"><Icon name="warning" size={18} color="f2a33a" /></div>
        <div class="col grow" style="row-gap: 3px">
          <div class="m" style="font-size: 14px">{a().title}</div>
          <Show when={a().detail}><div style="font-size: 12px; color: #A8A49B">{a().detail}</div></Show>
        </div>
      </div>
    </Show>
  );
}

function Hotbar() {
  return (
    <div class="row hotbar">
      <Index each={hud().hotbar}>
        {(s) => (
          <div class={"row hslot" + (s().icon ? "" : " empty") + (s().selected ? " on" : "")}>
            <Show when={s().icon}><img src={"/" + s().icon} style="width: 40px; height: 40px" /></Show>
            <div class={"hslot-key" + (s().selected ? " on" : "")}>{s().key}</div>
            <Show when={s().count}><div class="hslot-count">{s().count}</div></Show>
          </div>
        )}
      </Index>
    </div>
  );
}

function Hud() {
  // A centred panel takes the screen (the hotbar stays); build mode’s catalog docks right and leaves the HUD in view.
  const covered = () => panel() && !panel().right;
  return (
    <Show when={hud() && hud().visible}>
      <div class="fill">
        <Show when={hud().loading}>
          <div class="hud-card loading">{hud().loading}</div>
        </Show>
        <Show when={!covered() && hud().site}>
          <div class="row hud-card site-card">
            <div class="row site-tile none"><Icon name="home" size={18} /></div>
            <div class="col" style="row-gap: 2px">
              <div class="m" style="font-size: 15px">{hud().site}</div>
              <div style="font-size: 12px; color: #A8A49B">{hud().sub}</div>
            </div>
          </div>
        </Show>
        <Show when={!covered() && !(panel() && panel().right) && hud().cash}>
          <div class="col right-stack">
            <div class="col hud-card cash-card">
              <div class="row" style="justify-content: space-between">
                <div class="caps-12">CASH</div>
                <div class="mono" style={"font-size: 12px; color: " + (hud().rate_up ? "#6FCB86" : "#FF9A6B")}>{hud().rate}</div>
              </div>
              <div class="mono-b" style="font-size: 30px; letter-spacing: -0.3px">{hud().cash}</div>
              <div class="divider" style="margin: 4px 0" />
              <div class="row" style="column-gap: 8px; font-size: 13px; color: #CFCBC3">
                <Icon name="clock" size={14} color="a8a49b" />
                <div>{hud().day}</div>
                <div style="color: #6A675F">·</div>
                <div class="mono">{hud().clock}</div>
              </div>
            </div>
            <Index each={hud().alerts}>{(a) => <Alert alert={a} />}</Index>
          </div>
        </Show>
        <Show when={hud().crosshair}>
          <div class="crosshair" />
        </Show>
        <Show when={hud().prompt && !panel()}>
          <div class="row prompt">
            <div class="row prompt-key">{hud().prompt_key}</div>
            <div style="font-size: 14px">{hud().prompt}</div>
          </div>
        </Show>
        <Show when={hud().hint}>
          <div class="hint-line">{hud().hint}</div>
        </Show>
        <Hotbar />
        <Show when={!covered()}>
          <div class="row hints key-hints">
            <Index each={hud().keys}>{(k) => <div class="row hint"><div class="keycap">{k().key}</div>{k().label}</div>}</Index>
          </div>
        </Show>
        <Show when={tooltip() && tooltip().text}>
          <div class="tooltip" style={"left: " + tooltip().x + "px; top: " + tooltip().y + "px"}>{tooltip().text}</div>
        </Show>
      </div>
    </Show>
  );
}


// ---- panel ----
// The open in-world panel (inventory, a machine, an employee’s tasks, the phone, build mode’s catalog), drawn from the
// game’s panel model in the "Food Factory UI" design’s style: a dark card over a dimmed world, item slots in a ten-wide
// grid, icon cards for things to buy or place, and plain or accent buttons. Every button sends its action; buttons also
// carry it as their id (`act:<action>`) so the game can read right clicks and slot drags on them.
const isCaps = (text) => text.length > 0 && text.length < 60 && text !== text.toLowerCase() && text === text.toUpperCase();

/// "Bread\n8 spoiled" → name, count, spoiled.
function slotParts(label) {
  const lines = label.split("\n");
  const rest = (lines[1] || "").trim();
  return { name: lines[0], count: rest.split(" ")[0], spoiled: rest.indexOf("spoiled") >= 0 };
}

// Entries arrive as accessors (rows are keyed by position), so a live update changes text in place instead of rebuilding.
function Slot(props) {
  const e = props.entry;
  const parts = () => slotParts(e().label);
  const empty = () => !e().icon && !e().label;
  return (
    <button
      id={"act:" + e().action}
      class={"row slot" + (empty() ? " empty" : "") + (parts().spoiled ? " spoiled" : "") + (e().selected ? " on" : "")}
      onClick={() => intent(e().action)}
    >
      <Show when={e().icon}><img src={"/" + e().icon} style="width: 36px; height: 36px" /></Show>
      <Show when={!e().icon && parts().name}><div style="font-size: 9px; color: #CFCBC3; text-align: center">{parts().name}</div></Show>
      <Show when={parts().count}><div class="slot-count">{parts().count}</div></Show>
      <Show when={parts().spoiled}><div class="slot-tag">spoiled</div></Show>
    </button>
  );
}

function accentAction(action) {
  return ["p:hire", "p:buy-property", "p:task-run", "p:run|", "b:add-floor", "p:teleport"].some((p) => action.startsWith(p));
}

function dangerAction(action) {
  return ["p:fire", "p:sell|"].some((p) => action.startsWith(p));
}

function Button(props) {
  const e = props.entry;
  const lines = () => e().label.split("\n");
  return (
    <Show when={e().icon || e().image} fallback={
      <button
        id={"act:" + e().action}
        class={"row btn m" + (e().selected ? " on" : "") + (accentAction(e().action) ? " accent" : "") + (dangerAction(e().action) ? " danger" : "")}
        onClick={() => intent(e().action)}
      >{e().label}</button>
    }>
      <button id={"act:" + e().action} class={"col card-btn" + (e().selected ? " on" : "")} onClick={() => intent(e().action)}>
        <Show when={e().icon} fallback={<div id={"rt-" + e().image} style="width: 40px; height: 40px" />}>
          <img src={"/" + e().icon} style="width: 40px; height: 40px" />
        </Show>
        <div class="card-name">{lines()[0]}</div>
        <Show when={lines()[1]}><div class="card-sub">{lines()[1]}</div></Show>
      </button>
    </Show>
  );
}

/// What an entry draws as; a position whose kind changes is rebuilt, otherwise it updates in place.
function kind(e) {
  if (e.image && e.size) return "picture";
  if (e.row && e.row.length) return e.row.every((c) => c.slot) ? "slots" : "row";
  if (e.input !== undefined) return e.multiline ? "area" : "input";
  if (e.action) return e.slot ? "slot" : "button";
  return "label";
}

function Entry(props) {
  const e = props.entry;
  return (
    <div class="col" style="row-gap: 0">
      <Show when={kind(e()) === "picture"}>
        <div id={"rt-" + e().image} class="none" style={"width: " + e().size + "px; height: " + e().size + "px"} />
      </Show>
      <Show when={kind(e()) === "slots"}>
        <div class="slots"><Index each={e().row}>{(c) => <Slot entry={c} />}</Index></div>
      </Show>
      <Show when={kind(e()) === "row"}>
        <div class="row p-row"><Index each={e().row}>{(c) => <Entry entry={c} />}</Index></div>
      </Show>
      <Show when={kind(e()) === "area"}>
        <textarea class="p-area" rows="14" value={untrack(() => e().input)} onInput={(ev) => field(e().id, ev.target.value)} onFocus={() => typing(true)} onBlur={() => typing(false)} />
      </Show>
      <Show when={kind(e()) === "input"}>
        <Field id={e().id} value={e().input} class="p-input" />
      </Show>
      <Show when={kind(e()) === "slot"}>
        <Slot entry={e} />
      </Show>
      <Show when={kind(e()) === "button"}>
        <Button entry={e} />
      </Show>
      <Show when={kind(e()) === "label" && e().label}>
        <div class={isCaps(e().label) ? "p-caps" : "p-label"}>{e().label}</div>
      </Show>
    </div>
  );
}

function Panel() {
  return (
    <Show when={panel()}>
      <Show when={!panel().right} fallback={
        <div class="col panel dock" onClick={() => {}}>
          <PanelInner />
        </div>
      }>
        <div class="fill dim" />
        <div class="row panel-wrap">
          <div class={"col panel" + (panel().wide ? " wide" : "")} onClick={() => {}}>
            <PanelInner closable={true} />
          </div>
        </div>
      </Show>
    </Show>
  );
}

function PanelInner(props) {
  return (
    <div class="col grow" style="row-gap: 14px; min-height: 0">
      <div class="row" style="column-gap: 12px; align-items: flex-start">
        <div class="col grow" style="row-gap: 2px">
          <div class="m" style="font-size: 17px">{panel().heading}</div>
          <Show when={panel().status}><div style="font-size: 12px; color: #A8A49B">{panel().status}</div></Show>
        </div>
        <Show when={props.closable}>
          <button class="row close none" onClick={() => intent("p:close")}><Icon name="close" size={14} /></button>
        </Show>
      </div>
      <div class="col panel-body">
        <Index each={panel().entries}>{(e) => <Entry entry={e} />}</Index>
      </div>
      <Show when={panel().footer}>
        <div class="p-footer">{panel().footer}</div>
      </Show>
    </div>
  );
}


// ---- menu ----
// The main menu, world select and connect pages (the "Main Menu" design) and the pause menu in the same style. Which page
// shows is the UI’s own state; starting a world, joining or quitting goes to the game as the menu’s intents.
const [page, setPage] = createSignal("main");

function NavButton(props) {
  return (
    <button class={"row mbtn" + (props.quiet ? " quiet" : "")} onClick={props.onClick}>
      <Icon name={props.icon} size={20} color="a8a49b" />
      <div class={"grow m"} style={"font-size: " + (props.quiet ? 16 : 17) + "px"}>{props.label}</div>
      <Show when={!props.quiet}>
        <Icon name="chevron-right" size={16} color="6a675f" />
      </Show>
    </button>
  );
}

function Main() {
  return (
    <div class="fill">
      <Scene vignette={true} />
      <div class="col menu-col">
        <div class="col" style="align-items: center; row-gap: 12px">
          <div class="row" style="column-gap: 12px">
            <div class="row logo-tile"><Icon name="factory" size={22} color="f2a33a" /></div>
            <div class="eyebrow mono">FACTORY · RESTAURANT</div>
          </div>
          <div class="title b">Food Factory</div>
        </div>
        <div class="col nav">
          <Show when={menu().resume}>
            <button class="row resume" onClick={() => intent("menu:resume")}>
              <div class="row resume-dot none"><Icon name="play" size={18} color="f2a33a" /></div>
              <div class="col grow" style="row-gap: 3px">
                <div class="m" style="font-size: 22px">Resume</div>
                <div style="font-size: 13px; color: #3B2A12">{menu().resume.sub}</div>
              </div>
              <div class="mono" style="font-size: 12px; color: #3B2A12; align-self: flex-start; margin-top: 4px">{menu().resume.ago}</div>
            </button>
          </Show>
          <NavButton icon="home" label="Select World" onClick={() => setPage("worlds")} />
          <NavButton icon="server" label="Connect to Server" onClick={() => setPage("connect")} />
          <NavButton icon="sliders" label="Settings" onClick={() => intent("s:open")} />
          <div class="divider" style="margin: 6px 0" />
          <NavButton icon="quit" label="Quit" quiet={true} onClick={() => intent("menu:quit")} />
          <Show when={menu().status}>
            <div style="font-size: 13px; color: #FFC170; margin-top: 6px; text-align: center">{menu().status}</div>
          </Show>
        </div>
      </div>
      <div class="row hints" style="position: absolute; left: 50%; bottom: 28px; transform: translateX(-50%)">
        <div class="row hint"><div class="keycap">Tab</div>Navigate</div>
        <div class="row hint"><div class="keycap">Enter</div>Select</div>
      </div>
      <div class="mono" style="position: absolute; right: 28px; bottom: 28px; font-size: 12px; color: #8F8B83">{menu().version}</div>
    </div>
  );
}

function Header(props) {
  return (
    <div class="row" style="column-gap: 20px">
      <button class="row ghost back" onClick={() => setPage("main")}>
        <Icon name="chevron-left" size={16} />
        <div class="m">Back</div>
      </button>
      <div class="page-title m grow">{props.title}</div>
      {props.children}
    </div>
  );
}

const MODES = [
  { id: "solo", label: "Singleplayer", desc: "Only you. Nobody can join." },
  { id: "host", label: "Host for others", desc: "Others join this world at your host address." },
];

function Worlds() {
  // The picked world’s name, or "" for a new one.
  const [picked, setPicked] = createSignal(null);
  const [mode, setMode] = createSignal("solo");
  const [open, setOpen] = createSignal(false);
  const selected = () => {
    const name = picked();
    const list = menu().worlds;
    if (name === "") return null;
    return list.find((w) => w.name === name) || list[0] || null;
  };
  const isNew = () => picked() === "" || menu().worlds.length === 0;
  const current = () => MODES.find((m) => m.id === mode());
  const play = () => {
    const hosting = mode() === "host";
    if (isNew()) intent(hosting ? "menu:host-new" : "menu:new");
    else intent((hosting ? "menu:host:" : "menu:load:") + selected().name);
  };
  return (
    <div class="fill">
      <Scene />
      <div class="col page">
        <Header title="Select world">
          <button class="row ghost" onClick={() => setPicked("")}>
            <Icon name="plus" size={16} />
            <div class="m">New world</div>
          </button>
        </Header>
        <div class="row grow" style="column-gap: 24px; align-items: stretch; min-height: 0">
          <div class="col glass grow" style="overflow: hidden">
            <div class="row world-head caps-12">
              <div class="grow">WORLD</div>
              <div class="c-day">DAY</div>
              <div class="c-day">CASH</div>
              <div class="row c-last">LAST PLAYED</div>
            </div>
            <div class="col grow" style="padding: 8px; row-gap: 4px; overflow-y: scroll">
              <Show when={menu().worlds.length === 0}>
                <div style="padding: 24px 12px; font-size: 14px; color: #A8A49B">No saved worlds yet. Make one with New world.</div>
              </Show>
              <Index each={menu().worlds}>
                {(w) => (
                  <button class={"row world-row" + (!isNew() && selected() && selected().name === w().name ? " on" : "")} onClick={() => setPicked(w().name)}>
                    <div class="row grow" style="column-gap: 14px">
                      <div class="row tile44 none"><Icon name="home" size={20} color="cfcbc3" /></div>
                      <div class="col" style="row-gap: 3px">
                        <div class="m" style="font-size: 16px">{w().name}</div>
                        <div style="font-size: 12px; color: #A8A49B">{w().sub}</div>
                      </div>
                    </div>
                    <div class="mono c-day" style="font-size: 14px; color: #CFCBC3">{w().day}</div>
                    <div class="mono c-day" style="font-size: 14px; color: #CFCBC3">{w().cash}</div>
                    <div class="row c-last" style="font-size: 13px; color: #A8A49B">{w().last}</div>
                  </button>
                )}
              </Index>
            </div>
          </div>
          <div class="col glass aside none">
            <div class="col thumb">
              <Icon name="factory" size={22} color="f2a33a" />
              <div class="mono" style="font-size: 12px; letter-spacing: 1.9px; color: #6A675F">{isNew() ? "NEW WORLD" : "SAVED WORLD"}</div>
            </div>
            <Show when={!isNew()} fallback={
              <div class="col" style="row-gap: 12px">
                <div class="m" style="font-size: 24px">New world</div>
                <div class="col" style="row-gap: 6px">
                  <div class="caps-12">NAME</div>
                  <Field id="world-name" value={menu().new_name} />
                </div>
                <div class="col" style="row-gap: 6px">
                  <div class="caps-12">SEED (OPTIONAL)</div>
                  <Field id="seed" value="" placeholder="Random" />
                </div>
              </div>
            }>
              <div class="col" style="row-gap: 6px">
                <div class="m" style="font-size: 24px">{selected().name}</div>
                <div style="font-size: 13px; color: #A8A49B">{selected().sub + " · Day " + selected().day + " · " + selected().cash}</div>
              </div>
            </Show>
            <div class="divider" />
            <div class="col" style="row-gap: 8px; position: relative">
              <div class="caps-12">PLAY AS</div>
              <button class={"row dd" + (open() ? " open" : "")} onClick={() => setOpen(!open())}>
                <div class="row dd-tile none">
                  <Show when={mode() === "solo"} fallback={<Icon name="users" size={18} color="f2a33a" />}>
                    <Icon name="user" size={18} />
                  </Show>
                </div>
                <div class="col grow" style="row-gap: 2px">
                  <div class="m" style="font-size: 15px">{current().label}</div>
                  <div style="font-size: 12px; color: #A8A49B">{current().desc}</div>
                </div>
                <Icon name="chevron-down" size={16} color="a8a49b" />
              </button>
              <Show when={open()}>
                <div class="col dd-list">
                  <For each={MODES}>
                    {(m) => (
                      <button class={"row opt" + (mode() === m.id ? " on" : "")} onClick={() => { setMode(m.id); setOpen(false); }}>
                        <div class="col grow" style="row-gap: 2px">
                          <div class="m" style="font-size: 14px">{m.label}</div>
                          <div style="font-size: 12px; color: #A8A49B">{m.desc}</div>
                        </div>
                        <Show when={mode() === m.id}><Icon name="check" size={16} color="f2a33a" /></Show>
                      </button>
                    )}
                  </For>
                </div>
              </Show>
            </div>
            <Show when={mode() === "host"}>
              <div class="col" style="row-gap: 6px">
                <div class="caps-12">HOST ADDRESS</div>
                <Field id="host-address" value={menu().host_address} />
              </div>
            </Show>
            <div class="grow" />
            <Show when={menu().status}>
              <div style="font-size: 13px; color: #FFC170">{menu().status}</div>
            </Show>
            <button class="row cta" onClick={play}>
              <Icon name="play" size={16} color="16171a" />
              <div class="m">{mode() === "host" ? "Host and play" : isNew() ? "Create and play" : "Play"}</div>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function Connect() {
  return (
    <div class="fill">
      <Scene />
      <div class="col page">
        <Header title="Connect to server" />
        <div class="row" style="justify-content: center; padding-top: 80px">
          <div class="col glass" style="width: 520px; padding: 24px; row-gap: 18px">
            <div class="row" style="column-gap: 14px">
              <div class="row tile44 none"><Icon name="server" size={20} color="a8a49b" /></div>
              <div class="col" style="row-gap: 3px">
                <div class="m" style="font-size: 18px">Join a hosted world</div>
                <div style="font-size: 12px; color: #A8A49B">The host shares its address and port.</div>
              </div>
            </div>
            <div class="col" style="row-gap: 6px">
              <div class="caps-12">SERVER ADDRESS</div>
              <Field id="address" value={menu().address} />
            </div>
            <Show when={menu().status}>
              <div style="font-size: 13px; color: #FFC170">{menu().status}</div>
            </Show>
            <button class="row cta" onClick={() => intent("menu:join")}>
              <Icon name="play" size={16} color="16171a" />
              <div class="m">Join</div>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function Paused() {
  return (
    <div class="fill">
      <div class="fill vignette" />
      <div class="fill shade" />
      <div class="col menu-col">
        <div class="col" style="align-items: center; row-gap: 10px">
          <div class="title b" style="font-size: 48px">Paused</div>
          <div style="font-size: 14px; color: #A8A49B">{menu().session}</div>
        </div>
        <div class="col nav">
          <button class="row resume" onClick={() => intent("menu:resume")}>
            <div class="row resume-dot none"><Icon name="play" size={18} color="f2a33a" /></div>
            <div class="m grow" style="font-size: 22px">Resume</div>
          </button>
          <NavButton icon="sliders" label="Settings" onClick={() => intent("s:open")} />
          <NavButton icon="home" label="Save and leave" onClick={() => { setPage("main"); intent("menu:leave"); }} />
          <div class="divider" style="margin: 6px 0" />
          <NavButton icon="quit" label="Quit" quiet={true} onClick={() => intent("menu:quit")} />
        </div>
      </div>
    </div>
  );
}

function Menus() {
  return (
    <Show when={menu() && menu().screen !== "hidden"}>
      <Show when={menu().screen === "menu"} fallback={<Paused />}>
        <Show when={page() === "worlds"} fallback={
          <Show when={page() === "connect"} fallback={<Main />}>
            <Connect />
          </Show>
        }>
          <Worlds />
        </Show>
      </Show>
    </Show>
  );
}


// ---- settings ----
// The settings page (the "Main Menu" design’s Settings board): mouse sensitivity, audio, anti-aliasing and the player’s
// name on the left, the keybind table on the right. Clicking a key asks the game to listen for the next key press; values
// are saved by the game as they change.
function Slider(props) {
  return (
    <div class="col" style="row-gap: 10px">
      <div class="row" style="justify-content: space-between">
        <div class="m" style="font-size: 15px">{props.label}</div>
        <div class="mono-b" style="font-size: 16px; color: #F2A33A">{props.text}</div>
      </div>
      <input type="range" class="slider" min={props.min} max={props.max} step={props.step} value={props.value} onChange={(e) => intent("s:" + props.id + "-set|" + e.target.value)} />
      <Show when={props.marks}>
        <div class="row mono" style="justify-content: space-between; font-size: 11px; color: #8F8B83">
          {props.marks.map((m) => <div>{m}</div>)}
        </div>
      </Show>
    </div>
  );
}

const percent = (v) => Math.round(v * 100) + "%";

function Left() {
  const s = settings;
  return (
    <div class="col none" style="width: 340px; row-gap: 16px; overflow-y: scroll; padding-right: 4px">
      <div class="col glass section">
        <div class="caps">MOUSE</div>
        <Slider id="sensitivity" label="Sensitivity" text={s().sensitivity.toFixed(2) + "×"} min="0.1" max="3" step="0.05" value={s().sensitivity} marks={["0.10×", "1.00×", "3.00×"]} />
      </div>
      <div class="col glass section">
        <div class="caps">AUDIO</div>
        <Slider id="master" label="Master volume" text={percent(s().master)} min="0" max="1" step="0.05" value={s().master} />
        <Slider id="effects" label="Effects" text={percent(s().effects)} min="0" max="1" step="0.05" value={s().effects} />
        <Slider id="interface" label="Interface" text={percent(s().interface)} min="0" max="1" step="0.05" value={s().interface} />
        <Slider id="ambience" label="Music and ambience" text={percent(s().ambience)} min="0" max="1" step="0.05" value={s().ambience} />
      </div>
      <div class="col glass section">
        <div class="caps">GRAPHICS</div>
        <div class="col" style="row-gap: 10px">
          <div class="m" style="font-size: 15px">Anti-aliasing</div>
          <div class="row seg" style="align-self: flex-start">
            <For each={[["off", "Off"], ["fxaa", "FXAA"], ["msaa4", "MSAA 4×"]]}>
              {(o) => <button class={"row seg-btn m" + (s().anti_aliasing === o[0] ? " on" : "")} onClick={() => intent("s:aa|" + o[0])}>{o[1]}</button>}
            </For>
          </div>
        </div>
      </div>
      <div class="col glass section">
        <div class="caps">PROFILE</div>
        <div class="col" style="row-gap: 8px">
          <div class="m" style="font-size: 15px">Your name</div>
          <div class="row" style="column-gap: 8px">
            <div class="grow"><Field id="settings-name" value={s().name} /></div>
            <button class="row ghost" onClick={() => intent("s:name")}><div class="m">Save</div></button>
          </div>
        </div>
      </div>
      <div class="col section-soft">
        <div class="caps">REBINDING</div>
        <div style="font-size: 13px; line-height: 1.55; color: #CFCBC3">Click a key, then press the new key.</div>
        <div class="col" style="row-gap: 6px; font-size: 12px; color: #A8A49B">
          <div class="row" style="column-gap: 8px"><div class="tipcap">Esc</div>Cancel</div>
        </div>
      </div>
    </div>
  );
}

function Keys() {
  const s = settings;
  return (
    <div class="col glass grow" style="overflow: hidden">
      <div class="row keys-head">
        <div class="caps grow">KEYBINDS</div>
        <Show when={s().notice}>
          <div class="row" style="column-gap: 8px; font-size: 13px; color: #FFC170">
            <Icon name="warning" size={16} color="ffc170" />
            {s().notice}
          </div>
        </Show>
      </div>
      <div class="row keys-body grow" style="align-items: flex-start">
        <Index each={s().columns}>
          {(column) => (
            <div class="col grow" style="flex-basis: 0">
              <Index each={column()}>
                {(g) => (
                  <div class="col">
                    <div class="khead">
                      <div class="m" style="font-size: 13px; color: #ECE8E1">{g().name}</div>
                      <div style="text-align: center">Primary</div>
                      <div style="text-align: center">Alternate</div>
                    </div>
                    <Index each={g().rows}>
                      {(r) => (
                        <div class="krow">
                          <div style="font-size: 14px; color: #CFCBC3">{r().label}</div>
                          <Show when={!r().fixed} fallback={<div class="row cap-fixed">{r().key}</div>}>
                            <button class={"row cap" + (s().listening === r().action ? " listen" : "")} onClick={() => intent(s().listening === r().action ? "s:cancel" : "s:rebind|" + r().action)}>
                              {s().listening === r().action ? "Press…" : r().key}
                            </button>
                          </Show>
                          <Show when={r().fixed} fallback={<div class="row cap-fixed">{r().alternate}</div>}>
                            <div class="row cap-none">Fixed</div>
                          </Show>
                        </div>
                      )}
                    </Index>
                  </div>
                )}
              </Index>
            </div>
          )}
        </Index>
      </div>
    </div>
  );
}

function Settings() {
  return (
    <Show when={settings() && settings().open}>
      <div class="fill">
        <Scene />
        <div class="col page">
          <div class="row" style="column-gap: 20px">
            <button class="row ghost back" onClick={() => intent("s:back")}>
              <Icon name="chevron-left" size={16} />
              <div class="m">Back</div>
            </button>
            <div class="page-title m grow">Settings</div>
            <button class="row ghost" onClick={() => intent("s:defaults")}>
              <Icon name="reset" size={16} />
              <div class="m">Reset to defaults</div>
            </button>
            <button class="row primary" onClick={() => intent("s:back")}><div class="m">Done</div></button>
          </div>
          <div class="row grow" style="column-gap: 24px; align-items: stretch; min-height: 0">
            <Left />
            <Keys />
          </div>
        </div>
      </div>
    </Show>
  );
}


// ---- entry ----
function App() {
  onMount(() => listen());
  return (
    <div class="fill">
      <Hud />
      <Panel />
      <Menus />
      <Settings />
    </div>
  );
}

render(() => <App />, document.getElementById("root"));
