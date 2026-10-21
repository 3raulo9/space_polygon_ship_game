package net.hopper.client;

import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.hopper.net.DialPayload;
import net.hopper.net.RealmsPayload;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.client.gui.widget.TextFieldWidget;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;

import java.util.ArrayList;
import java.util.List;

/**
 * The gun's dial: every destination down the left, the chosen one described on the right.
 * Another game's realm that is not linked yet asks for a clear connection first, and shows
 * how establishing it is going, refreshed every second.
 */
public class DialScreen extends Screen {
    private static final int GREEN = 0xFF5CFF6E, DIM = 0xFF2A6B33, PANEL = 0xD0081A0C;
    private RealmsPayload realms;
    private final List<ButtonWidget> rows = new ArrayList<>();
    private ButtonWidget connect;
    private int ticks;
    /** What was clicked last, shown at once while the server catches up. */
    private String focus;
    /** The coordinate boxes' text, kept across re-layouts (blank: wherever is natural). */
    private final String[] typed = {"", "", ""};
    private final TextFieldWidget[] fields = new TextFieldWidget[3];
    private String layout = "";

    public DialScreen(RealmsPayload realms) {
        super(Text.literal("DIMENSION HOPPER"));
        this.realms = realms;
        String[] c = (realms.coords() + ",,").split(",", -1);
        for (int i = 0; i < 3; i++) typed[i] = c[i];
    }

    public RealmsPayload realms() {
        return realms;
    }

    /** A fresh list from the server. The screen is only laid out again when what it shows
     *  changed — a re-layout every second would snatch the cursor out of a coordinate box. */
    public void update(RealmsPayload p) {
        this.realms = p;
        if (client != null && !layoutKey().equals(layout)) clearAndInit();
    }

    private String layoutKey() {
        StringBuilder b = new StringBuilder(shown()).append('|');
        for (RealmsPayload.Entry e : realms.entries()) b.append(e.id()).append(e.clear()).append(';');
        return b.toString();
    }

    /** Types coordinates into the boxes and sets them, as the button would. */
    public void setCoords(String x, String y, String z) {
        typed[0] = x;
        typed[1] = y;
        typed[2] = z;
        for (int i = 0; i < 3; i++) if (fields[i] != null) fields[i].setText(typed[i]);
        sendCoords();
    }

    private void sendCoords() {
        ClientPlayNetworking.send(new DialPayload("coords", typed[0].trim() + "," + typed[1].trim() + "," + typed[2].trim()));
    }

    /** Dials a destination, as clicking it would. */
    public void pick(String id) {
        focus = id;
        ClientPlayNetworking.send(new DialPayload("select", id));
        if (client != null) clearAndInit();
    }

    public void establish(String id) {
        ClientPlayNetworking.send(new DialPayload("connect", id));
    }

    private int left() {
        return width / 2 - 200;
    }

    @Override
    protected void init() {
        rows.clear();
        int y = 44;
        for (RealmsPayload.Entry e : realms.entries()) {
            boolean on = e.id().equals(shown());
            String label = (on ? "> " : "") + e.name() + (e.realm() ? (e.clear() ? "" : "  (no signal)") : "");
            ButtonWidget b = ButtonWidget.builder(Text.literal(label).formatted(on ? Formatting.GREEN : Formatting.WHITE),
                btn -> pick(e.id())).dimensions(left(), y, 180, 20).build();
            rows.add(addDrawableChild(b));
            y += 22;
            if (y > height - 40) break;
        }
        RealmsPayload.Entry sel = selected();
        layout = layoutKey();
        connect = null;
        for (int i = 0; i < 3; i++) fields[i] = null;
        if (sel != null && !sel.realm()) {
            // Where over there: X, Y, Z. Blank is "wherever is natural".
            String[] names = {"X", "Y", "Z"};
            for (int i = 0; i < 3; i++) {
                int k = i;
                TextFieldWidget f = new TextFieldWidget(textRenderer, left() + 200 + i * 64, 118, 56, 18, Text.literal(names[i]));
                f.setMaxLength(9);
                f.setText(typed[i]);
                f.setPlaceholder(Text.literal("~ " + names[i]).formatted(Formatting.DARK_GRAY));
                f.setTextPredicate(t -> t.matches("-?\\d*"));
                f.setChangedListener(t -> typed[k] = t);
                fields[i] = addDrawableChild(f);
            }
            addDrawableChild(ButtonWidget.builder(Text.literal("Set coordinates"), btn -> sendCoords())
                .dimensions(left() + 200, 140, 188, 20).build());
        }
        if (sel != null && sel.realm() && !sel.clear()) {
            connect = addDrawableChild(ButtonWidget.builder(Text.literal("Establish connection"), btn -> establish(sel.id()))
                .dimensions(left() + 200, 128, 190, 20).build());
        }
        addDrawableChild(ButtonWidget.builder(Text.literal("Done"), btn -> close())
            .dimensions(width / 2 - 50, height - 28, 100, 20).build());
    }

