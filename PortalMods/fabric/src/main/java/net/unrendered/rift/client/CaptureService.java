package net.unrendered.rift.client;

import com.google.gson.JsonObject;
import com.mojang.authlib.GameProfile;
import net.fabricmc.fabric.api.client.rendering.v1.WorldRenderContext;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.network.OtherClientPlayerEntity;
import net.minecraft.client.render.LightmapTextureManager;
import net.minecraft.client.render.RenderLayer;
import net.minecraft.client.render.VertexConsumer;
import net.minecraft.client.render.VertexConsumerProvider;
import net.minecraft.client.render.VertexFormat;
import net.minecraft.client.render.entity.EntityRenderDispatcher;
import net.minecraft.client.texture.Sprite;
import net.minecraft.client.texture.SpriteAtlasTexture;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.client.world.ClientWorld;
import net.minecraft.entity.Entity;
import net.minecraft.entity.EntityPose;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.EquipmentSlot;
import net.minecraft.entity.ItemEntity;
import net.minecraft.entity.LivingEntity;
import net.minecraft.registry.Registries;
import net.minecraft.util.Identifier;
import net.minecraft.util.math.MathHelper;
import net.unrendered.rift.RiftCapture;
import net.unrendered.rift.RiftHooks;
import net.unrendered.rift.RiftMod;
import net.unrendered.rift.bridge.Bridge;
import net.unrendered.rift.bridge.Wire;

import java.lang.reflect.Field;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.IdentityHashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Renders entities into a capture instead of the screen: every vertex Minecraft's own
 * entity renderers emit, with the texture each layer was bound to. That covers mob models,
 * player skins, armour, held items and modded entities. The result is what UNRENDERED
 * draws, so a zombie in UNRENDERED is exactly the zombie Minecraft would draw.
 *
 * <p>Runs on the render thread, once per request from the server thread. An entity the
 * client hasn't loaded (the host is somewhere else) is drawn through a stand-in built from
 * the server's copy.
 */
public final class CaptureService {
    private static final EquipmentSlot[] SLOTS = {
        EquipmentSlot.MAINHAND, EquipmentSlot.OFFHAND, EquipmentSlot.FEET,
        EquipmentSlot.LEGS, EquipmentSlot.CHEST, EquipmentSlot.HEAD,
    };
    private static final Pattern NAME = Pattern.compile("^RenderLayer\\[([^:\\]]+)");
    private static final java.util.Set<String> skipped = new java.util.HashSet<>();
    private static final Pattern TEXTURE = Pattern.compile("Optional\\[([a-z0-9_.\\-]+:[a-z0-9_./\\-]+)\\]");

    private static volatile RiftHooks.CaptureRequest pending;
    private static long done;

    // What the captures have been producing, logged now and then: the one way to know from a
    // log whether Minecraft's models are really reaching UNRENDERED.
    private static int requests, failures;
    private static final StringBuilder report = new StringBuilder();

    private static final Map<Integer, Entity> mirrors = new HashMap<>();
    private static final Map<UUID, OtherClientPlayerEntity> playerMirrors = new HashMap<>();
    private static ClientWorld mirrorWorld;
    private static final Map<Identifier, AtlasIndex> atlases = new HashMap<>();

    private CaptureService() {}

    public static void request(RiftHooks.CaptureRequest r) {
        pending = r;
    }

