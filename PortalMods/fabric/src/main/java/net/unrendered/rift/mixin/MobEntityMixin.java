package net.unrendered.rift.mixin;

import net.minecraft.entity.EntityType;
import net.minecraft.entity.ai.goal.ActiveTargetGoal;
import net.minecraft.entity.ai.goal.GoalSelector;
import net.minecraft.entity.mob.MobEntity;
import net.minecraft.entity.mob.Monster;
import net.minecraft.world.World;
import net.unrendered.rift.entity.PilotEntity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Every monster in Minecraft also hunts UNRENDERED pilots, the same way it hunts players. */
@Mixin(MobEntity.class)
public abstract class MobEntityMixin {
    @Shadow
    protected GoalSelector targetSelector;

    @Inject(method = "<init>", at = @At("TAIL"))
    private void rift$huntPilots(EntityType<? extends MobEntity> type, World world, CallbackInfo ci) {
        MobEntity self = (MobEntity) (Object) this;
        if (world == null || world.isClient || !(self instanceof Monster) || self instanceof PilotEntity) return;
        targetSelector.add(3, new ActiveTargetGoal<>(self, PilotEntity.class, true));
    }
}
