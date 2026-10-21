package net.hopper.client;

import java.util.HashMap;
import java.util.Map;

/** How much of each body on screen has teleported away, as the server last said. */
public final class ScatterClient {
    private static final Map<Integer, Integer> stages = new HashMap<>();

    private ScatterClient() {}

    public static int stage(int entityId) {
        return stages.getOrDefault(entityId, 0);
    }

    static void set(int entityId, int stage) {
        if (stage <= 0) stages.remove(entityId);
        else stages.put(entityId, stage);
    }

    static void clear() {
        stages.clear();
    }
}