    /** Called at the end of every rendered frame. */
    public static void onFrame(WorldRenderContext ctx) {
        RiftHooks.CaptureRequest r = pending;
        if (r == null || r.serial() == done) return;
        done = r.serial();
        MinecraftClient mc = MinecraftClient.getInstance();
        ClientWorld world = mc.world;
        if (world != mirrorWorld) {
            mirrors.clear();
            playerMirrors.clear();
            atlases.clear();
            mirrorWorld = world;
        }
        if (world == null) return;
        float tickDelta = ctx.tickCounter().getTickDelta(false);
        EntityRenderDispatcher dispatcher = mc.getEntityRenderDispatcher();
        boolean shadows = mc.options.getEntityShadows().getValue();
        boolean hitboxes = dispatcher.shouldRenderHitboxes();
        dispatcher.setRenderShadows(false);
        dispatcher.setRenderHitboxes(false);
        try {
            boolean tell = ++requests % 40 == 1;
            report.setLength(0);
            if (r.ents() != null) {
                Wire.Out out = new Wire.Out(64 * 1024);
                out.u16(r.ents().size());
                for (RiftHooks.EntitySnap s : r.ents()) capture(out, s, world, dispatcher, tickDelta, 0, 0);
                if (tell) RiftMod.LOG.info("RIFT-E2E: ents capture #{}: {} entities, {} bytes [{}] failures so far {}",
                    requests, r.ents().size(), out.size(), report, failures);
                JsonObject h = new JsonObject();
                h.addProperty("time", r.worldTime());
                Bridge.sendBin("ents", h, out.toArray());
            }
            if (r.rents() != null) {
                // The rift dimension's own creatures, in UNRENDERED's frame (the rift's floor is its grid).
                Wire.Out out = new Wire.Out(64 * 1024);
                out.u16(r.rents().size());
                for (RiftHooks.EntitySnap s : r.rents()) capture(out, s, world, dispatcher, tickDelta, RiftMod.FLOOR_Y, 1);
                if (tell) RiftMod.LOG.info("RIFT-E2E: rents capture: {} entities", r.rents().size());
                Bridge.sendBin("rents", new JsonObject(), out.toArray());
            }
            if (tell && !r.avatars().isEmpty()) report.setLength(0);
            for (int i = 0; i < r.avatars().size(); i++) {
                RiftHooks.EntitySnap s = r.avatars().get(i);
                Wire.Out out = new Wire.Out(32 * 1024);
                // The avatar's position travels in UNRENDERED's frame: the rift's floor is its grid.
                capture(out, s, world, dispatcher, tickDelta, RiftMod.FLOOR_Y, 1);
                JsonObject h = new JsonObject();
                h.addProperty("uuid", r.avatarOwners().get(i).toString());
                Bridge.sendBin("avatar", h, out.toArray());
            }
            if (tell && !r.avatars().isEmpty())
                RiftMod.LOG.info("RIFT-E2E: avatar capture: {} avatars [{}]", r.avatars().size(), report);
        } finally {
            dispatcher.setRenderShadows(shadows);
            dispatcher.setRenderHitboxes(hitboxes);
        }
        // Stand-ins nobody asked about for a while are let go.
        if (mirrors.size() > 512) mirrors.clear();
    }

    private static void capture(Wire.Out out, RiftHooks.EntitySnap s, ClientWorld world, EntityRenderDispatcher dispatcher,
                                float tickDelta, double floorY, int mode) {
        Entity e = resolve(s, world);
        Capture cap = new Capture();
        double x = s.x, y = s.y, z = s.z;
        if (e != null) {
            boolean live = e.getWorld() == world && e.getId() == s.id && !mirrors.containsValue(e) && !playerMirrors.containsValue(e);
            float yaw = live ? MathHelper.lerp(tickDelta, e.prevYaw, e.getYaw()) : s.yaw;
            if (live) {
                x = MathHelper.lerp(tickDelta, e.prevX, e.getX());
                y = MathHelper.lerp(tickDelta, e.prevY, e.getY());
                z = MathHelper.lerp(tickDelta, e.prevZ, e.getZ());
            }
            try {
                dispatcher.render(e, 0.0, 0.0, 0.0, yaw, live ? tickDelta : 1.0f, new MatrixStack(), cap,
                    LightmapTextureManager.MAX_LIGHT_COORDINATE);
            } catch (Throwable t) {
                cap.layers.clear();
                if (failures++ < 8) RiftMod.LOG.warn("RIFT-E2E: rendering {} into the capture failed", s.typeId, t);
            }
        }
        if (report.length() < 600) {
            report.append(s.typeId).append(e == null ? "(no entity)" : "").append(':');
            for (Map.Entry<String, Layer> l : cap.layers.entrySet())
                report.append(' ').append(l.getKey().isEmpty() ? "<none>" : l.getKey()).append('x').append(l.getValue().count / 4);
            report.append("; ");
        }
        if (cap.layers.isEmpty()) {
            // Nothing drawable: a box the size of its hitbox.
            RiftHooks.EntitySnap shifted = shift(s, floorY);
            RiftCapture.writeBox(out, shifted);
            return;
        }
        float bright = 0.22f + 0.78f * s.light / 15f;
        RiftHooks.EntitySnap at = shift(s, floorY);
        at.x = x;
        at.y = y - floorY;
        at.z = z;
        RiftCapture.writeHeader(out, at, 0, 0, 0, cap.layers.size());
        for (Map.Entry<String, Layer> l : cap.layers.entrySet()) {
            Layer layer = l.getValue();
            int quads = layer.count / 4;
            out.str(l.getKey());
            out.u32(quads);
            for (int v = 0; v < quads * 4; v++) {
                out.f32(layer.pos[v * 3]).f32(layer.pos[v * 3 + 1]).f32(layer.pos[v * 3 + 2]);
                out.f32(layer.uv[v * 2]).f32(layer.uv[v * 2 + 1]);
                int c = layer.col[v];
                int r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF, a = (c >>> 24) & 0xFF;
                if (layer.hurt[v]) {
                    g = (int) (g * 0.55f);
                    b = (int) (b * 0.55f);
                }
                out.u8(Math.min(255, (int) (r * bright))).u8(Math.min(255, (int) (g * bright)))
                    .u8(Math.min(255, (int) (b * bright))).u8(a);
            }
        }
    }

