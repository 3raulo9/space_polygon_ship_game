package net.unrendered.rift;

import com.google.gson.JsonArray;
import net.minecraft.block.BlockState;
import net.minecraft.item.ItemStack;
import net.minecraft.nbt.NbtCompound;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.util.math.BlockPos;

import java.util.List;
import java.util.UUID;

/**
 * What only a Minecraft client can do: read baked models and textures, and render
 * entities exactly as the game draws them. The client entrypoint installs an
 * implementation. A dedicated server has none and falls back to plain shapes.
 */
public final class RiftHooks {
    private RiftHooks() {}

    /** One entity as the server sees it this tick, enough for the client to draw it, or a
     *  stand-in for it if the client doesn't have it loaded. */
    public static final class EntitySnap {
        public int id;
        public String typeId;
        /** Full NBT the first time an entity is sent, so a stand-in can be built. Null after that. */
        public NbtCompound nbt;
        public double x, y, z;
        public float yaw, pitch, bodyYaw, headYaw;
        public float w, h, hp, max;
        public int flags;
        public String name = "";
        public int light = 15;
        public int hurtTime;
        public boolean swinging;
        public int swingTicks;
        public boolean sneaking;
        public float speed;
        /** Mainhand, offhand, feet, legs, chest, head, for living entities; null otherwise. */
        public ItemStack[] equipment;
        /** For players: their profile, so a stand-in wears their skin. */
        public UUID profileId;
        public String profileName;
        /** For item entities. */
        public ItemStack item;
        public int age;
    }

    /** What the render thread is asked to capture next. Replaced whole by the server thread. */
    public record CaptureRequest(long serial, int worldTime, List<EntitySnap> ents, List<EntitySnap> avatars,
                                 List<UUID> avatarOwners, List<EntitySnap> rents) {}

    public interface Client {
        /** Baked-model quads for a block state, in the palette's quad format. Null if unavailable. */
        JsonArray blockQuads(BlockState state, ServerWorld world, BlockPos pos);

        /** A texture as PNG bytes plus its animation frame time, or null. */
        TextureData texture(String id);

        /** The item's own sprite as a texture id, or null. */
        String itemTexture(ItemStack stack);

        /** Hands the render thread the next thing to capture. */
        void request(CaptureRequest request);
    }

    public record TextureData(byte[] png, int frametime) {}

    /** Installed by the client entrypoint; null on a dedicated server. */
    public static volatile Client client;
}
