# THE RIFT, Minecraft side (Fabric 1.21.1)

`unrendered_portal-1.0.0.jar` goes in your `mods/` folder next to Fabric API. Nothing
else is needed.

## Playing

1. Open a world and **Open to LAN**. The chat says the world is now visible to
   UNRENDERED.
2. In UNRENDERED, press **B** (JOIN WORLDS). Minecraft shows
   `[JOIN WORLDS]`. Click it, and a glowstone portal with water in it appears in front
   of you.
   Or build one yourself the Aether way: a glowstone frame (inside 2×3 to 21×21), then
   pour a water bucket into it.
3. Walk into the water. You arrive inside UNRENDERED's world as yourself: your hotbar,
   hearts, hunger, every item (modded ones too). UNRENDERED's city is solid, its
   hostiles take hits from any weapon, its rounds hurt, and salvage you walk over turns
   into items you can craft with here. Walk back into the glowstone portal to go home.
4. When an UNRENDERED pilot comes the other way, they come out of your portal as their
   own craft. Monsters hunt them, they can pick up your items, and their rounds break
   blocks (`allowBlockBreak=false` in `config/unrendered_portal.properties` turns that
   off).

`/rift join` builds a portal in front of you, and `/rift status` says what is linked.
`/rift home` takes you out of UNRENDERED's world the way you came in, with no portal needed.

Nobody is left standing in the rift with nothing drawing it: closing the world, closing it to
LAN, or losing UNRENDERED sends every visitor home, and somebody who logs in inside the rift
with UNRENDERED gone is sent home too. Where each visitor came from is kept with the world.

## Building

```
./gradlew build      # the jar lands in build/libs/
```

CI builds it on every push (`.github/workflows/rift-fabric.yml`).
`tools/make_textures.py` regenerates the item sprites.