    private static RiftHooks.EntitySnap shift(RiftHooks.EntitySnap s, double floorY) {
        RiftHooks.EntitySnap c = new RiftHooks.EntitySnap();
        c.id = s.id;
        c.typeId = s.typeId;
        c.x = s.x;
        c.y = s.y - floorY;
        c.z = s.z;
        c.w = s.w;
        c.h = s.h;
        c.hp = s.hp;
        c.max = s.max;
        c.flags = s.flags;
        c.name = s.name;
        c.light = s.light;
        return c;
    }

    /** The client's own copy of the entity if it has one here, otherwise a stand-in posed from the server. */
    private static Entity resolve(RiftHooks.EntitySnap s, ClientWorld world) {
        MinecraftClient mc = MinecraftClient.getInstance();
        if (s.profileId != null) {
            if (mc.player != null && mc.player.getUuid().equals(s.profileId) && near(mc.player, s)) return mc.player;
            var other = world.getPlayerByUuid(s.profileId);
            if (other != null && near(other, s)) return other;
            OtherClientPlayerEntity m = playerMirrors.computeIfAbsent(s.profileId,
                id -> new OtherClientPlayerEntity(world, new GameProfile(id, s.profileName == null ? "" : s.profileName)));
            pose(m, s);
            return m;
        }
        Entity own = world.getEntityById(s.id);
        if (own != null && near(own, s) && Registries.ENTITY_TYPE.getId(own.getType()).toString().equals(s.typeId)) return own;

        Entity m = mirrors.get(s.id);
        if (m == null || s.nbt != null) {
            EntityType<?> type = Registries.ENTITY_TYPE.get(Identifier.tryParse(s.typeId));
            if (type == null) return null;
            m = type.create(world);
            if (m == null) return null;
            if (s.nbt != null) {
                try {
                    m.readNbt(s.nbt);
                } catch (RuntimeException ignored) {
                }
            } else {
                RiftCapture.needNbt.add(s.id);
            }
            mirrors.put(s.id, m);
        }
        pose(m, s);
        return m;
    }

    private static boolean near(Entity e, RiftHooks.EntitySnap s) {
        return e.squaredDistanceTo(s.x, s.y, s.z) < 16.0;
    }

    private static void pose(Entity m, RiftHooks.EntitySnap s) {
        double px = m.getX(), py = m.getY(), pz = m.getZ();
        m.setPosition(s.x, s.y, s.z);
        m.prevX = px;
        m.prevY = py;
        m.prevZ = pz;
        m.setYaw(s.yaw);
        m.prevYaw = s.yaw;
        m.setPitch(s.pitch);
        m.prevPitch = s.pitch;
        m.age = s.age;
        if (m instanceof ItemEntity item && s.item != null) item.setStack(s.item);
        if (m instanceof LivingEntity l) {
            l.setHeadYaw(s.headYaw);
            l.prevHeadYaw = s.headYaw;
            l.setBodyYaw(s.bodyYaw);
            l.prevBodyYaw = s.bodyYaw;
            l.hurtTime = s.hurtTime;
            l.handSwinging = s.swinging;
            l.handSwingTicks = s.swingTicks;
            if (s.equipment != null) {
                for (int i = 0; i < SLOTS.length && i < s.equipment.length; i++) l.equipStack(SLOTS[i], s.equipment[i]);
            }
            float speed = Math.min(1f, s.speed * 4f);
            l.limbAnimator.updateLimbs(speed, 0.4f);
        }
        m.setSneaking(s.sneaking);
        m.setPose(s.sneaking ? EntityPose.CROUCHING : EntityPose.STANDING);
    }

