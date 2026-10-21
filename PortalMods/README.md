# THE RIFT

Two mods, one in each game, that tear a glowstone-and-water portal between UNRENDERED
and Minecraft 1.21.1. It's a parody of the Aether portal. Neither mod changes either
game: each is one file dropped into that game's mods folder.

It's a passthrough, not a re-skin. Each game captures what its **own** renderer
actually draws and streams it to the other, and each player stays entirely themselves:

| | sees | keeps |
|-|------|-------|
| **Minecraft player in UNRENDERED** | UNRENDERED's own frame, drawn by UNRENDERED from where they stand: its city, grid, sky, fog, hunters and rounds | Minecraft's HUD, hotbar, hearts, hunger, inventory, crafting, every item and every mod |
| **UNRENDERED pilot in Minecraft** | Minecraft's own blocks, textures and light, and every mob, player and dropped item as Minecraft's renderer drew it that tick | their craft: chassis, HUD, physics, weapons, pack |

## Install

| game | file | where |
|------|------|-------|
| UNRENDERED | `glowstone-rift.dll` | `<UNRENDERED>/Mods/` (beside `swords.json`). SETTINGS > MODS lists it as THE RIFT (CODE) |
| Minecraft 1.21.1 | `unrendered_portal-1.0.0.jar` + Fabric API | `.minecraft/mods/` |

CI builds both. Download them from the latest green run of `rift-unrendered` and
`rift-fabric` in this repository's Actions tab (artifacts
`glowstone-rift-unrendered` and `unrendered_portal-fabric-1.21.1`).

## Play

1. In Minecraft, open a world and **Open to LAN**.
2. In UNRENDERED, start a solo run. A banner reads **MINECRAFT: <world> IS OPEN — PRESS B
   TO JOIN WORLDS**. Press **B**: a glowstone portal with water inside opens in front of
   you. Minecraft's chat offers **[JOIN WORLDS]**; clicking it opens the same portal in
   front of that player. A Minecraft player can also build one by hand: a glowstone
   frame, then a water bucket poured inside.
   With no Minecraft world around, **B** still opens the portal as a test. The water is
   just still.
3. **Walk through.**
   * As a pilot in Minecraft, you walk its hills under your own chassis' physics. Your
     rounds hurt mobs and break blocks. Roll over dropped items to take them.
     Minecraft's mobs hunt you. Walk back into the water to go home.
   * As a Minecraft player in UNRENDERED, the hunters, soldiers and bosses take hits from
     any weapon, modded ones too. Its rounds hurt you. Salvage you walk over becomes
     relic items you can craft UNRENDERED's recipes with in a crafting table. Mine the
     towers and enough of a tower brings it down. Walk back into the glowstone to go
     home.
4. Items cross as themselves, both ways.
   * What a pilot picks up in Minecraft goes into UNRENDERED's own pack (**F**), drawn
     with Minecraft's own sprite and stacking as high as Minecraft stacks it. The panel's
     own moves work on it. Right-click eats food, which mends your hull, or takes a sword
     in hand as a SOLDIER; any mod's sword is rebuilt pixel by pixel from its own texture.
     Shift+right-click throws one out. In Minecraft it lands at your feet; at home it lands
     in the rift. Iron, copper, gunpowder and UNRENDERED's own relics become that salvage.
     The pack is UNRENDERED's, so it lasts as long as the run does.
   * What a Minecraft player picks up in UNRENDERED is UNRENDERED's own item in their
     inventory: the very model UNRENDERED's pack shows, turning in the slot the way it
     does there, stacking as UNRENDERED stacks it, under UNRENDERED's name. Used, it does
     what it does in UNRENDERED. A CRAB CORE is thrown flat and straight at UNRENDERED's
     speed, then goes off as UNRENDERED's three-second burst of 32 tumbling lances, with
     UNRENDERED's own sounds. It hurts everything in reach except whoever threw it, and
     leaves the blocks alone. A MOONSTONE mends everything at once. Inside the rift,
     UNRENDERED throws the core itself, from your seat.

Chat commands in UNRENDERED: `{rift}`, `{rift join}`, `{rift leave}`,
`{rift connect 192.168.1.20:25570}`. Settings live in `Mods/rift/portal.cfg`. Keys,
your name, `connect=` for networks where LAN discovery is blocked, and whether your
rounds break blocks are all there.

## Pieces

* `PROTOCOL.md` is the contract between the two mods (LAN beacon, TCP, JSON lines and
  DEFLATEd binary frames).
* `unrendered/` is the UNRENDERED code mod, built on the game's `IGameMod` API.
* `fabric/` is the Fabric mod. See its README.
* The Dimension Hopper portal gun is a Minecraft mod of its own, in
  [3raulo9/diminsion_hopper](https://github.com/3raulo9/diminsion_hopper). With both installed,
  its dial lists UNRENDERED as a realm (the rift offers it through `HopperRealms`).
* `tools/fake_minecraft.py` plays the Minecraft side for developing and testing
  UNRENDERED's half without Minecraft. It writes what a visitor would see to
  `scene.png`.

## Limits

* Solo runs only on the UNRENDERED side, and one UNRENDERED per Minecraft world.
  Swapping a world under a networked match would desync it.
* A pilot sees a slice of Minecraft about 97 blocks across around the portal. Fog
  closes in before its edge.
* Captured mobs and players need a Minecraft **client** to render them, so open the
  world from the game, not a dedicated server. On a dedicated server they arrive as
  plain boxes and blocks as flat colours.
* Several Minecraft client paths (entity capture, scene replay, sky) compiled cleanly
  and the server side was smoke-tested in CI, but they haven't been played yet.
