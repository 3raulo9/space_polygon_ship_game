package net.unrendered.rift.client;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.block.BlockRenderType;
import net.minecraft.block.BlockState;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.render.model.BakedModel;
import net.minecraft.client.render.model.BakedQuad;
import net.minecraft.client.resource.metadata.AnimationResourceMetadata;
import net.minecraft.client.texture.AbstractTexture;
import net.minecraft.client.texture.NativeImage;
import net.minecraft.client.texture.NativeImageBackedTexture;
import net.minecraft.client.texture.Sprite;
import net.minecraft.item.ItemStack;
import net.minecraft.resource.Resource;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.util.Identifier;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Direction;
import net.minecraft.util.math.random.Random;
import net.minecraft.world.BlockRenderView;
import net.unrendered.rift.RiftHooks;

import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.concurrent.CompletableFuture;

/** The client half of the bridge: models, textures, and the entity capture. */
public final class ClientHooksImpl implements RiftHooks.Client {
    private static final Direction[] CULL = {null, Direction.DOWN, Direction.UP, Direction.NORTH,
        Direction.SOUTH, Direction.WEST, Direction.EAST};

    /** A sprite's own texture, e.g. block/stone to minecraft:textures/block/stone.png. */
    public static String spriteTexture(Sprite sprite) {
        Identifier id = sprite.getContents().getId();
        return id.getNamespace() + ":textures/" + id.getPath() + ".png";
    }

    @Override
    public JsonArray blockQuads(BlockState state, ServerWorld world, BlockPos pos) {
        MinecraftClient mc = MinecraftClient.getInstance();
        JsonArray out = new JsonArray();
        BlockRenderType type = state.getRenderType();
        if (state.isAir() || type == BlockRenderType.INVISIBLE) return out;
        BakedModel model = mc.getBlockRenderManager().getModel(state);
        // Tint is read off the client's own copy of the world when it is the same one.
        BlockRenderView view = mc.world != null && mc.world.getRegistryKey() == world.getRegistryKey() ? mc.world : null;

        if (type != BlockRenderType.MODEL) {
            // Drawn by a block-entity renderer (chests, signs...): a cube in its particle sprite.
            Sprite sprite = model.getParticleSprite();
            for (Direction d : Direction.values()) out.add(cube(d, spriteTexture(sprite)));
            return out;
        }
        Random random = Random.create(42L);
        for (Direction cull : CULL) {
            random.setSeed(42L);
            List<BakedQuad> quads = model.getQuads(state, cull, random);
            for (BakedQuad q : quads) out.add(quad(q, cull, state, view, pos));
        }
        return out;
    }

    private static JsonObject quad(BakedQuad q, Direction cull, BlockState state, BlockRenderView view, BlockPos pos) {
        int[] data = q.getVertexData();
        int stride = data.length / 4;
        Sprite sprite = q.getSprite();
        float u0 = sprite.getMinU(), u1 = sprite.getMaxU(), v0 = sprite.getMinV(), v1 = sprite.getMaxV();
        JsonObject o = new JsonObject();
        o.addProperty("tex", spriteTexture(sprite));
        o.addProperty("cull", cull == null ? -1 : cull.getId());
        if (q.hasColor()) {
            int c = MinecraftClient.getInstance().getBlockColors().getColor(state, view, view == null ? null : pos, q.getColorIndex());
            if (c != -1) {
                JsonArray t = new JsonArray();
                t.add((c >> 16) & 0xFF);
                t.add((c >> 8) & 0xFF);
                t.add(c & 0xFF);
                o.add("tint", t);
            } else {
                o.add("tint", null);
            }
        } else {
            o.add("tint", null);
        }
        o.addProperty("shade", q.hasShade());
        JsonArray v = new JsonArray();
        for (int i = 0; i < 4; i++) {
            int b = i * stride;
            v.add(round(Float.intBitsToFloat(data[b])));
            v.add(round(Float.intBitsToFloat(data[b + 1])));
            v.add(round(Float.intBitsToFloat(data[b + 2])));
            float u = Float.intBitsToFloat(data[b + 4]);
            float vv = Float.intBitsToFloat(data[b + 5]);
            v.add(round((u - u0) / Math.max(1e-6f, u1 - u0)));
            v.add(round((vv - v0) / Math.max(1e-6f, v1 - v0)));
        }
        o.add("v", v);
        return o;
    }

    private static float round(float f) {
        return Math.round(f * 10000f) / 10000f;
    }