    // --- The capture itself -------------------------------------------------------------

    /** Vertices for one texture. */
    private static final class Layer {
        float[] pos = new float[256 * 3];
        float[] uv = new float[256 * 2];
        int[] col = new int[256];
        boolean[] hurt = new boolean[256];
        int count;

        void add(float x, float y, float z, float u, float v, int argb, boolean red) {
            if (count == col.length) {
                int n = col.length * 2;
                pos = java.util.Arrays.copyOf(pos, n * 3);
                uv = java.util.Arrays.copyOf(uv, n * 2);
                col = java.util.Arrays.copyOf(col, n);
                hurt = java.util.Arrays.copyOf(hurt, n);
            }
            pos[count * 3] = x;
            pos[count * 3 + 1] = y;
            pos[count * 3 + 2] = z;
            uv[count * 2] = u;
            uv[count * 2 + 1] = v;
            col[count] = argb;
            hurt[count] = red;
            count++;
        }
    }

    /** A VertexConsumerProvider that keeps everything, keyed by the texture it was meant for. */
    private static final class Capture implements VertexConsumerProvider {
        final Map<String, Layer> layers = new LinkedHashMap<>();
        private final Map<RenderLayer, VertexConsumer> consumers = new IdentityHashMap<>();

        @Override
        public VertexConsumer getBuffer(RenderLayer layer) {
            return consumers.computeIfAbsent(layer, this::open);
        }

        private VertexConsumer open(RenderLayer layer) {
            String desc = layer.toString();
            // Judge a layer by its name ("RenderLayer[entity_cutout:CompositeState[texture[...]...]]"),
            // never by the whole description: every layer's description mentions "texture".
            Matcher nm = NAME.matcher(desc);
            String name = nm.find() ? nm.group(1) : desc;
            if (layer.getDrawMode() != VertexFormat.DrawMode.QUADS || name.startsWith("text") || name.contains("glint")
                || name.contains("shadow") || name.contains("lines") || name.contains("leash")) {
                if (skipped.size() < 32 && skipped.add(name)) RiftMod.LOG.info("RIFT-E2E: capture leaves out layer {}", name);
                return new Sink(null, null, null);
            }
            Matcher m = TEXTURE.matcher(desc);
            String tex = m.find() ? m.group(1) : "";
            AtlasIndex atlas = tex.contains(":textures/atlas/") ? atlas(Identifier.tryParse(tex)) : null;
            return new Sink(this, tex, atlas);
        }

        void put(String tex, float[] x, float[] y, float[] z, float[] u, float[] v, int[] c, boolean[] red) {
            Layer l = layers.computeIfAbsent(tex, k -> new Layer());
            for (int i = 0; i < 4; i++) l.add(x[i], y[i], z[i], u[i], v[i], c[i], red[i]);
        }
    }

    /** Gathers vertices four at a time and hands each finished quad to the capture. */
    private static final class Sink implements VertexConsumer {
        private final Capture owner;
        private final String tex;
        private final AtlasIndex atlas;
        private final float[] x = new float[4], y = new float[4], z = new float[4], u = new float[4], v = new float[4];
        private final int[] c = new int[4];
        private final boolean[] red = new boolean[4];
        private int n = -1;

        Sink(Capture owner, String tex, AtlasIndex atlas) {
            this.owner = owner;
            this.tex = tex;
            this.atlas = atlas;
        }

        private void flushIfFull() {
            if (n == 3 && owner != null) emit();
        }

        private void emit() {
            String t = tex;
            float[] uu = u, vv = v;
            if (atlas != null) {
                float cu = (u[0] + u[1] + u[2] + u[3]) / 4f, cv = (v[0] + v[1] + v[2] + v[3]) / 4f;
                Sprite s = atlas.find(cu, cv);
                if (s != null) {
                    t = ClientHooksImpl.spriteTexture(s);
                    uu = new float[4];
                    vv = new float[4];
                    float du = Math.max(1e-6f, s.getMaxU() - s.getMinU()), dv = Math.max(1e-6f, s.getMaxV() - s.getMinV());
                    for (int i = 0; i < 4; i++) {
                        uu[i] = (u[i] - s.getMinU()) / du;
                        vv[i] = (v[i] - s.getMinV()) / dv;
                    }
                }
            }
            owner.put(t, x, y, z, uu, vv, c, red);
        }

