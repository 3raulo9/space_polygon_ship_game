package net.unrendered.rift.net;

import net.minecraft.network.RegistryByteBuf;
import net.minecraft.network.codec.PacketCodec;
import net.minecraft.network.codec.PacketCodecs;
import net.minecraft.network.packet.CustomPayload;
import net.unrendered.rift.RiftMod;

/** A pilot's craft as UNRENDERED drew it (a deflated draw list), for every client to show
 *  on the pilot entity {@code entityId}. */
public record CraftPayload(int entityId, byte[] data) implements CustomPayload {
    public static final Id<CraftPayload> ID = new Id<>(RiftMod.id("craft"));
    public static final PacketCodec<RegistryByteBuf, CraftPayload> CODEC = PacketCodec.tuple(
        PacketCodecs.VAR_INT, CraftPayload::entityId,
        PacketCodecs.BYTE_ARRAY, CraftPayload::data,
        CraftPayload::new);

    @Override
    public Id<? extends CustomPayload> getId() {
        return ID;
    }
}
