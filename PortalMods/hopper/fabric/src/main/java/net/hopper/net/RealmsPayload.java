package net.hopper.net;

import net.hopper.HopperMod;
import net.minecraft.network.RegistryByteBuf;
import net.minecraft.network.codec.PacketCodec;
import net.minecraft.network.packet.CustomPayload;

import java.util.ArrayList;
import java.util.List;

/** Everything the dial can reach, as the server sees it right now, and which one the gun
 *  in hand is set to. {@code open} asks the client to show the dial. */
public record RealmsPayload(boolean open, String selected, List<Entry> entries) implements CustomPayload {
    /** One destination. {@code realm} is another game's; the rest are this world's dimensions. */
    public record Entry(String id, String name, String game, boolean realm, boolean clear, String status) {}

    public static final Id<RealmsPayload> ID = new Id<>(HopperMod.id("realms"));
    public static final PacketCodec<RegistryByteBuf, RealmsPayload> CODEC = PacketCodec.of(RealmsPayload::write, RealmsPayload::read);

    private void write(RegistryByteBuf buf) {
        buf.writeBoolean(open);
        buf.writeString(selected);
        buf.writeVarInt(entries.size());
        for (Entry e : entries) {
            buf.writeString(e.id());
            buf.writeString(e.name());
            buf.writeString(e.game());
            buf.writeBoolean(e.realm());
            buf.writeBoolean(e.clear());
            buf.writeString(e.status());
        }
    }

    private static RealmsPayload read(RegistryByteBuf buf) {
        boolean open = buf.readBoolean();
        String selected = buf.readString();
        int n = buf.readVarInt();
        List<Entry> entries = new ArrayList<>();
        for (int i = 0; i < n; i++) {
            entries.add(new Entry(buf.readString(), buf.readString(), buf.readString(), buf.readBoolean(),
                buf.readBoolean(), buf.readString()));
        }
        return new RealmsPayload(open, selected, entries);
    }

    @Override
    public Id<? extends CustomPayload> getId() {
        return ID;
    }
}