        @Override
        public VertexConsumer vertex(float px, float py, float pz) {
            if (n == 3 && owner != null) emit();
            n = n == 3 ? 0 : n + 1;
            x[n] = px;
            y[n] = py;
            z[n] = pz;
            c[n] = 0xFFFFFFFF;
            u[n] = 0;
            v[n] = 0;
            red[n] = false;
            return this;
        }

        @Override
        public VertexConsumer color(int r, int g, int b, int a) {
            if (n >= 0) c[n] = (a << 24) | (r << 16) | (g << 8) | b;
            return this;
        }

        @Override
        public VertexConsumer texture(float tu, float tv) {
            if (n >= 0) {
                u[n] = tu;
                v[n] = tv;
            }
            return this;
        }

        @Override
        public VertexConsumer overlay(int ou, int ov) {
            if (n >= 0) red[n] = ov < 8;
            return this;
        }

        @Override
        public VertexConsumer light(int lu, int lv) {
            // The last property most vertex formats set: a good moment to close a full quad.
            return this;
        }

        @Override
        public VertexConsumer normal(float nx, float ny, float nz) {
            if (n == 3) {
                flushIfFull();
                n = -1;
            }
            return this;
        }
    }

    // --- Atlases ------------------------------------------------------------------------

    private static AtlasIndex atlas(Identifier id) {
        if (id == null) return null;
        return atlases.computeIfAbsent(id, AtlasIndex::build);
    }

    /** Finds which sprite of an atlas a UV falls in. */
    private static final class AtlasIndex {
        private static final int GRID = 64;
        private final List<Sprite>[] cells;

        @SuppressWarnings("unchecked")
        private AtlasIndex(List<Sprite> sprites) {
            cells = new List[GRID * GRID];
            for (Sprite s : sprites) {
                int x0 = (int) Math.floor(s.getMinU() * GRID), x1 = (int) Math.floor(s.getMaxU() * GRID);
                int y0 = (int) Math.floor(s.getMinV() * GRID), y1 = (int) Math.floor(s.getMaxV() * GRID);
                for (int y = Math.max(0, y0); y <= Math.min(GRID - 1, y1); y++) {
                    for (int x = Math.max(0, x0); x <= Math.min(GRID - 1, x1); x++) {
                        if (cells[y * GRID + x] == null) cells[y * GRID + x] = new ArrayList<>(2);
                        cells[y * GRID + x].add(s);
                    }
                }
            }
        }

        static AtlasIndex build(Identifier id) {
            List<Sprite> sprites = new ArrayList<>();
            try {
                SpriteAtlasTexture atlas = MinecraftClient.getInstance().getBakedModelManager().getAtlas(id);
                for (Class<?> k = atlas.getClass(); k != null && sprites.isEmpty(); k = k.getSuperclass()) {
                    for (Field f : k.getDeclaredFields()) {
                        if (!Map.class.isAssignableFrom(f.getType())) continue;
                        f.setAccessible(true);
                        Object v = f.get(atlas);
                        if (!(v instanceof Map<?, ?> map) || map.isEmpty()) continue;
                        Object first = map.values().iterator().next();
                        if (!(first instanceof Sprite)) continue;
                        for (Object o : map.values()) sprites.add((Sprite) o);
                        break;
                    }
                }
            } catch (Exception e) {
                RiftMod.LOG.debug("Rift: could not index atlas {}: {}", id, e.toString());
            }
            return new AtlasIndex(sprites);
        }

        Sprite find(float u, float v) {
            int x = Math.min(GRID - 1, Math.max(0, (int) (u * GRID)));
            int y = Math.min(GRID - 1, Math.max(0, (int) (v * GRID)));
            List<Sprite> list = cells[y * GRID + x];
            if (list == null) return null;
            for (Sprite s : list) {
                if (u >= s.getMinU() && u <= s.getMaxU() && v >= s.getMinV() && v <= s.getMaxV()) return s;
            }
            return null;
        }
    }
}
