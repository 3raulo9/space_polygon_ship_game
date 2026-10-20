package net.unrendered.rift.client;

import net.fabricmc.fabric.api.client.rendering.v1.BuiltinItemRendererRegistry;
import net.minecraft.client.render.RenderLayer;
import net.minecraft.client.render.VertexConsumerProvider;
import net.minecraft.client.render.model.json.ModelTransformationMode;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.item.Item;
import net.minecraft.item.ItemStack;
import net.minecraft.util.Util;
import net.minecraft.util.math.RotationAxis;
import net.unrendered.rift.RiftItems;
import net.unrendered.rift.UnrModels;

/**
 * UNRENDERED's items in Minecraft are UNRENDERED's own models: the pack icon's polygons,
 * framed and shaded exactly as UNRENDERED's pack frames and shades them. In an inventory slot
 * it turns slowly on the spot at the pack's own pace, as it does in UNRENDERED's panel; in a
 * hand, on the ground or in a frame it is the same solid, standing still.
 */
public final class UnrItemRenderer implements BuiltinItemRendererRegistry.DynamicItemRenderer {
    /** UNRENDERED's icon camera, relative to the item it looks at. */
    private static final float EYE_Y = 0.9f, EYE_Z = -3.6f;
    /** Its view is about 1.53 m tall either side of centre at that distance; that half-height
     *  fills half of Minecraft's one-block item space. */
    private static final float FIT = 0.5f / 1.53f;
    /** The pack's idle turn, radians a second. */
    private static final float SPIN = 0.8f;

    private final String id;

    private UnrItemRenderer(String id) {
        this.id = id;
    }

    public static void registerAll() {
        RiftItems.BY_UNR_ID.forEach((id, item) -> {
            if (UnrModels.item(id) != null) BuiltinItemRendererRegistry.INSTANCE.register(item, new UnrItemRenderer(id));
        });
    }

    @Override
    public void render(ItemStack stack, ModelTransformationMode mode, MatrixStack matrices,
                       VertexConsumerProvider buffers, int light, int overlay) {
        UnrModels.Mesh mesh = UnrModels.item(id);
        if (mesh == null) return;
        boolean gui = mode == ModelTransformationMode.GUI;
        float heading = gui ? (Util.getMeasuringTimeMs() % 1_000_000L) / 1000f * SPIN : 0f;

        matrices.push();
        matrices.translate(0.5f, 0.5f, 0.5f);
        // UNRENDERED's icon camera looks from -Z and a little above; Minecraft's from +Z.
        // Turned half round and tipped toward the viewer, the same faces show.
        matrices.multiply(RotationAxis.POSITIVE_X.rotationDegrees((float) Math.toDegrees(Math.atan2(EYE_Y, -EYE_Z))));
        matrices.multiply(RotationAxis.POSITIVE_Y.rotationDegrees(180f));
        matrices.scale(FIT, FIT, FIT);
        UnrMeshes.emit(buffers.getBuffer(RenderLayer.getDebugQuads()), matrices.peek().getPositionMatrix(), mesh,
            mesh.scale(), heading, mesh.centerY() * mesh.scale(), 0f, EYE_Y, EYE_Z, 0, 1f);
        matrices.pop();
    }

    /** For anything that wants to draw one of these items outside an item stack. */
    public static UnrModels.Mesh meshOf(Item item) {
        for (var e : RiftItems.BY_UNR_ID.entrySet()) if (e.getValue() == item) return UnrModels.item(e.getKey());
        return null;
    }
}
