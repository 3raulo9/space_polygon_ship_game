# DIMENSION HOPPER (UNRENDERED)

A code mod of its own: `dimension-hopper.dll`, dropped into UNRENDERED's `Mods` folder.

* **H** brings up the gun and its dial: every world it can reach. **I/K** (or **1-9**) move,
  **J** chooses, **H** puts it away.
* **J** with the dial down fires: a green bolt, and a swirling door where it lands. Walk in.
* On its own it hops between UNRENDERED's five worlds. The craft is carried whole (hull,
  shields, reserve, magazine, lives, the whole pack) the way the game's own arches carry it,
  and comes out at the same spot on the next world with the door behind it.
* With the glowstone rift installed too, the dial also lists the Minecraft worlds the rift can
  hear. One that has not answered yet says **ESTABLISH A CLEAR CONNECTION WITH THIS REALM**;
  J reaches for it (JOIN WORLDS). Once the connection is clear, the gun opens onto it, and the
  craft comes home out of the same green door.
* Refuses: in a shared (networked) match; while somebody from another realm is standing in
  UNRENDERED, who would be left behind; while you are across in another realm (come home
  first).

It depends on nothing but the game. The rift is found by name (`HopperRealms`).

`dotnet build` puts the dll in `out/`. `HOPPER_TEST=1` makes it play itself once for the
end-to-end run.
