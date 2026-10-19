package net.unrendered.rift.item;

import net.minecraft.item.Item;
import net.minecraft.item.ItemStack;
import net.minecraft.item.tooltip.TooltipType;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;

import java.util.List;

/** Salvage from UNRENDERED. On this side of the rift it is mostly something to craft with. */
public class RelicItem extends Item {
    public RelicItem(Settings settings) {
        super(settings);
    }

    @Override
    public void appendTooltip(ItemStack stack, TooltipContext context, List<Text> tooltip, TooltipType type) {
        tooltip.add(Text.translatable(getTranslationKey() + ".desc").formatted(Formatting.DARK_PURPLE));
        tooltip.add(Text.translatable("item.unrendered_portal.from_unrendered").formatted(Formatting.DARK_GRAY, Formatting.ITALIC));
    }
}
