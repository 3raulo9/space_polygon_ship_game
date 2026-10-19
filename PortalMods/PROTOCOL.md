# THE RIFT — bridge protocol, version 1

Two mods, one wire. `UnrenderedPortal` (inside UNRENDERED) and `unrendered_portal`
(a Fabric mod inside Minecraft 1.21.1) never touch each other's code, or their own
game's code. Everything one game knows about the other crosses this protocol.

## The idea: a passthrough, not a re-skin

Neither game draws an imitation of the other. **Each game captures what its own
renderer actually draws and streams it to the other.** The other game replays it
unchanged, around a player who stays entirely themselves:

* A **Minecraft player in UNRENDERED** is still a Minecraft client. They keep the
  hotbar, hearts, hunger, inventory, crafting, every item and every mod. The world
  around them is UNRENDERED's own frame: the triangles UNRENDERED's renderer emitted
  from where they stand, with its colours, flat shading and fog, plus its dithered
  sky. The city has invisible collision. Its enemies are invisible hitboxes that any
  Minecraft weapon (modded ones too) can hit. Its rounds hurt them.
* An **UNRENDERED pilot in Minecraft** is still an UNRENDERED craft. They keep its
  HUD, chassis, physics, weapons and inventory. The world around them is Minecraft's
  own geometry: the baked block-model quads with Minecraft's textures and light, and
  every mob, player and dropped item exactly as Minecraft's entity renderers drew it
  that tick. Minecraft players see the pilot's craft as UNRENDERED drew it.

Both games run at once and each simulates its own player. That's what makes it a
clash of two realities rather than a port.

## Finding each other

When a Minecraft world is **opened to LAN** (or a dedicated server with the mod
starts), the Fabric mod:

1. listens on TCP **25570** (or the next free port up to 25579; `bridgePort` in
   `config/unrendered_portal.properties` overrides it);
2. every 1.5 s sends a UDP datagram to multicast **224.0.2.61:4446** and broadcast
   **255.255.255.255:4446**:

   ```
   UNRENDERED-RIFT/1 {"port":25570,"world":"New World","host":"Steve","motd":"Steve - New World","mc":"1.21.1"}
   ```

UNRENDERED listens on UDP 4446. It also listens for vanilla Minecraft's own LAN
announcement (224.0.2.60:4445, `[MOTD]…[/MOTD][AD]port[/AD]`), so it can say
"there's a Minecraft world here, but it doesn't have the rift mod". A
`connect=host:port` line in `Mods/Portal/portal.cfg` skips discovery.

## Framing

TCP. Messages are UTF-8 JSON objects, one per line (`\n`), each with a string field
`t`. A **binary frame** is a JSON line `{"t":"bin","k":"<kind>","n":<bytes>,...}`
followed by exactly `n` raw bytes. Those bytes are **raw DEFLATE** (no zlib header).
Inflated, they are the little-endian payload described for that kind. Unknown types,
kinds and fields are ignored. Either side sends `{"t":"ping"}` every 2 s and drops
the link after 15 s of silence.

## Coordinates and scales

* **MC**: Minecraft block space, doubles. Yaw in degrees (0 = +Z, 90 = −X).
* **UNR**: UNRENDERED's torus plane. x and z wrap in [−200, 200), y is height above
  the grid, and heading is in radians (0 = +Z, π/2 = +X). `mcYaw = −deg(heading)`.
* The rift dimension `unrendered_portal:unrendered` maps 1:1 onto UNR: MC
  `(x, 64 + y, z)` is UNR `(x, y, z)`. Its grid surface is MC y = 64. Walking past
  ±200 wraps you to the other side, as UNRENDERED does.
* For a pilot in Minecraft, UNRENDERED keeps its own local frame. The wire always
  carries MC coordinates for things in Minecraft.
* One block is one UNRENDERED metre.

| damage                       | factor |
|------------------------------|--------|
| MC weapon → UNR enemy        | × 0.15 |
| UNR enemy → MC player        | × 0.2  |
| UNR pilot's weapon → MC mob  | × 6.5  |
| MC mob → UNR pilot           | × 5    |

## Session

