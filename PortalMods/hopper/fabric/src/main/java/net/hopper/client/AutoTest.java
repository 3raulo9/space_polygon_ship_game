package net.hopper.client;

import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.hopper.HopperMod;
import net.hopper.entity.PortalEntity;
import net.hopper.net.DialPayload;
import net.hopper.net.RealmsPayload;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.util.ScreenshotRecorder;
import net.minecraft.command.argument.EntityAnchorArgumentType;
import net.minecraft.item.ItemStack;
import net.minecraft.server.integrated.IntegratedServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.hopper.scatter.Scatter;
import net.minecraft.client.option.Perspective;
import net.minecraft.component.type.PotionContentsComponent;
import net.minecraft.entity.effect.StatusEffectInstance;
import net.minecraft.item.Items;
import net.minecraft.util.Hand;
import net.minecraft.util.math.Vec3d;

import java.util.Comparator;

/**
 * The end-to-end harness's hands for the gun. Armed by {@code -Dhopper.autotest=true} or by
 * the rift's own autotest ({@code -Drift.autotest=true}); it waits until the system property
 * {@code hopper.autotest.go} is set (the rift's autotest sets it when it is done, so the two
 * never fight over the player), then: puts the gun in hand, opens the dial, picks another
 * game's realm if there is one and then the Nether, fires at the ground, walks through, and
 * photographs each step. It sets {@code hopper.autotest.done} when finished. Logged with the
 * {@code HOPPER-E2E} tag.
 */
final class AutoTest {
    private static int t = -1, shots;
    private static boolean finished;

    private AutoTest() {}

    static void install() {
        if (!Boolean.getBoolean("hopper.autotest") && !Boolean.getBoolean("rift.autotest")) return;
        log("armed; waiting for hopper.autotest.go");
        ClientTickEvents.END_CLIENT_TICK.register(AutoTest::tick);
    }

    private static void log(String s) {
        HopperMod.LOG.info("HOPPER-E2E: {}", s);
    }

    private static void shot(MinecraftClient mc, String what) {
        String file = String.format("%s-%02d.png", what, shots++);
        ScreenshotRecorder.saveScreenshot(mc.runDirectory, file, mc.getFramebuffer(), msg -> { });
        log("screenshot " + file);
    }

    private static void server(MinecraftClient mc, java.util.function.Consumer<ServerPlayerEntity> what) {
        IntegratedServer server = mc.getServer();
        if (server == null || mc.player == null) return;
        String name = mc.player.getGameProfile().getName();
        server.execute(() -> {
            ServerPlayerEntity sp = server.getPlayerManager().getPlayer(name);
            if (sp != null) what.accept(sp);
        });
    }

