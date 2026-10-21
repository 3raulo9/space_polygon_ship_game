package net.hopper.scatter;

import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.effect.StatusEffect;
import net.minecraft.entity.effect.StatusEffectCategory;
import net.minecraft.server.world.ServerWorld;

/**
 * Unstable teleportation: what a concentrated teleportation potion does to whoever drinks it.
 * The timeline lives in {@link Scatter}; this only hands it a tick.
 */
public final class ScatterEffect extends StatusEffect {
    public ScatterEffect() {
        super(StatusEffectCategory.HARMFUL, 0x7A2BE2);
    }

    @Override
    public boolean canApplyUpdateEffect(int duration, int amplifier) {
        return true;
    }

    @Override
    public boolean applyUpdateEffect(LivingEntity entity, int amplifier) {
        // False ends the effect: the body is gone, or the timeline ran out.
        return !(entity.getWorld() instanceof ServerWorld w) || Scatter.tick(w, entity);
    }
}