    private String shown() {
        return focus != null ? focus : realms.selected();
    }

    private RealmsPayload.Entry selected() {
        for (RealmsPayload.Entry e : realms.entries()) if (e.id().equals(shown())) return e;
        return null;
    }

    @Override
    public void tick() {
        if (++ticks % 20 == 0) ClientPlayNetworking.send(new DialPayload("list", ""));
    }

    /** The frame and the detail panel go under the buttons. */
    @Override
    public void renderBackground(DrawContext ctx, int mouseX, int mouseY, float delta) {
        super.renderBackground(ctx, mouseX, mouseY, delta);
        ctx.fill(left() - 8, 18, left() + 408, height - 34, 0x90031006);
        ctx.drawBorder(left() - 8, 18, 416, height - 52, GREEN);
        int dx = left() + 200;
        ctx.fill(dx - 4, 44, dx + 196, 200, PANEL);
        ctx.drawBorder(dx - 4, 44, 200, 156, DIM);
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        super.render(ctx, mouseX, mouseY, delta);
        ctx.drawCenteredTextWithShadow(textRenderer, Text.literal("DIMENSION HOPPER").formatted(Formatting.BOLD), width / 2, 24, GREEN);
        int dx = left() + 200;
        RealmsPayload.Entry sel = selected();
        if (sel == null) {
            ctx.drawTextWrapped(textRenderer, Text.literal("Choose where the next portal opens."), dx, 52, 188, 0xFFB0FFB8);
            return;
        }
        ctx.drawTextWithShadow(textRenderer, Text.literal(sel.name()).formatted(Formatting.BOLD), dx, 52, 0xFFFFFFFF);
        if (!sel.realm()) {
            ctx.drawTextWrapped(textRenderer, Text.literal("A dimension of this world. Fire, and the portal's twin opens somewhere safe there."),
                dx, 68, 188, 0xFFB0FFB8);
            ctx.drawTextWithShadow(textRenderer, Text.literal("COORDINATES").formatted(Formatting.BOLD), dx, 106, GREEN);
            String at = "wherever is natural";
            if (!realms.coords().isEmpty()) {
                String[] c = (realms.coords() + ",,").split(",", -1);
                at = (c[0].isEmpty() ? "~" : c[0]) + ", " + (c[1].isEmpty() ? "~" : c[1]) + ", " + (c[2].isEmpty() ? "~" : c[2]);
            }
            ctx.drawTextWithShadow(textRenderer, Text.literal("Set: " + at), dx, 166, 0xFFB0FFB8);
            ctx.drawTextWithShadow(textRenderer, Text.literal(sel.id()), dx, 186, 0xFF6F9F74);
            return;
        }
        ctx.drawTextWithShadow(textRenderer, Text.literal("A realm of " + sel.game()), dx, 66, 0xFFB0FFB8);
        if (sel.clear()) {
            ctx.drawTextWithShadow(textRenderer, Text.literal("CONNECTION CLEAR").formatted(Formatting.BOLD), dx, 88, GREEN);
        } else {
            ctx.drawTextWrapped(textRenderer, Text.literal("ESTABLISH A CLEAR CONNECTION WITH THIS REALM").formatted(Formatting.BOLD),
                dx, 84, 188, 0xFFFFD84A);
        }
        ctx.drawTextWrapped(textRenderer, Text.literal(sel.status()), dx, 156, 188, 0xFFC8C8C8);
    }

    @Override
    public boolean shouldPause() {
        return false;
    }
}