| dir | `t`       | fields | meaning |
|-----|-----------|--------|---------|
| U→M | `hello`   | `proto, name, chassis, version` | first line from UNRENDERED |
| M→U | `welcome` | `proto, world, host, mc, portal?` | accepted (`portal` as below, if one exists) |
| M→U | `refuse`  | `reason` | rejected; link closes |
| U→M | `join`    | — | JOIN WORLDS was pressed in UNRENDERED |
| M→U | `portal`  | `x, y, z, axis, dim` | the Minecraft portal. `x,y,z` = block of the bottom-left water cell, `axis` = `"x"`/`"z"` (the plane's long axis), `dim` = dimension id |
| M→U | `chat`    | `text` | a line for UNRENDERED's notice feed |
| any | `ping`    | — | keep-alive |
| any | `bye`     | `reason?` | closing |

Minecraft side: when `join` arrives, every player in the world gets a clickable chat
line **`[JOIN WORLDS]`**. Clicking it (or running `/rift join`) puts a glowstone
portal, with water inside, in front of that player. Any player can also build one the
Aether way: a glowstone frame (inside 2×3 up to 21×21), then pour a water bucket into
it.

## A pilot in Minecraft (U = UNRENDERED, M = Minecraft)

| dir | `t` / kind | fields | meaning |
|-----|-----------|--------|---------|
| U→M | `enter`   | `name, chassis, hp, max` | the pilot crossed UNRENDERED's portal |
| M→U | `palette` | `entries[]` | block states (all of them up front, more later) |
| M→U | bin `region` | `ox,oy,oz,sx,sy,sz` | the terrain. Payload: `sx·sy·sz` × u16 palette index, then `sx·sy·sz` × u8 light (`sky<<4 \| block`). Index `(y·sz + z)·sx + x`, relative to `(ox,oy,oz)` |
| M→U | `spawn`   | `x, y, z, yaw` | where the pilot stands, just outside the portal (MC degrees) |
| U→M | `me`      | `x, y, z, yaw, pitch, hp, max, moving, chassis` | 20 Hz pilot transform (MC coords and MC degrees) |
| U→M | bin `craft` | — | 10 Hz: the pilot's craft as UNRENDERED draws it, in a **draw list** (below), relative to the pilot's feet and already turned to its heading (MC and UNR share axis orientation, so it is only translated) |
| M→U | bin `ents` | `time` | 20 Hz: every entity near the pilot, as Minecraft rendered it (below) |
| M→U | `blocks`  | `set[[x,y,z,p,light]...]` | block changes inside the region |
| U→M | `tex`     | `ids[]` | texture request |
| M→U | `texdata` | `id, png, frametime` | base64 PNG (null if unknown). Animated textures are vertical strips, frames `frametime` ticks long |
| U→M | `hit`     | `id, dmg` | a pilot's round struck entity `id` (MC HP) |
| U→M | `break`   | `x, y, z` | a pilot's round struck a block |
| U→M | `pickup`  | `id` | the pilot rolled over item entity `id` |
| M→U | `got`     | `stack, item, name, count, tex, attack, food` | it's theirs. `stack` is opaque (Minecraft's own item codec) and comes back unchanged |
| U→M | `drop`    | `stack, x, y, z` | the pilot dropped an item back into Minecraft |
| M→U | `hurt`    | `amount, by` | Minecraft hurt the pilot (MC HP) |
| U→M | `leave`   | — | the pilot went home, died, or quit |

**Palette entry**: `{ "i", "id", "solid", "liquid": "water"|"lava"|null,
"emit", "collide", "top", "portal", "quads": [ { "tex", "cull", "tint": [r,g,b]|null, "shade", "v":
[x,y,z,u,v, ×4] } ] }`. `solid` means an opaque full cube, which culls neighbours. `collide` says the state has a collision shape, and `top` is that shape's highest point in block units (1 full, 0.5 slab, 1.5 fence). `portal` marks the rift's own portal block.
`cull` is −1 or a direction index (0 down, 1 up, 2 north/−Z, 3 south/+Z, 4 west/−X,
5 east/+X). The quad is skipped when the neighbour that way is `solid`, or is the
same liquid. Vertices are in block space 0..1, UVs local to the texture's first
frame. `tex` is a resource id, e.g. `minecraft:textures/block/stone.png`.

**`ents` payload**: `u16 count`, then per entity:
`i32 id, f32 x, y, z, f32 w, h, f32 hp, max, u8 flags (1 hostile, 2 player, 4 item,
8 pickable), str name, u16 layers`, then per layer: `str tex, u32 quads`, then per
quad 4 × (`f32 x,y,z` relative to the entity position, `f32 u,v`, `u8 r,g,b,a`).
`str` is u16 length + UTF-8. Brightness from light is already multiplied into the
colour.

## A Minecraft player in UNRENDERED

