package net.unrendered.rift.client;

import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.Element;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.screen.world.CreateWorldScreen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.client.util.ScreenshotRecorder;
import net.minecraft.command.argument.EntityAnchorArgumentType;
import net.minecraft.server.integrated.IntegratedServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.text.TranslatableTextContent;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.GameMode;
import net.unrendered.rift.RiftMod;
import net.unrendered.rift.RiftPilot;
import net.unrendered.rift.RiftVisitors;
import net.unrendered.rift.bridge.Bridge;

/**
 * The end-to-end harness's hands in Minecraft, active only with {@code -Drift.autotest=true}.
 * It makes a world, opens it to LAN, puts mobs, an item and a portal in front of the player,
 * waits for UNRENDERED's pilot, photographs it, then walks the player into UNRENDERED and
 * photographs that, and quits. Everything it does is logged with the {@code RIFT-E2E} tag.
 */
public final class AutoTest {
    private enum Step { TITLE, CREATING, LOADING, SETUP, WAIT_PILOT, WATCH_PILOT, TO_RIFT, IN_RIFT, DONE }

    private static Step step = Step.TITLE;
    private static int ticks, stepTicks, shots;
    private static long pilotSince = -1;
    private static boolean pilotSeenLeaving;

    private AutoTest() {}

    public static void install() {
        if (!Boolean.getBoolean("rift.autotest")) return;
        log("autotest on");
        ClientTickEvents.END_CLIENT_TICK.register(AutoTest::tick);
    }

    static void log(String s) {
        RiftMod.LOG.info("RIFT-E2E: {}", s);
    }

    private static void go(Step s) {
        log("step " + step + " -> " + s + " at tick " + ticks);
        step = s;
        stepTicks = 0;
    }

    private static void tick(MinecraftClient mc) {
        ticks++;
        stepTicks++;
        if (ticks > 20 * 60 * 12 && step != Step.DONE) {
            log("timed out in step " + step);
            go(Step.DONE);
        }
        switch (step) {
            case TITLE -> {
                if (mc.currentScreen instanceof TitleScreen && stepTicks > 40) {
                    CreateWorldScreen.create(mc, mc.currentScreen);
                    go(Step.CREATING);
                }
            }
            case CREATING -> {
                Screen s = mc.currentScreen;
                if (s instanceof CreateWorldScreen && stepTicks > 20) {
                    for (Element e : s.children()) {
                        if (e instanceof ButtonWidget b && b.getMessage().getContent() instanceof TranslatableTextContent t
                            && t.getKey().equals("selectWorld.create")) {
                            b.onPress();
                            go(Step.LOADING);
                            return;
                        }
                    }
                    if (stepTicks % 100 == 0) log("no create button yet on " + s.getClass().getSimpleName());
                }
            }
            case LOADING -> {
                if (mc.world != null && mc.player != null && mc.getServer() != null && stepTicks > 100) go(Step.SETUP);
            }
            case SETUP -> {
                IntegratedServer server = mc.getServer();
                String name = mc.player.getGameProfile().getName();
                server.execute(() -> {
                    ServerPlayerEntity sp = server.getPlayerManager().getPlayer(name);
                    run(server, null, "gamerule doDaylightCycle false");
                    run(server, null, "gamerule doWeatherCycle false");
                    run(server, null, "gamerule doMobSpawning false");
                    run(server, null, "time set noon");
                    run(server, null, "weather clear");
                    run(server, null, "gamemode creative " + name);
                    run(server, null, "difficulty easy");
                    String at = "execute at " + name + " run ";
                    run(server, null, at + "summon husk ^3 ^ ^6 {PersistenceRequired:1b}");
                    run(server, null, at + "summon cow ^-3 ^ ^5 {PersistenceRequired:1b}");
                    run(server, null, at + "summon pig ^-1 ^ ^7 {PersistenceRequired:1b}");
                    run(server, null, at + "summon villager ^2 ^ ^8 {PersistenceRequired:1b,NoAI:1b}");
                    run(server, null, at + "summon item ^1 ^1 ^4 {Item:{id:\"minecraft:diamond_sword\",count:1},PickupDelay:32767}");
                    boolean lan = server.openToLan(GameMode.CREATIVE, true, 25565);
                    log("open to LAN: " + lan);
                    if (sp != null) run(server, sp, "rift join");
                });
                go(Step.WAIT_PILOT);
            }
            case WAIT_PILOT -> {
                if (stepTicks % 100 == 0) log("waiting for the pilot; linked=" + Bridge.linked());
                if (RiftPilot.active()) {
                    pilotSince = ticks;
                    go(Step.WATCH_PILOT);
                }
            }
            case WATCH_PILOT -> {
                Vec3d p = RiftPilot.pilotPos();
                if (RiftPilot.active()) follow(mc, p);
                if (stepTicks % 80 == 40) shot(mc, "mc-pilot");
                if (!RiftPilot.active() && !pilotSeenLeaving) {
                    pilotSeenLeaving = true;
                    log("the pilot went home");
                }
                boolean long_ = stepTicks > 20 * 70;
                if ((pilotSeenLeaving && stepTicks > 20 * 10) || long_) {
                    IntegratedServer server = mc.getServer();
                    String name = mc.player.getGameProfile().getName();
                    server.execute(() -> {
                        ServerPlayerEntity sp = server.getPlayerManager().getPlayer(name);
                        if (sp != null && Bridge.linked())
                            RiftVisitors.requestArrival(sp, sp.getServerWorld().getRegistryKey(), sp.getPos(), sp.getYaw());
                        log("asked to go through to UNRENDERED; linked=" + Bridge.linked());
                    });
                    go(Step.TO_RIFT);
                }
            }
            case TO_RIFT -> {
                if (mc.world != null && mc.world.getRegistryKey() == RiftMod.RIFT && stepTicks > 20) go(Step.IN_RIFT);
                else if (stepTicks > 20 * 30) {
                    log("never reached the rift");
                    go(Step.DONE);
                }
            }
            case IN_RIFT -> {
                if (mc.player != null) mc.player.setYaw(mc.player.getYaw() + 1.5f);
                if (stepTicks % 60 == 30) shot(mc, "mc-rift");
                if (stepTicks > 20 * 30) go(Step.DONE);
            }
            case DONE -> {
                if (stepTicks == 1) log("done after " + shots + " screenshots");
                if (stepTicks == 40) mc.scheduleStop();
            }
        }
    }

    /** Stand a little way off the pilot and look at it. */
    private static void follow(MinecraftClient mc, Vec3d p) {
        if (mc.player == null || p == null) return;
        if (stepTicks % 40 == 1) {
            IntegratedServer server = mc.getServer();
            String name = mc.player.getGameProfile().getName();
            Vec3d at = p.add(5.5, 2.5, 5.5);
            server.execute(() -> run(server, null, String.format("tp %s %.2f %.2f %.2f", name, at.x, at.y, at.z)));
        }
        mc.player.lookAt(EntityAnchorArgumentType.EntityAnchor.EYES, p.add(0, 1.0, 0));
    }

    private static void shot(MinecraftClient mc, String what) {
        String file = String.format("%s-%02d.png", what, shots++);
        ScreenshotRecorder.saveScreenshot(mc.runDirectory, file, mc.getFramebuffer(), msg -> { });
        log("screenshot " + file);
    }

    private static void run(IntegratedServer server, ServerPlayerEntity as, String cmd) {
        try {
            server.getCommandManager().executeWithPrefix(as != null ? as.getCommandSource() : server.getCommandSource(), cmd);
        } catch (RuntimeException e) {
            log("command failed: " + cmd + ": " + e);
        }
    }
}
