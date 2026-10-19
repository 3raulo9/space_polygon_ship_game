package net.unrendered.rift.mixin;

import net.minecraft.block.BlockState;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;
import net.unrendered.rift.RiftBlocks;
import net.unrendered.rift.RiftBuilt;
import net.unrendered.rift.RiftCity;
import net.unrendered.rift.RiftMod;
import net.unrendered.rift.RiftPilot;
import net.unrendered.rift.RiftVisitors;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Tells the rift about block changes: so a pilot sees a block go the moment it goes, so
 * UNRENDERED sees what visitors build, and so a piece of UNRENDERED's city broken by
 * anything at all (a pickaxe, TNT, a creeper) is reported as mined.
 */
@Mixin(World.class)
public abstract class WorldMixin {
    @Inject(method = "setBlockState(Lnet/minecraft/util/math/BlockPos;Lnet/minecraft/block/BlockState;II)Z", at = @At("HEAD"))
    private void rift$beforeChange(BlockPos pos, BlockState state, int flags, int maxUpdateDepth,
                                   CallbackInfoReturnable<Boolean> cir) {
        World self = (World) (Object) this;
        if (self.isClient || RiftCity.writing || self.getRegistryKey() != RiftMod.RIFT) return;
        if (state.isOf(RiftBlocks.RIFT_SOLID)) return;
        if (self.getBlockState(pos).isOf(RiftBlocks.RIFT_SOLID)) RiftVisitors.onCityBroken(self, pos);
    }

    @Inject(method = "setBlockState(Lnet/minecraft/util/math/BlockPos;Lnet/minecraft/block/BlockState;II)Z", at = @At("RETURN"))
    private void rift$blockChanged(BlockPos pos, BlockState state, int flags, int maxUpdateDepth,
                                   CallbackInfoReturnable<Boolean> cir) {
        World self = (World) (Object) this;
        if (self.isClient || !cir.getReturnValueZ()) return;
        RiftPilot.onBlockChanged(self, pos);
        RiftBuilt.onBlockChanged(self, pos);
    }
}
