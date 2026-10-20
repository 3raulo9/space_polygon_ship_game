package net.unrendered.rift.item;

import com.google.gson.JsonObject;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.item.ItemStack;
import net.minecraft.sound.SoundCategory;
import net.minecraft.util.Hand;
import net.minecraft.util.TypedActionResult;
import net.minecraft.world.World;
import net.unrendered.rift.RiftMod;
import net.unrendered.rift.RiftSounds;
import net.unrendered.rift.bridge.Bridge;
import net.unrendered.rift.entity.CrabCoreEntity;

/**
 * UNRENDERED's CRAB CORE, thrown the way UNRENDERED throws it. Inside the rift — which is
 * UNRENDERED's world — UNRENDERED throws its own from this player's seat, so it is the very
 * same bomb. Anywhere else it is thrown here, flying and going off exactly as UNRENDERED's
 * does (see {@link CrabCoreEntity}), with UNRENDERED's own sounds.
 */
public class CrabCoreItem extends RelicItem {
    public CrabCoreItem(Settings settings) {
        super(settings);
    }

    @Override
    public TypedActionResult<ItemStack> use(World world, PlayerEntity user, Hand hand) {
        ItemStack stack = user.getStackInHand(hand);
        world.playSound(null, user.getX(), user.getY(), user.getZ(), RiftSounds.CRAB_CORE_THROW,
            SoundCategory.PLAYERS, 1.0f, 1.0f);
        if (!world.isClient) {
            if (world.getRegistryKey() == RiftMod.RIFT && Bridge.linked()) {
                JsonObject use = Bridge.msg("use");
                use.addProperty("uuid", user.getUuidAsString());
                use.addProperty("item", "crab_core");
                Bridge.send(use);
            } else {
                CrabCoreEntity core = new CrabCoreEntity(world, user);
                core.setItem(stack.copyWithCount(1));
                core.setPosition(user.getEyePos().add(user.getRotationVector().multiply(0.6)).subtract(0, 0.25, 0));
                core.launch(user.getRotationVector());
                world.spawnEntity(core);
            }
            stack.decrementUnlessCreative(1, user);
        }
        return TypedActionResult.success(stack, world.isClient);
    }
}
