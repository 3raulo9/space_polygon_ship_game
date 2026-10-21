package net.hopper.client;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.fabricmc.fabric.api.client.rendering.v1.EntityRendererRegistry;
import net.hopper.HopperMod;
import net.hopper.net.DialPayload;
import net.hopper.net.RealmsPayload;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.util.InputUtil;
import net.minecraft.util.Hand;
import org.lwjgl.glfw.GLFW;

public final class HopperClient implements ClientModInitializer {
    public static KeyBinding dialKey;

    @Override
    public void onInitializeClient() {
        EntityRendererRegistry.register(HopperMod.PORTAL, PortalRenderer::new);
        EntityRendererRegistry.register(HopperMod.SHOT, ShotRenderer::new);

        dialKey = KeyBindingHelper.registerKeyBinding(new KeyBinding("key.dimension_hopper.dial",
            InputUtil.Type.KEYSYM, GLFW.GLFW_KEY_V, "category.dimension_hopper"));

        ClientPlayNetworking.registerGlobalReceiver(RealmsPayload.ID, (payload, ctx) -> {
            MinecraftClient mc = ctx.client();
            if (mc.currentScreen instanceof DialScreen dial) dial.update(payload);
            else if (payload.open()) mc.setScreen(new DialScreen(payload));
        });

        ClientTickEvents.END_CLIENT_TICK.register(mc -> {
            while (dialKey.wasPressed()) {
                if (mc.player != null && holding(mc)) ClientPlayNetworking.send(new DialPayload("open", ""));
            }
        });

        AutoTest.install();
    }

    static boolean holding(MinecraftClient mc) {
        for (Hand h : Hand.values()) if (mc.player.getStackInHand(h).isOf(HopperMod.HOPPER)) return true;
        return false;
    }
}
