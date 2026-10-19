package net.unrendered.rift.item;

import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.entity.projectile.ArrowEntity;
import net.minecraft.entity.projectile.PersistentProjectileEntity;
import net.minecraft.item.ItemStack;
import net.minecraft.item.Items;
import net.minecraft.sound.SoundCategory;
import net.minecraft.sound.SoundEvents;
import net.minecraft.util.Hand;
import net.minecraft.util.TypedActionResult;
import net.minecraft.world.World;

/** UNRENDERED's rounds. There's no cannon on this side, so each one is fired off the
 *  palm: a fast, flat bolt. */
public class RoundsItem extends RelicItem {
    public RoundsItem(Settings settings) {
        super(settings);
    }

    @Override
    public TypedActionResult<ItemStack> use(World world, PlayerEntity user, Hand hand) {
        ItemStack stack = user.getStackInHand(hand);
        if (!world.isClient) {
            ArrowEntity bolt = new ArrowEntity(world, user, new ItemStack(Items.ARROW), null);
            bolt.setVelocity(user, user.getPitch(), user.getYaw(), 0.0f, 4.0f, 0.4f);
            bolt.setDamage(3.0);
            bolt.setNoGravity(true);
            bolt.pickupType = PersistentProjectileEntity.PickupPermission.DISALLOWED;
            world.spawnEntity(bolt);
            world.playSound(null, user.getX(), user.getY(), user.getZ(), SoundEvents.ENTITY_FIREWORK_ROCKET_BLAST,
                SoundCategory.PLAYERS, 0.7f, 1.7f);
            stack.decrementUnlessCreative(1, user);
        }
        user.getItemCooldownManager().set(this, 6);
        return TypedActionResult.success(stack, world.isClient);
    }
}
