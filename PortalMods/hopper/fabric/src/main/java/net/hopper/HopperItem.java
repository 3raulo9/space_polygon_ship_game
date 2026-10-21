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
import net.minecraft.util.Identifier;
import net.minecraft.util.TypedActionResult;
import net.minecraft.world.World;

import java.util.List;

/** The gun. Sneak-use dials, use fires. */
public class HopperItem extends Item {
    public HopperItem(Settings settings) {
        super(settings);
    }

    @Override
    public TypedActionResult<ItemStack> use(World world, PlayerEntity user, Hand hand) {
        ItemStack stack = user.getStackInHand(hand);
        if (user instanceof ServerPlayerEntity player && world instanceof ServerWorld sw) {
            if (player.isSneaking()) HopperMod.showDial(player, stack);
            // The cooldown is the recoil: the client plays the kick off it (see HopperClient).
            else if (HopperMod.fire(sw, player, stack)) player.getItemCooldownManager().set(this, 10);
        }
        return TypedActionResult.success(stack, world.isClient());
    }

    @Override
    public void appendTooltip(ItemStack stack, TooltipContext context, List<Text> tooltip, TooltipType type) {
        String dest = HopperMod.destination(stack);
        String shown;
        if (dest.isEmpty()) shown = "not dialled";
        else if (dest.startsWith("realm:")) shown = dest.substring(6).toUpperCase();
        else shown = Realms.friendly(Identifier.of(dest));
        tooltip.add(Text.literal("Dialled: " + shown).formatted(Formatting.GREEN));
        String at = HopperMod.coords(stack);
        if (!at.isEmpty() && !dest.startsWith("realm:"))
            tooltip.add(Text.literal("At: " + at.replace(",", ", ").replaceAll("(^|, )(?=,|$)", "$1~")).formatted(Formatting.DARK_GREEN));
        tooltip.add(Text.translatable("item.dimension_hopper.hopper.desc").formatted(Formatting.GRAY));
    }
}
