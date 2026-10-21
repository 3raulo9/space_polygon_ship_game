# Dimension Hopper

Portal guns for Minecraft 1.21.1 (Fabric), and a mod of its own: drop
`dimension-hopper-<version>.jar` into `mods/` beside Fabric API. It needs nothing else.

Both guns are built like the reference: a dark-oak box chamber with iron brackets and bars
over the glowing charge, three stacked iron barrels with glowing tips, an iron console with
buttons and gauges, a PORTAL FUEL canister cabled to the chamber, and a wooden pistol grip.
The charge moves (a rolling surface, sparks rising), the tips breathe, and a shot kicks the
gun back with the tips flashing, the charge boiling and the barrel cluster turning a step.

## Making one

| Item | Recipe |
|---|---|
| Reinforced Ender Pearl | 9 ender pearls, filling the grid |
| Concentrated Teleportation Potion | brewing: Awkward Potion + Reinforced Ender Pearl |
| Bottle of Concentrated Unstable Portal Fluid | brewing: Concentrated Teleportation Potion + Phantom Membrane |
| Portal Gun Handle | button over redstone between planks, a plank under: ` B ` / `PRP` / ` P ` |
| Portal Chamber | the fluid bottle in the middle, glass panes on its sides, iron ingots in the corners |
| **Portal Gun** | nether star and chamber on top, the handle under the chamber: `NC` / ` H` |
| Monitor | iron ingots on top, glass pane / glowstone dust / glass pane, iron / redstone / iron |
| Dimensional Computer | monitor on top, comparator / redstone block / comparator, three iron ingots |
| **Dimension Hopper** | Portal Gun + Dimensional Computer (shapeless) |

**The concentrated potions are not for drinking.** Whoever drinks one is fine for two minutes.
Then they start jumping about at random like a chorus fruit, every twenty seconds and then
more often, and further each time, 8 blocks at first and 64 by four minutes. From four
minutes every jump tears: it hurts and leaves a red burst behind. Their limbs teleport away
one at a time (the left arm, the right arm, the left leg, the right leg), and others see
them go. At five minutes the rest of them goes too, and the death message is "was scattered
across space". Milk still saves you, if you drink it in time.

## The guns

* The **Portal Gun** has no screen. Use fires, and each shot rolls one of the world's other
  dimensions at random. Another game's realm is never rolled.
* The **Dimension Hopper** is the gun with a computer bolted on, and its screen:
* **Dial** a destination: sneak + use with the gun in hand, or press **V** (rebindable). The
  dial lists every dimension the world has (the Overworld, the Nether, the End, and any a mod
  adds) and every realm another game opens to it. For a dimension it also takes **X / Y / Z**
  coordinates (leave any blank for "wherever is natural"); the far end opens at the nearest
  safe spot to them. The choices stay on the gun.
* **Fire**: use. A green shot flies up to about 64 blocks; where it lands a swirling portal
  opens, standing on the floor facing you, out from a wall, or hanging from a ceiling.
  Its twin opens somewhere safe at the destination: the same place scaled the way the two
  dimensions scale (eight to one into the Nether), two blocks of air over solid ground that
  isn't lava, under the Nether's roof, on the End's own platform. Hopping within one dimension
  goes 64 blocks the way you fired. If nothing safe is near, a small platform is built.
* **Anything** that goes into a portal comes out of its twin: players, mobs, items, arrows.
  Each keeps its speed and comes out facing away from the far portal. A portal pair closes
  after a minute, or when the same gun fires again.
* A gun that has not been dialled goes from the Overworld to the Nether, and from anywhere
  else back to the Overworld.

## Other games' realms

A realm from another game is on the dial when that game's Minecraft mod offers one. Until it
is linked the dial says **ESTABLISH A CLEAR CONNECTION WITH THIS REALM**, and its button asks
the realm to connect, with the realm's own status underneath, refreshed every second. Firing
at a realm that is not clear is refused. Once it is clear, the portal opens here only, and a
player who walks in is handed to the realm.

THE RIFT (`PortalMods/fabric`) offers UNRENDERED this way. Establishing the connection opens
the world to LAN, so UNRENDERED can find it; the link is clear once UNRENDERED answers (press
B there).

### Offering a realm

No compile dependency on this mod is needed. Declare an entrypoint in your `fabric.mod.json`:

```json
"entrypoints": { "dimension_hopper:realms": ["com.example.MyRealms"] }
```

and implement `java.util.function.Supplier<java.util.List<java.util.Map<String, Object>>>`.
It is called on the server thread whenever the dial is drawn. Each map is one realm:

| key | type | |
|---|---|---|
| `id` | `String` | stable id |
| `name` | `String` | what the dial calls it |
| `game` | `String` | the game it belongs to |
| `clear` | `java.util.function.BooleanSupplier` | whether it is linked now |
| `status` | `java.util.function.Supplier<String>` | one line on how the connection is going |
| `connect` | `Runnable` | the dial's "Establish connection" |
| `enter` | `java.util.function.BiConsumer<ServerPlayerEntity, Vec3d>` | a player walked into a portal aimed at it, at that spot; move them there |
| `dimension` | `RegistryKey<World>` (optional) | the dimension the realm lives in, which the plain list then leaves out |

## Building

`./gradlew build` in `fabric/`. The models and textures (and their animations) come from
`tools/make_hopper.py`.
