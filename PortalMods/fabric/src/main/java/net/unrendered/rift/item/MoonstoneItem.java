package net.unrendered.rift.item;

import net.minecraft.entity.effect.StatusEffectInstance;
import net.minecraft.entity.effect.StatusEffects;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.item.ItemStack;
import net.minecraft.sound.SoundCategory;
import net.minecraft.sound.SoundEvents;
import net.minecraft.util.Hand;
import net.minecraft.util.TypedActionResult;
import net.minecraft.world.World;

/** A piece of the moon. In UNRENDERED it is the one item that does everything at once —
 *  every shield charge, the hull whole, the reserve full — and it refuses to be wasted on a
 *  craft that is already whole. Here: every heart, the shield (absorption) full, and the
 *  hunger bar full, refused the same way. */
public class MoonstoneItem extends RelicItem {
    public MoonstoneItem(Settings settings) {
        super(settings);
    }

    @Override
    public TypedActionResult<ItemStack> use(World world, PlayerEntity user, Hand hand) {
        ItemStack stack = user.getStackInHand(hand);
        boolean needed = user.getHealth() < user.getMaxHealth() || user.getAbsorptionAmount() < 8f
            || user.getHungerManager().isNotFull();
        if (!needed) return TypedActionResult.fail(stack);
        if (!world.isClient) {
            user.setHealth(user.getMaxHealth());
            user.addStatusEffect(new StatusEffectInstance(StatusEffects.ABSORPTION, 20 * 120, 1));
            user.getHungerManager().add(20, 20f);
            world.playSound(null, user.getX(), user.getY(), user.getZ(), SoundEvents.BLOCK_AMETHYST_BLOCK_RESONATE,
                SoundCategory.PLAYERS, 1.0f, 0.8f);
            stack.decrementUnlessCreative(1, user);
        }
        return TypedActionResult.success(stack, world.isClient);
    }
}
