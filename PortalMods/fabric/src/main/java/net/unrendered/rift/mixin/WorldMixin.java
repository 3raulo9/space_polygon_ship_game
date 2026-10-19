package net.unrendered.rift.mixin;

import net.minecraft.block.BlockState;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;
import net.unrendered.rift.RiftPilot;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Tells the rift about block changes, so a pilot sees a block go the moment it goes. */
@Mixin(World.class)
public abstract class WorldMixin {
    @Inject(method = "setBlockState(Lnet/minecraft/util/math/BlockPos;Lnet/minecraft/block/BlockState;II)Z", at = @At("RETURN"))
    private void rift$blockChanged(BlockPos pos, BlockState state, int flags, int maxUpdateDepth,
                                   CallbackInfoReturnable<Boolean> cir) {
        World self = (World) (Object) this;
        if (!self.isClient && cir.getReturnValueZ()) RiftPilot.onBlockChanged(self, pos);
    }
}
