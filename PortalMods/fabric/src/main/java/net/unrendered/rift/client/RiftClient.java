package net.unrendered.rift.client;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.blockrenderlayer.v1.BlockRenderLayerMap;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.fabricmc.fabric.api.client.rendering.v1.ColorProviderRegistry;
import net.fabricmc.fabric.api.client.rendering.v1.DimensionRenderingRegistry;
import net.fabricmc.fabric.api.client.rendering.v1.EntityRendererRegistry;
import net.fabricmc.fabric.api.client.rendering.v1.WorldRenderEvents;
import net.minecraft.client.render.RenderLayer;
import net.minecraft.client.render.entity.EmptyEntityRenderer;
import net.minecraft.client.render.entity.FlyingItemEntityRenderer;
import net.unrendered.rift.RiftBlocks;
import net.unrendered.rift.RiftEntities;
import net.unrendered.rift.RiftHooks;
import net.unrendered.rift.RiftMod;
import net.unrendered.rift.net.CraftPayload;
import net.unrendered.rift.net.ModelPayload;
import net.unrendered.rift.net.ScenePayload;

public final class RiftClient implements ClientModInitializer {
    /** Minecraft's default water colour, which the portal's still water is tinted with. */
    public static final int WATER = 0x3F76E4;

    @Override
    public void onInitializeClient() {
        RiftHooks.client = new ClientHooksImpl();
        AutoTest.install();

        BlockRenderLayerMap.INSTANCE.putBlock(RiftBlocks.RIFT_PORTAL, RenderLayer.getTranslucent());
        ColorProviderRegistry.BLOCK.register((state, world, pos, tint) -> WATER, RiftBlocks.RIFT_PORTAL);

        EntityRendererRegistry.register(RiftEntities.ECHO, EmptyEntityRenderer::new);
        EntityRendererRegistry.register(RiftEntities.PILOT, PilotRenderer::new);
        EntityRendererRegistry.register(RiftEntities.CRAB_CORE, FlyingItemEntityRenderer::new);
        EntityRendererRegistry.register(RiftEntities.SEAT, EmptyEntityRenderer::new);
        EntityRendererRegistry.register(RiftEntities.UNR_ENEMY, UnrEnemyRenderer::new);
        EntityRendererRegistry.register(RiftEntities.UNR_BOLT, ctx -> new FlyingItemEntityRenderer<>(ctx, 0.8f, true));

        ClientPlayNetworking.registerGlobalReceiver(ScenePayload.ID, (payload, ctx) -> SceneRenderer.accept(payload.data()));
        ClientPlayNetworking.registerGlobalReceiver(CraftPayload.ID, (payload, ctx) -> PilotRenderer.accept(payload.entityId(), payload.data()));
        ClientPlayNetworking.registerGlobalReceiver(ModelPayload.ID, (payload, ctx) -> UnrEnemyRenderer.accept(payload.kind(), payload.data()));

        // Inside the rift, the sky is UNRENDERED's and there is no weather and no cloud.
        DimensionRenderingRegistry.registerSkyRenderer(RiftMod.RIFT, SkyPainter::render);
        DimensionRenderingRegistry.registerCloudRenderer(RiftMod.RIFT, ctx -> { });
        DimensionRenderingRegistry.registerWeatherRenderer(RiftMod.RIFT, ctx -> { });

        WorldRenderEvents.AFTER_ENTITIES.register(SceneRenderer::render);
        WorldRenderEvents.END.register(CaptureService::onFrame);

        ClientTickEvents.END_CLIENT_TICK.register(mc -> {
            if (mc.world == null) {
                SceneRenderer.clear();
                PilotRenderer.clear();
            }
        });
    }
}
