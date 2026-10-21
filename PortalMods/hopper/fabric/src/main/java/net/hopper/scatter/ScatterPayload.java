package net.hopper.scatter;

import net.hopper.HopperMod;
import net.minecraft.network.RegistryByteBuf;
import net.minecraft.network.codec.PacketCodec;
import net.minecraft.network.codec.PacketCodecs;
import net.minecraft.network.packet.CustomPayload;

/** How much of a body has teleported away: 0 whole, 1 the left arm, 2 both arms, 3 and the
 *  left leg, 4 and the right leg. Sent to everyone who can see it. */
public record ScatterPayload(int entity, int stage) implements CustomPayload {
    public static final Id<ScatterPayload> ID = new Id<>(HopperMod.id("scatter"));
    public static final PacketCodec<RegistryByteBuf, ScatterPayload> CODEC = PacketCodec.tuple(
        PacketCodecs.VAR_INT, ScatterPayload::entity,
        PacketCodecs.VAR_INT, ScatterPayload::stage,
        ScatterPayload::new);

    @Override
    public Id<? extends CustomPayload> getId() {
        return ID;
    }
}
