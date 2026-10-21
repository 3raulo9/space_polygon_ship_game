package net.unrendered.rift;

import com.mojang.brigadier.CommandDispatcher;
import net.minecraft.server.command.CommandManager;
import net.minecraft.server.command.ServerCommandSource;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;
import net.unrendered.rift.bridge.Bridge;
import net.unrendered.rift.bridge.Session;

/** /rift join, which builds a portal in front of you, /rift status, and /rift home, the way
 *  out of the rift that needs no portal. */
public final class RiftCommands {
    private RiftCommands() {}

    public static void register(CommandDispatcher<ServerCommandSource> dispatcher) {
        dispatcher.register(CommandManager.literal("rift")
            .then(CommandManager.literal("join").executes(ctx -> {
                ServerPlayerEntity player = ctx.getSource().getPlayerOrThrow();
                if (player.getServerWorld().getRegistryKey() == RiftMod.RIFT) {
                    ctx.getSource().sendError(Text.literal("You're already in UNRENDERED. Walk back through the glowstone to go home."));
                    return 0;
                }
                RiftPortals.buildInFront(player);
                ctx.getSource().sendFeedback(() -> Text.literal("A portal opened in front of you. Step into the water.")
                    .formatted(Formatting.LIGHT_PURPLE), false);
                return 1;
            }))
            .then(CommandManager.literal("home").executes(ctx -> {
                ServerPlayerEntity player = ctx.getSource().getPlayerOrThrow();
                if (player.getServerWorld().getRegistryKey() != RiftMod.RIFT) {
                    ctx.getSource().sendError(Text.literal("You're not in UNRENDERED."));
                    return 0;
                }
                RiftVisitors.sendHome(player);
                ctx.getSource().sendFeedback(() -> Text.literal("Back the way you came.").formatted(Formatting.LIGHT_PURPLE), false);
                return 1;
            }))
            .then(CommandManager.literal("status").executes(ctx -> {
                Session s = Bridge.session();
                String line;
                if (!Bridge.running()) line = "Not listening. Open this world to LAN to let UNRENDERED find it.";
                else if (s == null) line = "Listening on port " + Bridge.port() + ". No UNRENDERED linked yet.";
                else line = "Linked to " + s.pilotName + " (" + s.chassis + ") at " + s.remote() + ". "
                    + RiftVisitors.count() + " Minecraft player(s) in UNRENDERED. "
                    + (RiftPilot.active() ? "The pilot is in Minecraft." : "The pilot is at home.");
                String out = line;
                ctx.getSource().sendFeedback(() -> Text.literal("[RIFT] ").formatted(Formatting.LIGHT_PURPLE)
                    .append(Text.literal(out).formatted(Formatting.GRAY)), false);
                return 1;
            })));
    }
}
