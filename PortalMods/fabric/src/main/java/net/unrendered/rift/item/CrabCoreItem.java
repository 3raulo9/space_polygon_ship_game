package net.unrendered.rift.item;

import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.item.ItemStack;
import net.minecraft.sound.SoundCategory;
import net.minecraft.sound.SoundEvents;
import net.minecraft.util.Hand;
import net.minecraft.util.TypedActionResult;
import net.minecraft.world.World;
import net.unrendered.rift.entity.CrabCoreEntity;
import net.unrendered.rift.RiftMod;
import net.unrendered.rift.bridge.Bridge;
import com.google.gson.JsonObject;

/** The Crab-Core's heart. Thrown, it goes off the way it does in UNRENDERED: a ring of
 *  force that flattens what stands near it and leaves the ground alone. */
public class CrabCoreItem extends RelicItem {
    public CrabCoreItem(Settings settings) {
        super(settings);
    }

    @Override
    public TypedActionResult<ItemStack> use(World world, PlayerEntity user, Hand hand) {
        ItemStack stack = user.getStackInHand(hand);
        // Inside UNRENDERED's world it is UNRENDERED's crab core: UNRENDERED throws its own,
        // from this player's seat, with its own blast.
        if (world.getRegistryKey() == RiftMod.RIFT && Bridge.linked()) {
            if (!world.isClient) {
                JsonObject use = Bridge.msg("use");
                use.addProperty("uuid", user.getUuidAsString());
                use.addProperty("item", "crab_core");
                Bridge.send(use);
                stack.decrementUnlessCreative(1, user);
            }
            world.playSound(null, user.getX(), user.getY(), user.getZ(), SoundEvents.ENTITY_SNOWBALL_THROW,
                SoundCategory.PLAYERS, 0.6f, 0.5f);
            user.getItemCooldownManager().set(this, 20);
            return TypedActionResult.success(stack, world.isClient);
        }
        world.playSound(null, user.getX(), user.getY(), user.getZ(), SoundEvents.ENTITY_SNOWBALL_THROW,
            SoundCategory.PLAYERS, 0.6f, 0.5f);
        if (!world.isClient) {
            CrabCoreEntity core = new CrabCoreEntity(world, user);
            core.setItem(stack.copyWithCount(1));
            core.setVelocity(user, user.getPitch(), user.getYaw(), 0.0f, 1.3f, 1.0f);
            world.spawnEntity(core);
            stack.decrementUnlessCreative(1, user);
        }
        user.getItemCooldownManager().set(this, 20);
        return TypedActionResult.success(stack, world.isClient);
    }
}
