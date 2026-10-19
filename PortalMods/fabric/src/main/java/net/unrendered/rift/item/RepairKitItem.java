package net.unrendered.rift.item;

import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.item.ItemStack;
import net.minecraft.sound.SoundCategory;
import net.minecraft.sound.SoundEvents;
import net.minecraft.util.Hand;
import net.minecraft.util.TypedActionResult;
import net.minecraft.world.World;

/** Mends the hull. UNRENDERED's only way to get hull back, and here it is health. */
public class RepairKitItem extends RelicItem {
    public RepairKitItem(Settings settings) {
        super(settings);
    }

    @Override
    public TypedActionResult<ItemStack> use(World world, PlayerEntity user, Hand hand) {
        ItemStack stack = user.getStackInHand(hand);
        if (user.getHealth() >= user.getMaxHealth()) return TypedActionResult.fail(stack);
        if (!world.isClient) {
            user.heal(10.0f);
            world.playSound(null, user.getX(), user.getY(), user.getZ(), SoundEvents.BLOCK_ANVIL_USE,
                SoundCategory.PLAYERS, 0.5f, 1.8f);
            stack.decrementUnlessCreative(1, user);
            user.getItemCooldownManager().set(this, 30);
        }
        return TypedActionResult.success(stack, world.isClient);
    }
}
