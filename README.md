# CommNext Redux

![CommNext Redux](Assets/CommNextRedux/Copied/assets/images/icon.png)

CommNext Redux reworks the KSP 2 Redux comms network. It decides which craft are in contact, blocks
links that pass behind a moon or a planet, gives relays an ElectricCharge bill, and tags every
transmitter with one or more radio bands that decide who can talk to whom. It is a port of
[CommNext](https://github.com/Kerbalight/CommNext) by leonardfactory, and it keeps that mod's UI, its
range-sphere mesh and its part balance.

The mod ships as version `0.7.5`, and its descriptor declares KSP 2 Redux `0.2.9.0` or newer. This
guide describes the snapshot current at the time of writing, `0.2.9.0.104521`.

## Compatibility

| | |
|---|---|
| Game | KSP 2 Redux **`0.2.9.0.104521`** (snapshot `26w36c`) — the pin. Newer Redux is untested and unsupported |
| Unity | `6000.5.8f1` (the Redux `0.2.9.0` player generation) |
| Dependency | SpaceWarp2 `2.0.0` or newer (declared in `swinfo.json`) |

## Lineage

This is a port of the pre-Redux KSP 2 mod
**[Kerbalight/CommNext](https://github.com/Kerbalight/CommNext)** (MIT; the original copyright
`Copyright (c) 2024 leonardfactory` is retained in [`LICENSE`](LICENSE)), retargeted to KSP 2 Redux
`0.2.9.0.104521`. Third-party components and their notices are listed in
[`THIRD-PARTY-NOTICES`](THIRD-PARTY-NOTICES).

## What it changes

The game's own network treats every antenna as a plain circle: if two craft are inside each other's
range, they are connected. CommNext replaces that with three checks, and the middle one is the new
part:

- a link is dropped when a celestial body sits between the two ends;
- a link is dropped when the two ends have no band in common, or when the band they share cannot
  reach the distance;
- a relay that cannot pay its own ElectricCharge draw stops relaying.

On top of the engine you get a set of map tools: connection lines coloured by the band they use,
range spheres for relays and other nodes, a toolbar for switching both on and off, and a vessel
report that lists the active vessel's links one by one.

Past the KSC's own reach, the network is carried by relays. Your vessel stays in contact through a
chain of relay craft, as long as every hop in the chain passes those three tests.

## Installing

1. Copy the `CommNextRedux` folder into your game's `mods` directory, next to your other mods
   (`Kerbal Space Program 2/mods/CommNextRedux/`). The folder name matters on Linux, where it is
   lower-case `mods`.
2. Start the game and load a save. Nothing needs enabling in game, and no new parts are added.
3. The mod writes its settings to `mods/CommNextRedux/CommNextRedux-config.json`. You can edit the
   file by hand or use the in-game page under `Settings -> Mods -> CommNext Redux`. Both write the
   same values.
4. Open the map (`M`). The map is where everything this mod draws lives.

The deployed folder holds `CommNextRedux.dll`, the `assets` folder with the UI bundle, the
`localizations` and `patches` folders, `swinfo.json`, and the `CommNextRedux-config.json` the mod
writes on first run.

## First look in the map

The toolbar appears in the top right of the map view: a small bar labelled `COMMNEXT` with three icon
buttons for connection lines, range rulers, and the vessel report. It has no close button and comes
and goes with the map. Drag it by its background if it is in your way.

Hover any button to see a tooltip naming the state it is in. The first button cycles the connection
lines between `All active connections`, `Active vessel path` and `Disabled`. The second does the same
for the range rulers, between `Only relays`, `All` and `Disabled`. The third opens the vessel report.

What you should see on a working install, with the shipped defaults:

- the network's links redrawn as coloured lines, teal by default, one line per link;
- a soft gold sphere around each relay, sized to that relay's own range;
- the game's own flat green CommNet lines gone, so the mod's colours are the only ones on the map.

## Bands

### What a band is here

KSP 2 itself has no band concept. A stock `ConnectionGraphNode` carries an active flag, a control
source flag and a range, and nothing else, so a band is something this mod adds: a label on a
transmitter that says which part of the spectrum its signal sits in. Two craft may only link when
they have at least one band in common and can reach each other on it.

If that sounds like the real world, that is the idea, but the bands here are the mod's own set. There
are five, they are fixed, and they carry no frequencies anywhere in the code.

### The five bands

| Code | Name on screen | Default line colour |
|---|---|---|
| `X` | X Band | `#2CC8C6` (teal) |
| `S` | S Band | `#1791F7` (blue) |
| `K` | K Band | `#7063FF` (indigo) |
| `Ka` | Ka Band | `#D626EB` (magenta) |
| `V` | V Band | `#2BD994` (green) |

`X` is the default band, and every transmitter the mod patches starts on it. That is why a fresh
install behaves like an ordinary range network: everything shares `X`, so nothing is ever removed
for having no band in common. The band set only starts to matter once you change a part's band or
turn on omni mode.

### How a part gets a band

A part's bands live in two modules, both added to existing stock parts by the mod's Lua patches. No
new parts are introduced.

`Signal Modulator` is the module that owns the bands, and it comes in three kinds. A `Mono-band`
part carries one band, the `Band` row. A `Dual-band` part carries two, `Band` and `Secondary Band`,
and a `Secondary Band` of `None` makes it behave like a mono-band part. An `Omni-band` part adds the
`Omni-band` toggle: turn it on and the transmitter goes out on all five bands at once, and the `Band`
and `Secondary Band` rows disappear because they no longer apply. Turn the toggle off and the part
falls back to those two rows.

The kind is fixed per part and set by the patches; what you choose on the part is the band or bands
it should use.

`Signal Relay` is the module that lets a part forward other craft's traffic. It has one `Enable
relay` toggle, on by default, and it is what makes a dish a relay rather than an endpoint. Only the
four relay antennas the patches name carry it, and each has its own ElectricCharge rate. A relay
that is switched off, or that cannot pay, is not a relay for the moment.

The two modules combine: a part can be a relay, a banded transmitter, or both, and the four relay
antennas are both.

### What two craft need to link

The engine tests each pair of nodes and builds a link only when all three of these hold:

1. both ends have enough ElectricCharge for their own relays (only matters when `Relays require
   power` is on, and never in a save with the InfinitePower difficulty option);
2. the two ends' band sets share at least one band;
3. on one of the shared bands, both ends' range on that band covers the distance.

The band a link ends up using is the first shared band (in the order `X`, `S`, `K`, `Ka`, `V`) that
passes the range test. That band decides the line's colour on the map and the icon on the report
row, so the colour you see is the band the link actually runs on.

Two things follow from this that are easy to miss. A shared band that is too short does not fall back
to another range: the pair is simply not linked unless a different shared band reaches. And a craft
that has bands in common with nobody nearby is off the network even when the range circles overlap.

Two safety rules stop a band mismatch from cutting a craft off by accident. A part that carries a
transmitter but no modulator at all is treated as having every band, and a node the network pass
cannot read (a craft still loading, for example) is treated the same way. The KSC's node gets its
full reach on every band it offers, so the `KSC range` setting is never eaten by a band test.

### Where you see bands

- On the map, as the colour of each connection line.
- In the vessel report, as a small coloured band icon on each connection row, and as a band table at
  the bottom of the window showing every band the vessel offers and its range on each.
- On the part, in its Part Action Menu, as the `Band` and `Secondary Band` rows and the `Omni-band`
  toggle.
- In the part's info window, as a `Modulation kind` line reading `Mono-band`, `Dual-band` or
  `Omni-band`.

## Which stock parts get which bands

The mod's two Lua patches rework the stock antennas, so the tables below are the whole mapping. These
are the game's part IDs, the same names the patches use.

The five antennas that are not relays:

| Part | Range | Modulator kind |
|---|---|---|
| `antenna_0v_16` | 500 km | Mono-band |
| `antenna_0v_16s` | 500 km | Mono-band |
| `antenna_1v_dish_hg55` | 15 Gm | Dual-band |
| `antenna_1v_parabolic_dts-m1` | 2 Gm | Dual-band |
| `antenna_1v_dish_88-88` | 100 Gm | Omni-band |

The four relays, with the ElectricCharge they draw while relaying:

| Part | Range | Modulator kind | Relay cost |
|---|---|---|---|
| `antenna_1v_dish_hg5` | 5 Mm | Dual-band | 0.2 EC/s |
| `antenna_0v_dish_ra-2` | 2 Gm | Omni-band | 0.5 EC/s |
| `antenna_0v_dish_ra-15` | 15 Gm | Omni-band | 1.0 EC/s |
| `antenna_1v_dish_ra-100` | 100 Gm | Omni-band | 2.0 EC/s |

Every other stock part with a transmitter is covered by two blanket rules: command pods, probe cores
and cockpits get their built-in antenna range reset to 5 km, and any remaining transmitter gets a
Mono-band modulator on `X`. The end state is that all 37 stock transmitters carry a modulator, and
none of them carried the mod's modules before the patches ran.

The ranges above are the mod's own rebalance of the stock parts, and they are the numbers the band
tests use. A bare command part on its own reaches about 5 km, so anything that leaves the pad needs
a real antenna on board.

## Map lines and range rulers

The lines button cycles three drawing modes for the connection lines:

- `All active connections`: one line for every link in the network's tree.
- `Active vessel path`: only the chain of links between your current vessel and the control source,
  which is the KSC on a normal save.
- `Disabled`: nothing is drawn, and lines already on screen are removed.

Each line is coloured by the band its link uses. A link with no band on it gets one of two fallback
colours instead: `Relay hop color` when both ends are relays, `Other links color` for everything
else. You normally see those fallbacks when the master switch is off and the map is drawing the
game's own tree.

The rulers button does the same for range spheres:

- `Only relays`: a sphere on every node that carries an enabled relay.
- `All`: a sphere on every node with a range.
- `Disabled`: no spheres, and the ones already drawn are destroyed.

A sphere is centred on its marker and its radius is that node's own maximum range, so you can read
off at a glance how far a relay or a ground station actually reaches. The sphere is tinted while the
node has at least one live link and draws nothing at all while it does not, which means a relay that
has dropped out of the network also loses its sphere.

The toolbar's position is remembered. Move it, leave the map, and the next map view opens with it
where you left it. If you quit the game from inside the map, a move made in that session is lost.

## The vessel comms report

The third toolbar button opens the report, and pressing it again closes it. It opens just under the
toolbar, it always describes your active vessel, and it closes by itself when you leave the map. Drag
it by its title bar, resize it by its lower-right corner, or close it with its `X`.

The header shows the vessel's name, its own maximum range as `Range {0}`, and a `Focus` button that
centres the vessel's marker on the map.

Below that sits the connection list, one row per link the vessel is an endpoint of:

- the other end's name, with `KSC Ground Control` for the space centre;
- a direction tag: `Out` when this vessel is the link's source and feeds it, `In` when the vessel
  receives it;
- icons for the other end's type (relay or antenna), for a power problem, for the band, and for the
  signal strength. Hover them for the band's name, the signal percentage, and whether the missing
  power is yours or theirs;
- the distance, as `Distance {0}`.

Click a row to focus the other end on the map. Hovering a row reveals a `Control` button that takes
control of the other vessel, subject to the game's own rule that you may not leave a vessel when
doing so is not allowed. The KSC row has no `Control` button, since there is nothing to take control
of.

Two dropdowns and an arrow sit above the list. The first filters the rows by direction: `All`,
`Outbound`, `Inbound`. The second sorts them by `Distance`, `Band`, `Signal Strength` or `Name`, and
the arrow flips between ascending and descending. The defaults are `All`, `Distance`, descending.

At the bottom of the window is the vessel's own band table: one row per band the vessel offers, with
the band's name and the vessel's range on that band. The range shown there is the same number the
band test uses, so it is the number that decides whether a link on that band can reach.

Signal strength comes from the game's own curve over range. It is a percentage of the two ends'
usable range, and it uses the shorter of the two antennas, so a huge dish talking to a small antenna
reads lower than the dish's own range suggests.

## Part controls

Open a patched part's Part Action Menu and you will find:

- `Signal Relay` with an `Enable relay` toggle, on the four relay antennas;
- `Signal Modulator` with `Modulation kind` and, depending on the kind, `Omni-band`, `Band` and
  `Secondary Band`.

The part's info window adds a `Modulation kind` line with `Mono-band`, `Dual-band` or `Omni-band`,
and the relay antennas add `This antenna behaves as a relay`. Band selections are part state, so they
are saved with the craft and travel with your save. There is no global band setting to look for; each
part carries its own.

## Settings

All settings live under `Settings -> Mods -> CommNext Redux`, in four groups. Twenty keys ship in
total, and the config file is generated from them.

### Network

| Key | Default | What it does |
|---|---|---|
| `Enable CommNext network` | `true` | The master switch. Off means the game's own connection graph is used unchanged, as if the mod were not installed. Takes effect on the next rebuild, which is about every 3 seconds. |
| `Best path mode` | `NearestRelay` | How the best path is scored. `NearestRelay` prefers the path with the shortest hop between nodes, and it is what ships. `ShortestKSC` prefers the lowest accumulated cost to the KSC, and it is the metric the game itself uses. |
| `KSC range` | `G2` | The KSC's reach: `G2` is 2 Gm, `G10` is 10 Gm, `G50` is 50 Gm. The mod also moves the CommNet origin onto the space centre, so the range is measured from the launch site instead of the centre of Kerbin. |
| `Occlusion radius` | `0.98` | How much of a body's radius blocks a link. `0` means no occlusion, `1` means the full radius. The default shaves the last 2% so terrain and atmosphere do not block a link that looks clear. Slider, 0 to 1. |
| `Relays require power` | `true` | On, an enabled relay that cannot pay its ElectricCharge draw stops relaying. Off, relays are not charged at all and can never be starved. Takes effect on the next tick and the next rebuild. |

### Debug

| Key | Default | What it does |
|---|---|---|
| `Network probe` | `false` | Logs one structural block per rebuild: node count, occlusion verdicts with the blocking body, the selected path, and a cross-check against the game's own path computation. It makes the log large, so it ships off. A config that already stored `true` keeps it on. |
| `Prints in Player.log the time it takes to compute the network` | `false` | Logs how long each pass took, at most once every few seconds. It also decides whether network nodes carry vessel names, which costs a lookup per node. |

### Map

| Key | Default | What it does |
|---|---|---|
| `Rulers mode` | `Relays` | Which range spheres are drawn: `None`, `Relays` for every node with an enabled relay, or `All` for every node with a range. Live, and the toolbar's middle button writes the same key. |
| `Connections mode` | `Lines` | Which connection lines are drawn: `None`, `Lines` for every edge of the tree, or `Active` for the active vessel's path to the control source. Live, and the toolbar's first button writes the same key. |
| `Hide the game's own CommNet lines` | `true` | Hides the game's flat green line drawer, which otherwise paints over the mod's coloured lines. It never writes your game setting; see the next section. |
| `Map toolbar X` | `-1` | Written by the mod. The toolbar's horizontal position in panel pixels. `-1` means you have not moved it. Both halves are saved together when you leave the map. |
| `Map toolbar Y` | `-1` | The vertical half of the same pair. |

### Lines

| Key | Default | What it does |
|---|---|---|
| `X band color` | empty | A colour for the X band's lines. Empty means the built-in `#2CC8C6`. |
| `S band color` | empty | Empty means the built-in `#1791F7`. |
| `K band color` | empty | Empty means the built-in `#7063FF`. |
| `Ka band color` | empty | Empty means the built-in `#D626EB`. |
| `V band color` | empty | Empty means the built-in `#2BD994`. |
| `Relay hop color` | empty | The colour of a relay-to-relay link that got no band. Empty means the built-in `#37DBD8`. |
| `Other links color` | empty | Every other link that got no band. Empty means the built-in `#36C358`. |
| `Line opacity` | `1.00` | A multiplier on the line colour's alpha. `1.00` is solid and `0` is invisible. It affects the connection lines only, not the range spheres. |

The colour keys accept `#RRGGBB`, `#RRGGBBAA` and the colour names Unity understands. A value that
cannot be read as a colour is reported in the log and the built-in colour is used instead. Empty is
not a colour; it means "not overridden", which is why the fields start blank. Changes to the colour
and opacity keys repaint the lines already on the map within about half a second.

## Living with the stock network

CommNext computes the connection graph the game's comms systems use, so the game's own idea of who
is in contact comes from this mod while it is enabled. With `Enable CommNext network` off, the game's
own routine runs and the mod only draws what it finds.

The game draws its own CommNet lines in flat green, and it draws them over the mod's lines. `Hide the
game's own CommNet lines` is on by default and stops the game's drawer, so the band colours are
visible. It does that by reporting the game's own value as off, and it never writes your saved
setting. One side effect to know about: while this key is on, the game's own `Show CommNet Lines`
toggle in `Settings -> Gameplay` reads as off and flipping it has no visible effect. The value it
writes is still stored, and turning the mod's key off restores stock behaviour exactly, toggle and
all.

With the master switch off, the mod still draws the network, but there is no band on any link, so
every line takes the fallback colours instead of band colours.

Uninstalling needs nothing special. The mod adds no parts and leaves the game's own CommNet setting
untouched. The only state it keeps in your save is what you set on its parts, the band choices and the
relay toggles, and that data is ignored once the mod is gone.

## Known limitations

- The map toolbar does not stop the map's mouse camera controls. A right-drag that starts on the
  toolbar still rotates and pans the map, and the wheel still zooms it. The vessel report does stop
  those controls while the pointer is on it. This is the one open defect in the port's record and it
  is left as it is by choice.
- The hover tooltip cannot capture the mouse, by design. It is a full-screen click-through overlay,
  so it only appears while you hover an element that owns one, and it hides when you leave the map.
- A drag that starts on the vessel report stops moving the camera as soon as the pointer leaves the
  report's rectangle. The input guard covers the window, nothing more.
- The toolbar's position is saved when you leave the map. Quit the game from inside the map and a
  move made in that session is lost.
- The report cannot show you which body blocks a link, or list links that were rejected. The engine
  counts those verdicts; it does not publish them per link. A blocked link simply is not on the map.
- This build has only been run on a development install that carries other mods. It has not been
  tested on an installation that carries only Redux.

## Credits

- **leonardfactory** for [CommNext](https://github.com/Kerbalight/CommNext): the design, the engine,
  the UI markup, the images and the range-sphere mesh. This port follows the original except where
  the pinned API makes that impossible. The port's detailed divergence record is maintained
  privately and is not shipped.
- **[UitkForKsp2](https://github.com/UitkForKsp2/UitkForKsp2.Unity)** for the game's UI Toolkit
  integration, and **munix** for `uitkforksp2.controls`.
- The **KSP 2 Redux** team for the loader and the API this port targets.

## Licence

MIT. The original copyright (`Copyright (c) 2024 leonardfactory`) is kept in
[`LICENSE`](LICENSE), together with the port's own line. [`THIRD-PARTY-NOTICES`](THIRD-PARTY-NOTICES)
lists what this mod redistributes and what it only links against.

## Building it yourself

This repository is the Redux `0.2.9.0` Unity mod project itself. Build the C# with `Tools/build.sh`
(it compiles against the managed assemblies of your installed KSP 2 Redux — `$KSP2`, or the default
Steam path — and stages the deployable); then build the UI bundle with
`PLAYER_UNITY=6000.5.8f1 Tools/build-ui-bundle.sh` (the script's own default is already `6000.5.8f1`;
`UNITY=/path/to/Editor/Unity` overrides the editor location).

Note the ordering hazard: **the C# build re-stages the deployable's `assets/` tree, so build the
bundle AFTER the code.** The deployable is `CommNextRedux.dll` + `swinfo.json` +
`assets/bundles/commnextredux_ui.bundle` (plus `localizations/` and `patches/`); in this repository
the payload files live under `Assets/CommNextRedux/Copied/`.

## Repository layout

| Path | What it is |
|---|---|
| `Assets/CommNextRedux/Code/` | The C#: network engine, map rendering, UI controllers, config |
| `Assets/CommNextRedux/UI/` | UXML and USS for the three windows, shipped inside the bundle |
| `Assets/CommNextRedux/Copied/` | The deployable payload: UI bundle, localizations, Lua patches, icon |
| `Assets/CommNextRedux/swinfo.json` | The descriptor Redux's loader reads |
| `Tools/` | The build chain, `build.sh` for the DLL and `build-ui-bundle.sh` for the bundle |
| `Packages/manifest.json` | The Unity package set the project builds against |