    private static void tick(MinecraftClient mc) {
        if (finished) return;
        if (t < 0) {
            if (System.getProperty("hopper.autotest.go") == null || mc.player == null || mc.world == null) return;
            t = 0;
            log("go, in " + mc.world.getRegistryKey().getValue());
        }
        t++;
        if (mc.player == null) return;
        switch (t) {
            case 1 -> {
                mc.player.getInventory().selectedSlot = 0;
                server(mc, sp -> {
                    // Everything the mod adds, along the hotbar: the Hopper in hand.
                    ItemStack[] bar = {
                        new ItemStack(HopperMod.HOPPER), new ItemStack(HopperMod.PORTAL_GUN),
                        new ItemStack(HopperMod.REINFORCED_PEARL, 9), new ItemStack(HopperMod.HANDLE),
                        new ItemStack(HopperMod.CHAMBER), new ItemStack(HopperMod.MONITOR), new ItemStack(HopperMod.COMPUTER),
                        PotionContentsComponent.createStack(Items.POTION, HopperMod.CONCENTRATED),
                        PotionContentsComponent.createStack(Items.POTION, HopperMod.UNSTABLE),
                    };
                    sp.getInventory().selectedSlot = 0;
                    for (int i = 0; i < bar.length; i++) sp.getInventory().setStack(i, bar[i]);
                });
            }
            case 5 -> mc.player.setPitch(12f);
            case 8 -> mc.player.getInventory().selectedSlot = 1;
            case 20 -> shot(mc, "mc-portal-gun-hand");
            case 24 -> mc.player.getInventory().selectedSlot = 0;
            case 32 -> shot(mc, "mc-hopper-items");
            case 30 -> shot(mc, "mc-hopper-hand");
            case 34 -> ClientPlayNetworking.send(new DialPayload("open", ""));
            case 60 -> {
                if (mc.currentScreen instanceof DialScreen dial) {
                    StringBuilder b = new StringBuilder();
                    for (RealmsPayload.Entry e : dial.realms().entries())
                        b.append(e.id()).append(e.realm() ? (e.clear() ? " (clear)" : " (no signal)") : "").append("; ");
                    log("the dial lists: " + b);
                    dial.realms().entries().stream().filter(RealmsPayload.Entry::realm).findFirst()
                        .ifPresent(e -> dial.pick(e.id()));
                } else log("the dial did not open; screen is " + mc.currentScreen);
            }
            case 80 -> shot(mc, "mc-dial");
            case 86 -> {
                if (mc.currentScreen instanceof DialScreen dial) dial.pick("minecraft:the_nether");
            }
            case 94 -> {
                if (mc.currentScreen instanceof DialScreen dial) {
                    dial.setCoords("120", "", "-60");
                    log("dialled coordinates 120, ~, -60");
                }
            }
            case 100 -> shot(mc, "mc-dial-coords");
            case 104 -> shot(mc, "mc-dial");
            case 110 -> {
                mc.setScreen(null);
                mc.player.setPitch(38f);
            }
            case 116 -> log("fired: " + mc.interactionManager.interactItem(mc.player, Hand.MAIN_HAND));
            case 119 -> shot(mc, "mc-hopper-kick");
            case 150 -> {
                PortalEntity p = nearest(mc);
                if (p != null) {
                    log("a portal stands at " + p.getPos() + " facing " + p.getYaw());
                    Vec3d eye = p.getPos().add(PortalEntity.facing(p.getYaw()).multiply(5)).add(0, 1.0, 0);
                    server(mc, sp -> sp.networkHandler.requestTeleport(eye.x, eye.y, eye.z, sp.getYaw(), sp.getPitch()));
                } else log("no portal in sight");
            }
            case 156 -> {
                PortalEntity p = nearest(mc);
                if (p != null) mc.player.lookAt(EntityAnchorArgumentType.EntityAnchor.EYES, p.getPos().add(0, 1.3, 0));
            }
            case 162 -> shot(mc, "mc-hopper-portal");
            case 170 -> {
                PortalEntity p = nearest(mc);
                if (p != null) {
                    Vec3d in = p.getPos().add(0, 0.2, 0);
                    log("walking into it");
                    server(mc, sp -> sp.networkHandler.requestTeleport(in.x, in.y, in.z, sp.getYaw(), 0f));
                }
            }
            case 240 -> {
                mc.player.setPitch(5f);
                log("now in " + mc.world.getRegistryKey().getValue() + " at " + mc.player.getPos());
            }
            case 250 -> shot(mc, "mc-hopper-far");
            case 262 -> mc.player.setYaw(mc.player.getYaw() + 180f);
            case 270 -> shot(mc, "mc-hopper-far");
            case 280 -> log("over in " + mc.world.getRegistryKey().getValue() + "; now the potion");
            default -> {
                if (t > 280 && !scattered) scatter(mc);
                else if (t > 280) unclear(mc);
            }
        }
    }

    private static boolean scattered;
    private static int scatterAt = -1, apart = -1, unclearFrom;

