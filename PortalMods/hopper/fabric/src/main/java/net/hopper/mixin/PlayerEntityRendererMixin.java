package net.hopper.mixin;

import net.hopper.client.ScatterClient;
import net.minecraft.client.network.AbstractClientPlayerEntity;
import net.minecraft.client.render.entity.PlayerEntityRenderer;
import net.minecraft.client.render.entity.model.PlayerEntityModel;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** A player coming apart: the limbs that have teleported away are not drawn — in third
 *  person, to everyone watching, and in first person, the arm that is gone. */
@Mixin(PlayerEntityRenderer.class)
public abstract class PlayerEntityRendererMixin {
    @Inject(method = "setModelPose", at = @At("TAIL"))
    private void hopper$scattered(AbstractClientPlayerEntity player, CallbackInfo ci) {
        int stage = ScatterClient.stage(player.getId());
        if (stage <= 0) return;
        PlayerEntityModel<AbstractClientPlayerEntity> m = ((PlayerEntityRenderer) (Object) this).getModel();
        if (stage >= 1) {
            m.leftArm.visible = false;
            m.leftSleeve.visible = false;
        }
        if (stage >= 2) {
            m.rightArm.visible = false;
            m.rightSleeve.visible = false;
        }
        if (stage >= 3) {
            m.leftLeg.visible = false;
            m.leftPants.visible = false;
        }
        if (stage >= 4) {
            m.rightLeg.visible = false;
            m.rightPants.visible = false;
        }
    }
}
