package net.unrendered.rift.net;

import net.minecraft.network.RegistryByteBuf;
import net.minecraft.network.codec.PacketCodec;
import net.minecraft.network.codec.PacketCodecs;
import net.minecraft.network.packet.CustomPayload;
import net.unrendered.rift.RiftMod;

/**
 * UNRENDERED's frame for one visitor, forwarded unchanged (still deflated) from the
 * bridge to that player's client, which replays it around them.
 */
public record ScenePayload(byte[] data) implements CustomPayload {
    public static final Id<ScenePayload> ID = new Id<>(RiftMod.id("scene"));
    public static final PacketCodec<RegistryByteBuf, ScenePayload> CODEC =
        PacketCodec.tuple(PacketCodecs.BYTE_ARRAY, ScenePayload::data, ScenePayload::new);

    @Override
    public Id<? extends CustomPayload> getId() {
        return ID;
    }
}
