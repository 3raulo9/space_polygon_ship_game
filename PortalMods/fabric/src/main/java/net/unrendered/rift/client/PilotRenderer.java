package net.unrendered.rift.client;

import net.minecraft.client.render.VertexConsumerProvider;
import net.minecraft.client.render.entity.EntityRenderer;
import net.minecraft.client.render.entity.EntityRendererFactory;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.util.Identifier;
import net.unrendered.rift.RiftMod;
import net.unrendered.rift.bridge.Wire;
import net.unrendered.rift.entity.PilotEntity;

import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Draws an UNRENDERED pilot exactly as UNRENDERED draws their craft. The vertices arrive
 * already turned to the craft's heading and relative to its feet, so they are only moved
 * to where the pilot stands.
 */
public final class PilotRenderer extends EntityRenderer<PilotEntity> {
    private static final Map<Integer, DrawList> crafts = new ConcurrentHashMap<>();
    private static final Identifier NONE = Identifier.ofVanilla("textures/misc/white.png");

    public PilotRenderer(EntityRendererFactory.Context ctx) {
        super(ctx);
        shadowRadius = 0.6f;
    }

    /** A `craft` arrived for pilot entity {@code id} (still deflated). */
    public static void accept(int id, byte[] packed) {
        try {
            crafts.put(id, DrawList.read(new Wire.In(Wire.inflate(packed))));
        } catch (Exception e) {
            RiftMod.LOG.debug("Rift: bad craft: {}", e.toString());
        }
    }

    @Override
    public void render(PilotEntity entity, float yaw, float tickDelta, MatrixStack matrices,
                       VertexConsumerProvider vertexConsumers, int light) {
        DrawList craft = crafts.get(entity.getId());
        if (craft != null) craft.render(matrices.peek().getPositionMatrix(), 0, 0, 0);
        super.render(entity, yaw, tickDelta, matrices, vertexConsumers, light);
    }

    @Override
    public Identifier getTexture(PilotEntity entity) {
        return NONE;
    }

    public static void clear() {
        crafts.clear();
    }
}
