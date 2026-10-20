package net.unrendered.rift;

import net.minecraft.registry.Registries;
import net.minecraft.registry.Registry;
import net.minecraft.sound.SoundEvent;

/** UNRENDERED's own sounds, synthesised by UNRENDERED (PortalMods/tools/export-items.sh). */
public final class RiftSounds {
    public static final SoundEvent CRAB_CORE_THROW = SoundEvent.of(RiftMod.id("crab_core_throw"));
    public static final SoundEvent CRAB_CORE_BLAST = SoundEvent.of(RiftMod.id("crab_core_blast"));

    private RiftSounds() {}

    public static void register() {
        Registry.register(Registries.SOUND_EVENT, CRAB_CORE_THROW.getId(), CRAB_CORE_THROW);
        Registry.register(Registries.SOUND_EVENT, CRAB_CORE_BLAST.getId(), CRAB_CORE_BLAST);
    }
}
