package net.unrendered.rift;

import net.minecraft.block.AbstractBlock;
import net.minecraft.block.Block;
import net.minecraft.block.BlockRenderType;
import net.minecraft.block.BlockState;
import net.minecraft.block.MapColor;
import net.minecraft.block.piston.PistonBehavior;
import net.minecraft.registry.Registries;
import net.minecraft.registry.Registry;
import net.minecraft.sound.BlockSoundGroup;

public final class RiftBlocks {
    /** The water in a glowstone frame. Vanilla's own still-water texture, tinted water blue. */
    public static final RiftPortalBlock RIFT_PORTAL = new RiftPortalBlock(AbstractBlock.Settings.create()
        .noCollision()
        .strength(-1.0f)
        .sounds(BlockSoundGroup.GLASS)
        .luminance(s -> 11)
        .nonOpaque()
        .dropsNothing()
        .pistonBehavior(PistonBehavior.BLOCK)
        .mapColor(MapColor.WATER_BLUE));

    /**
     * UNRENDERED's grid, as Minecraft feels it. You stand on it and can't dig through it,
     * and it draws nothing: what you see underfoot is UNRENDERED's own floor, streamed in.
     */
    public static final Block RIFT_FLOOR = new InvisibleBlock(AbstractBlock.Settings.create()
        .strength(-1.0f, 3600000.0f)
        .nonOpaque()
        .dropsNothing()
        .allowsSpawning((s, w, p, t) -> false)
        .pistonBehavior(PistonBehavior.BLOCK)
        .mapColor(MapColor.BLACK));

    /**
     * The inside of one of UNRENDERED's buildings. Solid, invisible, and diggable. Every one
     * a Minecraft player breaks is reported to UNRENDERED, which is the side that decides
     * when the tower comes down.
     */
    public static final Block RIFT_SOLID = new InvisibleBlock(AbstractBlock.Settings.create()
        .strength(2.5f, 6.0f)
        .nonOpaque()
        .dropsNothing()
        .sounds(BlockSoundGroup.NETHERITE)
        .allowsSpawning((s, w, p, t) -> false)
        .mapColor(MapColor.BLACK));

    private RiftBlocks() {}

    public static void register() {
        Registry.register(Registries.BLOCK, RiftMod.id("rift_portal"), RIFT_PORTAL);
        Registry.register(Registries.BLOCK, RiftMod.id("rift_floor"), RIFT_FLOOR);
        Registry.register(Registries.BLOCK, RiftMod.id("rift_solid"), RIFT_SOLID);
    }

    /** A solid block that draws nothing. */
    public static final class InvisibleBlock extends Block {
        public InvisibleBlock(Settings settings) {
            super(settings);
        }

        @Override
        protected BlockRenderType getRenderType(BlockState state) {
            return BlockRenderType.INVISIBLE;
        }

        @Override
        protected float getAmbientOcclusionLightLevel(BlockState state, net.minecraft.world.BlockView world, net.minecraft.util.math.BlockPos pos) {
            return 1.0f;
        }

        @Override
        protected boolean isTransparent(BlockState state, net.minecraft.world.BlockView world, net.minecraft.util.math.BlockPos pos) {
            return true;
        }
    }
}
