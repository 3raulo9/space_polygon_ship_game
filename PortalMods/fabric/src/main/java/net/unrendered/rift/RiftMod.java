package net.unrendered.rift;

import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.command.v2.CommandRegistrationCallback;
import net.fabricmc.fabric.api.entity.event.v1.ServerLivingEntityEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.event.player.PlayerBlockBreakEvents;
import net.fabricmc.fabric.api.event.player.UseBlockCallback;
import net.fabricmc.fabric.api.networking.v1.PayloadTypeRegistry;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import net.minecraft.registry.RegistryKey;
import net.minecraft.registry.RegistryKeys;
import net.minecraft.server.MinecraftServer;
import net.minecraft.util.Identifier;
import net.minecraft.world.World;
import net.minecraft.server.world.ChunkTicketType;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.ChunkPos;
import net.minecraft.util.math.Vec3d;
import java.util.Comparator;
import net.unrendered.rift.bridge.Bridge;
import net.unrendered.rift.net.CraftPayload;
import net.unrendered.rift.net.ModelPayload;
import net.unrendered.rift.net.ScenePayload;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * THE RIFT, Minecraft side.
 *
 * <p>Open a world to LAN and the bridge starts listening for UNRENDERED. Pour water into a
 * glowstone frame and the portal opens. Step through while an UNRENDERED pilot is linked
 * and you are standing in UNRENDERED's world, still a Minecraft player with everything
 * you carry. A pilot who comes the other way walks Minecraft's real terrain as their own
 * craft.
 */
public final class RiftMod implements ModInitializer {
    public static final String MOD_ID = "unrendered_portal";
    public static final Logger LOG = LoggerFactory.getLogger("rift");

    /** The dimension UNRENDERED's world is replayed in. MC (x, 64 + y, z) is UNR (x, y, z). */
    public static final RegistryKey<World> RIFT = RegistryKey.of(RegistryKeys.WORLD, id("unrendered"));

    /** Where the grid's surface is in the rift: a player standing on it has feet at y = 64. */
    public static final int FLOOR_Y = 64;

    /** UNRENDERED's world is a 400 x 400 torus. */
    public static final double TORUS = 400.0;

    public static MinecraftServer server;

    /**
     * Keeps the world around something that isn't a Minecraft player loaded and ticking the
     * way it would be around a player: the UNRENDERED pilot in Minecraft, and UNRENDERED's own
     * players standing in the rift. Refreshed while they're there, gone a couple of seconds
     * after they leave.
     */
    public static final ChunkTicketType<ChunkPos> PRESENCE =
        ChunkTicketType.create("unrendered_presence", Comparator.comparingLong(ChunkPos::toLong), 60);

    /** Holds the chunks around {@code at} in {@code world}, like a player standing there. */
    public static void hold(ServerWorld world, Vec3d at) {
        ChunkPos c = new ChunkPos(BlockPos.ofFloored(at));
        world.getChunkManager().addTicket(PRESENCE, c, 3, c);
    }

    public static Identifier id(String path) {
        return Identifier.of(MOD_ID, path);
    }

    @Override
    public void onInitialize() {
        RiftConfig.load();
        RiftBlocks.register();
        RiftItems.register();
        RiftEntities.register();
        RiftDamage.register();
        RiftSounds.register();

        PayloadTypeRegistry.playS2C().register(ScenePayload.ID, ScenePayload.CODEC);
        PayloadTypeRegistry.playS2C().register(CraftPayload.ID, CraftPayload.CODEC);
        PayloadTypeRegistry.playS2C().register(ModelPayload.ID, ModelPayload.CODEC);

        UseBlockCallback.EVENT.register(RiftPortals::onUseBlock);
        // Who is breaking a block, for the piece of UNRENDERED's city it may turn out to be.
        PlayerBlockBreakEvents.BEFORE.register((world, player, pos, state, be) -> {
            RiftVisitors.breaker = player;
            return true;
        });
        PlayerBlockBreakEvents.AFTER.register((world, player, pos, state, be) -> RiftVisitors.breaker = null);
        PlayerBlockBreakEvents.CANCELED.register((world, player, pos, state, be) -> RiftVisitors.breaker = null);

        CommandRegistrationCallback.EVENT.register((dispatcher, access, env) -> RiftCommands.register(dispatcher));

        ServerLifecycleEvents.SERVER_STARTED.register(s -> {
            server = s;
            RiftCity.load(s);
            RiftBuilt.load(s);
            RiftVisitors.load(s);
            RiftPortals.load(s);
        });
        ServerLifecycleEvents.SERVER_STOPPING.register(s -> {
            // Nobody is saved standing in the rift: the world it shows is about to stop.
            RiftVisitors.sendAllHome(s, "The world is closing. You were sent home.");
            Bridge.stop(s, "the Minecraft world closed");
            RiftVisitors.clear();
            RiftPilot.clear();
            RiftSeats.clear();
            RiftEnemies.clear();
            server = null;
        });
        ServerTickEvents.END_SERVER_TICK.register(RiftMod::tick);
        ServerPlayConnectionEvents.DISCONNECT.register((handler, s) -> RiftVisitors.onDisconnect(handler.getPlayer()));
        ServerPlayConnectionEvents.JOIN.register((handler, sender, s) -> {
            RiftEnemies.onJoin(handler.getPlayer());
            RiftVisitors.onJoin(handler.getPlayer());
        });
        ServerLivingEntityEvents.AFTER_DEATH.register((entity, source) -> {
            if (entity instanceof net.minecraft.server.network.ServerPlayerEntity p) RiftVisitors.onDeath(p);
        });
    }

    private static void tick(MinecraftServer s) {
        // A world opened to LAN (or any dedicated server) is a world UNRENDERED can find.
        boolean open = s.isDedicated() || s.isRemote();
        if (open && !Bridge.running()) Bridge.start(s);
        else if (!open && Bridge.running()) Bridge.stop(s, "the world is no longer open to LAN");

        Bridge.pump(s);
        RiftPortals.tick(s);
        RiftCity.tick(s);
        RiftVisitors.tick(s);
        RiftPilot.tick(s);
        RiftSeats.tick(s);
        RiftBuilt.tick(s);
    }
}