    /**
     * The concentrated potion's doom, twenty times faster (hopper.fastScatter), on the test
     * player — in creative, so the jumps and tears cost nothing. Once both arms have gone it is
     * photographed from the front, and then the effect is taken away and the body is whole.
     */
    private static void scatter(MinecraftClient mc) {
        if (scatterAt < 0) {
            scatterAt = t;
            System.setProperty("hopper.fastScatter", "true");
            server(mc, sp -> sp.addStatusEffect(new StatusEffectInstance(HopperMod.SCATTER, Scatter.TOTAL)));
            log("drank the concentrated teleportation potion (fast)");
            return;
        }
        int stage = ScatterClient.stage(mc.player.getId());
        if (apart < 0 && (stage >= 2 || t - scatterAt > 20 * 40)) {
            apart = t;
            log("stage " + stage + " after " + (t - scatterAt) + " ticks, at " + mc.player.getPos());
            mc.options.setPerspective(Perspective.THIRD_PERSON_FRONT);
            mc.player.setPitch(10f);
        }
        if (apart < 0) return;
        if (t - apart == 6) shot(mc, "mc-scatter");
        if (t - apart == 12) {
            server(mc, sp -> sp.removeStatusEffect(HopperMod.SCATTER));
            System.clearProperty("hopper.fastScatter");
            mc.options.setPerspective(Perspective.FIRST_PERSON);
        }
        if (t - apart == 40) {
            log("whole again: stage " + ScatterClient.stage(mc.player.getId()) + "; waiting for a realm to lose its signal");
            scattered = true;
            unclearFrom = t;
        }
    }

    private static int lost = -1;

    /**
     * The second half: a realm from another game with no clear connection. It waits for one
     * (UNRENDERED's test run quits a minute or so after the rift's test is done), shows it on
     * the dial with its message, asks it to connect, and tries to fire at it.
     */
    private static void unclear(MinecraftClient mc) {
        if (lost < 0) {
            if (t % 20 == 0) ClientPlayNetworking.send(new DialPayload("list", ""));
            RealmsPayload l = HopperClient.last;
            String realm = l == null ? null : l.entries().stream().filter(e -> e.realm() && !e.clear())
                .map(RealmsPayload.Entry::id).findFirst().orElse(null);
            if (realm != null) {
                lost = t;
                unclearId = realm;
                log(realm + " has no clear connection now");
            } else if (t > unclearFrom + 20 * 70) {
                log("every realm stayed clear");
                finish(mc);
            }
            return;
        }
        int s = t - lost;
        switch (s) {
            case 2 -> ClientPlayNetworking.send(new DialPayload("open", ""));
            case 22 -> {
                if (mc.currentScreen instanceof DialScreen dial) dial.pick(unclearId);
                else log("the dial did not open; screen is " + mc.currentScreen);
            }
            case 40 -> shot(mc, "mc-dial-unclear");
            case 44 -> {
                if (mc.currentScreen instanceof DialScreen dial) {
                    dial.establish(unclearId);
                    log("asked " + unclearId + " to connect");
                }
            }
            case 70 -> {
                RealmsPayload l = HopperClient.last;
                if (l != null) l.entries().stream().filter(e -> e.id().equals(unclearId)).findFirst()
                    .ifPresent(e -> log("its status: " + e.status() + (e.clear() ? " (clear)" : " (not clear)")));
                shot(mc, "mc-dial-unclear");
            }
            case 76 -> {
                mc.setScreen(null);
                mc.player.setPitch(20f);
            }
            case 82 -> log("fired at it: " + mc.interactionManager.interactItem(mc.player, Hand.MAIN_HAND));
            case 86 -> shot(mc, "mc-hopper-refused");
            case 100 -> finish(mc);
            default -> { }
        }
    }

    private static String unclearId;

    private static void finish(MinecraftClient mc) {
        log("done in " + mc.world.getRegistryKey().getValue());
        finished = true;
        System.setProperty("hopper.autotest.done", "1");
    }

    private static PortalEntity nearest(MinecraftClient mc) {
        return mc.world.getEntitiesByClass(PortalEntity.class, mc.player.getBoundingBox().expand(80), e -> true).stream()
            .min(Comparator.comparingDouble(e -> e.squaredDistanceTo(mc.player))).orElse(null);
    }
}
