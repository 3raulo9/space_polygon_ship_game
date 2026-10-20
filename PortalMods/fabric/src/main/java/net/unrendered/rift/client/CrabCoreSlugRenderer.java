package net.unrendered.rift.client;

import net.minecraft.client.render.RenderLayer;
import net.minecraft.client.render.VertexConsumerProvider;
import net.minecraft.client.render.entity.EntityRenderer;
import net.minecraft.client.render.entity.EntityRendererFactory;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.util.Identifier;
import net.minecraft.util.math.Vec3d;
import net.unrendered.rift.UnrModels;
import net.unrendered.rift.entity.CrabCoreEntity;

/** A thrown crab core in flight, as UNRENDERED draws one: its grenade slug, a small hot
 *  octahedron in the elite orange, shaded the way UNRENDERED shades it. */
public final class CrabCoreSlugRenderer extends EntityRenderer<CrabCoreEntity> {
    private static final Identifier NONE = Identifier.ofVanilla("textures/misc/white.png");

    public CrabCoreSlugRenderer(EntityRendererFactory.Context ctx) {
        super(ctx);
    }

    @Override
    public void render(CrabCoreEntity e, float yaw, float tickDelta, MatrixStack matrices,
                       VertexConsumerProvider buffers, int light) {
        Vec3d eye = dispatcher.camera.getPos().subtract(e.getLerpedPos(tickDelta));
        matrices.push();
        matrices.translate(0, e.getHeight() / 2f, 0);
        UnrMeshes.emit(buffers.getBuffer(RenderLayer.getDebugQuads()), matrices.peek().getPositionMatrix(),
            UnrModels.slug(), 1f, 0f, 0f, (float) eye.x, (float) eye.y - e.getHeight() / 2f, (float) eye.z, 0, 1f);
        matrices.pop();
    }

    @Override
    public Identifier getTexture(CrabCoreEntity entity) {
        return NONE;
    }
}