    private static JsonObject cube(Direction d, String tex) {
        float[][] p = switch (d) {
            case DOWN -> new float[][]{{0, 0, 1}, {0, 0, 0}, {1, 0, 0}, {1, 0, 1}};
            case UP -> new float[][]{{0, 1, 0}, {0, 1, 1}, {1, 1, 1}, {1, 1, 0}};
            case NORTH -> new float[][]{{1, 1, 0}, {1, 0, 0}, {0, 0, 0}, {0, 1, 0}};
            case SOUTH -> new float[][]{{0, 1, 1}, {0, 0, 1}, {1, 0, 1}, {1, 1, 1}};
            case WEST -> new float[][]{{0, 1, 0}, {0, 0, 0}, {0, 0, 1}, {0, 1, 1}};
            case EAST -> new float[][]{{1, 1, 1}, {1, 0, 1}, {1, 0, 0}, {1, 1, 0}};
        };
        float[][] uv = {{0, 0}, {0, 1}, {1, 1}, {1, 0}};
        JsonObject o = new JsonObject();
        o.addProperty("tex", tex);
        o.addProperty("cull", d.getId());
        o.add("tint", null);
        o.addProperty("shade", true);
        JsonArray v = new JsonArray();
        for (int i = 0; i < 4; i++) {
            v.add(p[i][0]);
            v.add(p[i][1]);
            v.add(p[i][2]);
            v.add(uv[i][0]);
            v.add(uv[i][1]);
        }
        o.add("v", v);
        return o;
    }

    @Override
    public RiftHooks.TextureData texture(String id) {
        Identifier ident = Identifier.tryParse(id);
        if (ident == null) return null;
        MinecraftClient mc = MinecraftClient.getInstance();
        Optional<Resource> res = mc.getResourceManager().getResource(ident);
        if (res.isPresent()) {
            try (InputStream in = res.get().getInputStream()) {
                byte[] png = in.readAllBytes();
                int frametime = 1;
                try {
                    frametime = res.get().getMetadata().decode(AnimationResourceMetadata.READER)
                        .map(AnimationResourceMetadata::getDefaultFrameTime).orElse(1);
                } catch (IOException | RuntimeException ignored) {
                }
                return new RiftHooks.TextureData(png, frametime);
            } catch (IOException e) {
                return null;
            }
        }
        // A downloaded skin lives in the skin cache, named by its hash.
        if (ident.getPath().startsWith("skins/")) {
            String hash = ident.getPath().substring("skins/".length());
            for (Path dir : skinDirs(mc)) {
                Path f = dir.resolve(hash.length() > 2 ? hash.substring(0, 2) : hash).resolve(hash);
                if (Files.isRegularFile(f)) {
                    try {
                        return new RiftHooks.TextureData(Files.readAllBytes(f), 1);
                    } catch (IOException ignored) {
                    }
                }
            }
        }
        // Anything else that only exists on the GPU side as a dynamic texture (maps...).
        try {
            CompletableFuture<byte[]> png = new CompletableFuture<>();
            mc.execute(() -> {
                try {
                    AbstractTexture t = mc.getTextureManager().getTexture(ident);
                    if (t instanceof NativeImageBackedTexture n && n.getImage() != null) {
                        NativeImage img = n.getImage();
                        Path tmp = Files.createTempFile("rift", ".png");
                        img.writeTo(tmp);
                        png.complete(Files.readAllBytes(tmp));
                        Files.deleteIfExists(tmp);
                    } else {
                        png.complete(null);
                    }
                } catch (Exception e) {
                    png.complete(null);
                }
            });
            byte[] bytes = png.get(2, java.util.concurrent.TimeUnit.SECONDS);
            return bytes == null ? null : new RiftHooks.TextureData(bytes, 1);
        } catch (Exception e) {
            return null;
        }
    }

    private static List<Path> skinDirs(MinecraftClient mc) {
        List<Path> dirs = new ArrayList<>();
        dirs.add(mc.runDirectory.toPath().resolve("assets").resolve("skins"));
        dirs.add(FabricLoader.getInstance().getGameDir().resolve("assets").resolve("skins"));
        String appdata = System.getenv("APPDATA");
        if (appdata != null) dirs.add(Path.of(appdata, ".minecraft", "assets", "skins"));
        String home = System.getProperty("user.home");
        if (home != null) {
            dirs.add(Path.of(home, ".minecraft", "assets", "skins"));
            dirs.add(Path.of(home, "Library", "Application Support", "minecraft", "assets", "skins"));
        }
        return dirs;
    }

    @Override
    public String itemTexture(ItemStack stack) {
        MinecraftClient mc = MinecraftClient.getInstance();
        BakedModel model = mc.getItemRenderer().getModel(stack, mc.world, null, 0);
        Sprite sprite = model.getParticleSprite();
        return sprite == null ? null : spriteTexture(sprite);
    }

    @Override
    public void request(RiftHooks.CaptureRequest request) {
        CaptureService.request(request);
    }
}
