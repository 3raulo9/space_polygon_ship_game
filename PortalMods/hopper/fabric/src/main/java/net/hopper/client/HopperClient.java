package net.hopper.client;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.fabricmc.fabric.api.client.rendering.v1.EntityRendererRegistry;
import net.fabricmc.fabric.api.client.item.v1.ItemTooltipCallback;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents;
import net.hopper.HopperMod;
import net.hopper.PortalGunItem;
import net.hopper.scatter.ScatterPayload;
import net.minecraft.component.DataComponentTypes;
import net.minecraft.component.type.PotionContentsComponent;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;
import net.hopper.net.DialPayload;
import net.hopper.net.RealmsPayload;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.item.ModelPredicateProviderRegistry;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.client.util.InputUtil;
import net.minecraft.util.Hand;
import org.lwjgl.glfw.GLFW;

public final class HopperClient implements ClientModInitializer {
    public static KeyBinding dialKey;
    /** The newest list the server sent, for the autotest. */
    static volatile RealmsPayload last;

    @Override
    public void onInitializeClient() {
        EntityRendererRegistry.register(HopperMod.PORTAL, PortalRenderer::new);
        EntityRendererRegistry.register(HopperMod.SHOT, ShotRenderer::new);

        dialKey = KeyBindingHelper.registerKeyBinding(new KeyBinding("key.dimension_hopper.dial",
            InputUtil.Type.KEYSYM, GLFW.GLFW_KEY_V, "category.dimension_hopper"));

        ClientPlayNetworking.registerGlobalReceiver(RealmsPayload.ID, (payload, ctx) -> {
            MinecraftClient mc = ctx.client();
            last = payload;
            if (mc.currentScreen instanceof DialScreen dial) dial.update(payload);
            else if (payload.open()) mc.setScreen(new DialScreen(payload));
        });

        ClientTickEvents.END_CLIENT_TICK.register(mc -> {
            while (dialKey.wasPressed()) {
                if (mc.player != null && holding(mc)) ClientPlayNetworking.send(new DialPayload("open", ""));
                else if (mc.player != null && holdingBlind(mc)) PortalGunItem.noScreen(mc.player);
            }
        });

        ClientPlayNetworking.registerGlobalReceiver(ScatterPayload.ID, (payload, ctx) -> ScatterClient.set(payload.entity(), payload.stage()));
        ClientPlayConnectionEvents.DISCONNECT.register((handler, mc) -> ScatterClient.clear());

        // The concentrated potions are for the gun.
        ItemTooltipCallback.EVENT.register((stack, context, type, lines) -> {
            PotionContentsComponent pc = stack.get(DataComponentTypes.POTION_CONTENTS);
            if (pc == null || pc.potion().isEmpty()) return;
            var potion = pc.potion().get().value();
            if (potion == HopperMod.CONCENTRATED.value() || potion == HopperMod.UNSTABLE.value())
                lines.add(Text.translatable("potion.dimension_hopper.not_for_drinking").formatted(Formatting.RED));
        });

        // The gun's look follows its state: the vial wakes when it is dialled, and a shot's
        // cooldown is the recoil — the first half kicks with the flash, the second settles.
        ModelPredicateProviderRegistry.register(HopperMod.HOPPER, HopperMod.id("dialled"),
            (stack, world, entity, seed) -> HopperMod.destination(stack).isEmpty() ? 0f : 1f);
        for (var gun : new net.minecraft.item.Item[]{HopperMod.HOPPER, HopperMod.PORTAL_GUN}) {
            ModelPredicateProviderRegistry.register(gun, HopperMod.id("fire"),
                (stack, world, entity, seed) -> {
                    if (!(entity instanceof PlayerEntity p)) return 0f;
                    float tick = MinecraftClient.getInstance().getRenderTickCounter().getTickDelta(false);
                    return p.getItemCooldownManager().getCooldownProgress(gun, tick);
                });
        }

        AutoTest.install();
    }

    static boolean holdingBlind(MinecraftClient mc) {
        for (Hand h : Hand.values()) if (mc.player.getStackInHand(h).isOf(HopperMod.PORTAL_GUN)) return true;
        return false;
    }

    static boolean holding(MinecraftClient mc) {
        for (Hand h : Hand.values()) if (mc.player.getStackInHand(h).isOf(HopperMod.HOPPER)) return true;
        return false;
    }
}
