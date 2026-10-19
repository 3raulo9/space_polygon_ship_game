package net.unrendered.rift.net;

import net.minecraft.network.RegistryByteBuf;
import net.minecraft.network.codec.PacketCodec;
import net.minecraft.network.codec.PacketCodecs;
import net.minecraft.network.packet.CustomPayload;
import net.unrendered.rift.RiftMod;

/** An UNRENDERED enemy's body as UNRENDERED draws it (a deflated draw list), for clients to
 *  draw that kind of enemy with. */
public record ModelPayload(String kind, byte[] data) implements CustomPayload {
    public static final Id<ModelPayload> ID = new Id<>(RiftMod.id("model"));
    public static final PacketCodec<RegistryByteBuf, ModelPayload> CODEC = PacketCodec.tuple(
        PacketCodecs.STRING, ModelPayload::kind,
        PacketCodecs.BYTE_ARRAY, ModelPayload::data,
        ModelPayload::new);

    @Override
    public Id<? extends CustomPayload> getId() {
        return ID;
    }
}
