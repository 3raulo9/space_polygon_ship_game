# Dimension Hopper

A handheld portal gun for Minecraft 1.21.1 (Fabric), and a mod of its own: drop
`dimension-hopper-<version>.jar` into `mods/` beside Fabric API. It needs nothing else.

* **Dial** a destination: sneak + use with the gun in hand, or press **V** (rebindable). The
  dial lists every dimension the world has (the Overworld, the Nether, the End, and any a mod
  adds) and every realm another game opens to it. The choice stays on the gun.
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

`./gradlew build` in `fabric/`. The model and texture come from `tools/make_hopper.py`.
