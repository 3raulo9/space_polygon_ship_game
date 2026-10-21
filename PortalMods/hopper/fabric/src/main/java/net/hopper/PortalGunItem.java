package net.hopper;

import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.item.Item;
import net.minecraft.item.ItemStack;
import net.minecraft.item.tooltip.TooltipType;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;
import net.minecraft.util.Hand;
import net.minecraft.util.TypedActionResult;
import net.minecraft.world.World;

import java.util.List;

/** The Portal Gun, before it has a computer: it fires, and where the portal goes is up to it. */
public class PortalGunItem extends Item {
    public PortalGunItem(Settings settings) {
        super(settings);
    }

    @Override
    public TypedActionResult<ItemStack> use(World world, PlayerEntity user, Hand hand) {
        ItemStack stack = user.getStackInHand(hand);
        if (user instanceof ServerPlayerEntity player && world instanceof ServerWorld sw) {
            if (player.isSneaking()) noScreen(player);
            // The cooldown is the recoil: the client plays the kick off it (see HopperClient).
            else if (HopperMod.fireBlind(sw, player, stack)) player.getItemCooldownManager().set(this, 10);
        }
        return TypedActionResult.success(stack, world.isClient());
    }

    public static void noScreen(PlayerEntity player) {
        player.sendMessage(Text.translatable("item.dimension_hopper.portal_gun.no_screen").formatted(Formatting.GRAY), true);
    }

    @Override
    public void appendTooltip(ItemStack stack, TooltipContext context, List<Text> tooltip, TooltipType type) {
        tooltip.add(Text.translatable("item.dimension_hopper.portal_gun.desc").formatted(Formatting.GRAY));
    }
}
