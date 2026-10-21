package net.hopper;

import net.fabricmc.loader.api.FabricLoader;
import net.hopper.net.RealmsPayload;
import net.minecraft.registry.RegistryKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.util.Identifier;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.World;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.function.BiConsumer;
import java.util.function.BooleanSupplier;
import java.util.function.Supplier;

/**
 * Where the gun can go: every dimension this world has, and every realm another game opens
 * to it. Other games come in through the {@code dimension_hopper:realms} entrypoint (see
 * README.md): a supplier of plain maps, so a mod that offers a realm needs nothing of this
 * one to compile, and works the same with or without it installed.
 */
public final class Realms {
    /** One destination the dial can show. */
    public record Realm(String id, String name, String game, boolean realm, RegistryKey<World> dimension,
                        BooleanSupplier clear, Supplier<String> status, Runnable connect,
                        BiConsumer<ServerPlayerEntity, Vec3d> enter) {
        public boolean isClear() {
            try {
                return clear == null || clear.getAsBoolean();
            } catch (RuntimeException e) {
                return false;
            }
        }

        public String statusText() {
            try {
                return status == null ? "" : String.valueOf(status.get());
            } catch (RuntimeException e) {
                return "";
            }
        }
    }

    private static List<Supplier<List<Map<String, Object>>>> providers;

    private Realms() {}

    @SuppressWarnings({"unchecked", "rawtypes"})
    private static List<Supplier<List<Map<String, Object>>>> providers() {
        if (providers == null) {
            providers = new ArrayList<>();
            try {
                for (Supplier s : FabricLoader.getInstance().getEntrypoints("dimension_hopper:realms", Supplier.class))
                    providers.add(s);
            } catch (RuntimeException e) {
                HopperMod.LOG.warn("Dimension Hopper: a realm provider failed to load: {}", e.toString());
            }
        }
        return providers;
    }

    /** Every destination, realms from other games first, then this world's dimensions. */
    @SuppressWarnings("unchecked")
    public static List<Realm> all(MinecraftServer server) {
        List<Realm> out = new ArrayList<>();
        Set<RegistryKey<World>> claimed = new HashSet<>();
        for (Supplier<List<Map<String, Object>>> p : providers()) {
            List<Map<String, Object>> list;
            try {
                list = p.get();
            } catch (RuntimeException e) {
                continue;
            }
            if (list == null) continue;
            for (Map<String, Object> m : list) {
                if (!(m.get("id") instanceof String id)) continue;
                RegistryKey<World> dim = m.get("dimension") instanceof RegistryKey<?> k ? (RegistryKey<World>) k : null;
                if (dim != null) claimed.add(dim);
                out.add(new Realm("realm:" + id, String.valueOf(m.getOrDefault("name", id)),
                    String.valueOf(m.getOrDefault("game", "")), true, dim,
                    m.get("clear") instanceof BooleanSupplier b ? b : null,
                    m.get("status") instanceof Supplier<?> s ? () -> String.valueOf(s.get()) : null,
                    m.get("connect") instanceof Runnable r ? r : null,
                    m.get("enter") instanceof BiConsumer<?, ?> c ? (BiConsumer<ServerPlayerEntity, Vec3d>) c : null));
            }
        }
        for (RegistryKey<World> key : server.getWorldRegistryKeys()) {
            if (claimed.contains(key)) continue;
            out.add(new Realm(key.getValue().toString(), friendly(key.getValue()), "", false, key,
                null, null, null, null));
        }
        return out;
    }

    public static Realm find(MinecraftServer server, String id) {
        for (Realm r : all(server)) if (r.id().equals(id)) return r;
        return null;
    }

    public static String friendly(Identifier id) {
        if (id.equals(World.OVERWORLD.getValue())) return "The Overworld";
        if (id.equals(World.NETHER.getValue())) return "The Nether";
        if (id.equals(World.END.getValue())) return "The End";
        StringBuilder b = new StringBuilder();
        for (String w : id.getPath().replace('/', ' ').split("[_ ]+")) {
            if (w.isEmpty()) continue;
            if (b.length() > 0) b.append(' ');
            b.append(w.substring(0, 1).toUpperCase(Locale.ROOT)).append(w.substring(1));
        }
        return b + " (" + id.getNamespace() + ")";
    }

    public static RealmsPayload payload(MinecraftServer server, boolean open, String selected, String coords) {
        List<RealmsPayload.Entry> entries = new ArrayList<>();
        for (Realm r : all(server))
            entries.add(new RealmsPayload.Entry(r.id(), r.name(), r.game(), r.realm(), r.isClear(), r.statusText()));
        return new RealmsPayload(open, selected == null ? "" : selected, coords == null ? "" : coords, entries);
    }
}