| dir | `t` / kind | fields | meaning |
|-----|-----------|--------|---------|
| M→U | `arrive`  | `uuid, name` | a Minecraft player stepped through the portal |
| U→M | `city`    | `hash, size, structures[]` | UNRENDERED's skyline, for collision |
| U→M | `place`   | `uuid, x, z, yaw, portal: {x, z, facing}` | where they stand (UNR coords, `yaw` an UNR heading in radians), and where UNRENDERED's portal stands. Minecraft builds a real glowstone portal there in the rift dimension, the way home |
| M→U | bin `avatar` | `uuid` | ~10 Hz: the visitor as Minecraft renders them (skin, armour, held item), as ONE entity record in the `ents` layout, relative to their feet. UNRENDERED draws exactly this |
| M→U | `visitor` | `uuid, name, x, y, z, yaw, pitch, hp, max, swing, held` | 20 Hz, UNR coords (y = height above the grid). `yaw` is an UNR heading and `pitch` is in radians, positive up |
| U→M | bin `scene` | `uuid` | 10 Hz: UNRENDERED's world as seen from that visitor's eye (below) |
| U→M | `field`   | `ents[]` | 10 Hz hostiles near the visitors: `{id, x, y, z, r, h, hp, max}` (UNR) |
| M→U | `strike`  | `id, dmg, uuid` | a Minecraft player hit an UNRENDERED enemy (MC HP) |
| U→M | `harm`    | `uuid, amount, by, eject?` | UNRENDERED hit the visitor (MC HP). `eject: true` sends them home: the world they were in ended |
| U→M | `give`    | `uuid, item, count` | the visitor collected salvage (`scrap_metal`, `battery`…) |
| M→U | `mine`    | `uuid, x, y, z` | the visitor broke a piece of the city (UNR coords) |
| U→M | `fell`    | `i` | structure `i` came down; clear its collision |
| M→U | `depart`  | `uuid` | the visitor went home |

Structure: `{ "i", "h", "legs": [[x, z, r], ...] }`. Each leg is a solid circle,
`h` metres tall.

**`scene` payload**: the sky, `rgba top, mid, horizon, fog` (4 × u8 each), then
`f32 murk, lx, ly, lz, sunAlt, sunAz, moonAlt, moonAz, night`, then `f32 eyeX, eyeY,
eyeZ`, then a **draw list**.

**Draw list** (`scene` and `craft`): `u32 draws`, then per draw: `u8 mode` (1
lines, 4 triangles, 7 quads), `u32 verts`, then `verts` × (`f32 x,y,z`,
`u8 r,g,b,a`). These are UNRENDERED's own vertices, already shaded and fogged.

## One world each, shared (protocol 1, second pass)

The rift dimension is UNRENDERED's world, as seen by Minecraft. The pilot's stand-in
world is the Minecraft overworld around the portal, as seen by UNRENDERED. Each game
simulates its own things wherever they are; the other game sees them and can touch them.

| dir | `t` / kind | fields | meaning |
|-----|-----------|--------|---------|
| U→M | `seats`   | `list[{seat, name, x, y, z, yaw, hp, max, r, h}]` | 20 Hz: UNRENDERED's own players in its world (UNR coords). Minecraft keeps an invisible stand-in for each in the rift dimension, which mobs target and anything can hit |
| M→U | `hurt`    | `amount, by, seat` | with `seat`: something in the rift dimension hit that UNRENDERED player (MC HP) |
| M→U | bin `rents` | — | 20 Hz: every entity in the rift dimension within 96 blocks of a visitor or an UNRENDERED seat, other than the rift's own stand-ins and the visitors themselves: mobs that walked through the portal, dropped items, arrows, block entities in motion. Same record layout as `ents`, but **x, y, z are UNR coordinates** (MC y − 64). UNRENDERED draws them and its rounds hit them (`hit` takes any entity id) |
| M→U | `built`   | `set[[x,y,z,p]...], palette[]` | blocks Minecraft players placed (or removed: `p` = 0) in the rift dimension, anything that isn't the rift's own collision. UNR coords: y = MC y − 64. `palette` holds new entries in the pilot palette format, index 0 is air. Sent in full (`full: true`, replacing what UNRENDERED holds) on `welcome` and when a visitor arrives, then as changes |
| M→U | `use`     | `uuid, item` | a visitor used an UNRENDERED relic in the rift dimension whose effect is UNRENDERED's (`crab_core`): UNRENDERED throws its own from their seat |
| U→M | `cut`     | `i, boxes[[x, y, z, r]...]` | a building lost the pieces around these points (UNR coords). Clear its collision blocks within each `r` |
| U→M | bin `model` | `kind` | a draw list of an UNRENDERED enemy's body at the origin, facing +Z, feet on 0. Sent once per kind, before the first `cross` of that kind |
| U→M | `cross`   | `kind, hp, max` | an UNRENDERED enemy (`hunter`, `elite`, `soldier`) walked into UNRENDERED's portal. It comes out of the Minecraft portal and fights there as itself. Drawn with its `model`, killed by Minecraft weapons, drops UNRENDERED salvage as relics |
| M→U | `return`  | `kind, hp, max` | such an enemy walked back into the Minecraft portal; UNRENDERED takes it back |

Mobs, animals and dropped items go through either portal like a nether portal. A mob
walking into the overworld portal comes out of the rift dimension's portal (in
UNRENDERED's world), and the other way round.

`mine` is also sent when an explosion or any other means breaks the city's collision.
Minecraft drops UNRENDERED salvage relics for mined city blocks (mostly `scrap_metal`,
sometimes `dense_alloy` or `copper_wire`).
