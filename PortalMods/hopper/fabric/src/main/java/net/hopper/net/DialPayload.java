package net.hopper.net;

import net.hopper.HopperMod;
import net.minecraft.network.RegistryByteBuf;
import net.minecraft.network.codec.PacketCodec;
import net.minecraft.network.codec.PacketCodecs;
import net.minecraft.network.packet.CustomPayload;

/** The client's dial: "open" (show it), "list" (refresh it), "select" a destination, or
 *  "connect" to a realm that is not clear yet. */
public record DialPayload(String action, String id) implements CustomPayload {
    public static final Id<DialPayload> ID = new Id<>(HopperMod.id("dial"));
    public static final PacketCodec<RegistryByteBuf, DialPayload> CODEC = PacketCodec.tuple(
        PacketCodecs.STRING, DialPayload::action,
        PacketCodecs.STRING, DialPayload::id,
        DialPayload::new);

    @Override
    public Id<? extends CustomPayload> getId() {
        return ID;
    }
}
