package net.unrendered.rift.entity;

import net.minecraft.entity.EntityDimensions;
import net.minecraft.entity.EntityPose;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.ai.NoPenaltyTargeting;
import net.minecraft.entity.ai.goal.ActiveTargetGoal;
import net.minecraft.entity.ai.goal.Goal;
import net.minecraft.entity.ai.goal.LookAroundGoal;
import net.minecraft.entity.ai.goal.MeleeAttackGoal;
import net.minecraft.entity.ai.goal.RevengeGoal;
import net.minecraft.entity.ai.goal.SwimGoal;
import net.minecraft.entity.ai.goal.WanderAroundFarGoal;
import net.minecraft.entity.attribute.DefaultAttributeContainer;
import net.minecraft.entity.attribute.EntityAttributes;
import net.minecraft.entity.damage.DamageSource;
import net.minecraft.entity.data.DataTracker;
import net.minecraft.entity.data.TrackedData;
import net.minecraft.entity.data.TrackedDataHandlerRegistry;
import net.minecraft.entity.mob.HostileEntity;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.item.ItemStack;
import net.minecraft.nbt.NbtCompound;
import net.minecraft.sound.SoundEvents;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.World;
import net.unrendered.rift.RiftItems;

import java.util.EnumSet;

/**
 * One of UNRENDERED's own enemies that walked out of the portal into Minecraft: a hunter, an
 * elite or a soldier. It looks exactly as UNRENDERED draws it (the body arrives as
 * UNRENDERED's own vertices) and fights the way it does there: hunters hold their range and
 * fire slow bolts, soldiers close in. Minecraft weapons kill it; it drops UNRENDERED salvage.
 * Walk it back into a portal and UNRENDERED has it again.
 */
public class UnrEnemyEntity extends HostileEntity {
    private static final TrackedData<String> KIND = DataTracker.registerData(UnrEnemyEntity.class, TrackedDataHandlerRegistry.STRING);

    /** The hit points UNRENDERED counts it in, so it goes home with what it had left. */
    public float unrMax = 3f;
    private int cooldown = 40;

    public UnrEnemyEntity(EntityType<? extends UnrEnemyEntity> type, World world) {
        super(type, world);
        experiencePoints = 8;
    }

    public static DefaultAttributeContainer.Builder createAttributes() {
        return HostileEntity.createHostileAttributes()
            .add(EntityAttributes.GENERIC_MAX_HEALTH, 30.0)
            .add(EntityAttributes.GENERIC_MOVEMENT_SPEED, 0.28)
            .add(EntityAttributes.GENERIC_FOLLOW_RANGE, 48.0)
            .add(EntityAttributes.GENERIC_ATTACK_DAMAGE, 4.0)
            .add(EntityAttributes.GENERIC_KNOCKBACK_RESISTANCE, 0.6);
    }

    @Override
    protected void initDataTracker(DataTracker.Builder builder) {
        super.initDataTracker(builder);
        builder.add(KIND, "hunter");
    }

    public String kind() {
        return dataTracker.get(KIND);
    }

    public boolean isSoldier() {
        return "soldier".equals(kind());
    }

    public boolean isElite() {
        return "elite".equals(kind());
    }

    /** Sets what it is and how hurt it is, in UNRENDERED's own numbers. */
    public void setup(String kind, float hp, float max) {
        dataTracker.set(KIND, kind);
        calculateDimensions();
        unrMax = Math.max(0.01f, max);
        float mcMax = "elite".equals(kind) ? 30f : 20f;
        var attr = getAttributeInstance(EntityAttributes.GENERIC_MAX_HEALTH);
        if (attr != null) attr.setBaseValue(mcMax);
        setHealth(Math.max(1f, mcMax * Math.min(1f, hp / unrMax)));
    }

    /** What it has left, in UNRENDERED's numbers. */
    public float unrHp() {
        return unrMax * getHealth() / Math.max(1f, getMaxHealth());
    }

    @Override
    public void onTrackedDataSet(TrackedData<?> data) {
        super.onTrackedDataSet(data);
        if (KIND.equals(data)) calculateDimensions();
    }

    @Override
    public EntityDimensions getBaseDimensions(EntityPose pose) {
        return switch (kind()) {
            case "soldier" -> EntityDimensions.changing(0.6f, 1.9f);
            case "elite" -> EntityDimensions.changing(2.4f, 2.9f);
            default -> EntityDimensions.changing(2.2f, 2.6f);
        };
    }

    @Override
    protected void initGoals() {
        goalSelector.add(1, new SwimGoal(this));
        goalSelector.add(2, new HoldRangeGoal(this));
        goalSelector.add(3, new SoldierMeleeGoal(this));
        goalSelector.add(6, new WanderAroundFarGoal(this, 0.8));
        goalSelector.add(7, new LookAroundGoal(this));
        targetSelector.add(1, new RevengeGoal(this));
        targetSelector.add(2, new ActiveTargetGoal<>(this, PlayerEntity.class, true));
    }

    @Override
    public void writeCustomDataToNbt(NbtCompound nbt) {
        super.writeCustomDataToNbt(nbt);
        nbt.putString("UnrKind", kind());
        nbt.putFloat("UnrMax", unrMax);
    }

    @Override
    public void readCustomDataFromNbt(NbtCompound nbt) {
        super.readCustomDataFromNbt(nbt);
        if (nbt.contains("UnrKind")) dataTracker.set(KIND, nbt.getString("UnrKind"));
        if (nbt.contains("UnrMax")) unrMax = nbt.getFloat("UnrMax");
    }

    @Override
    public boolean cannotDespawn() {
        return true;
    }

    @Override
    public boolean canImmediatelyDespawn(double distanceSquared) {
        return false;
    }

    @Override
    protected void dropLoot(DamageSource source, boolean causedByPlayer) {
        super.dropLoot(source, causedByPlayer);
        dropStack(new ItemStack(RiftItems.SCRAP_METAL, 1 + random.nextInt(2)));
        float roll = random.nextFloat();
        if (roll < 0.45f) dropStack(new ItemStack(RiftItems.BATTERY));
        else if (roll < 0.9f) dropStack(new ItemStack(RiftItems.BULLET, 5));
        if (random.nextFloat() < 0.3f) dropStack(new ItemStack(random.nextBoolean() ? RiftItems.SPACE_GUNPOWDER : RiftItems.COPPER_WIRE));
    }

    /** The hunter's whole doctrine: stay at range, keep the target in sight, fire. */
    static final class HoldRangeGoal extends Goal {
        private final UnrEnemyEntity mob;

        HoldRangeGoal(UnrEnemyEntity mob) {
            this.mob = mob;
            setControls(EnumSet.of(Control.MOVE, Control.LOOK));
        }

        @Override
        public boolean canStart() {
            LivingEntity t = mob.getTarget();
            return !mob.isSoldier() && t != null && t.isAlive();
        }

        @Override
        public boolean shouldContinue() {
            return canStart();
        }

        @Override
        public boolean shouldRunEveryTick() {
            return true;
        }

        @Override
        public void tick() {
            LivingEntity t = mob.getTarget();
            if (t == null) return;
            mob.getLookControl().lookAt(t, 30f, 30f);
            double d = mob.distanceTo(t);
            if (d > 18.0) {
                mob.getNavigation().startMovingTo(t, 1.0);
            } else if (d < 10.0) {
                Vec3d away = NoPenaltyTargeting.findFrom(mob, 8, 4, t.getPos());
                if (away != null) mob.getNavigation().startMovingTo(away.x, away.y, away.z, 1.1);
            } else {
                mob.getNavigation().stop();
            }
            if (--mob.cooldown > 0 || !mob.canSee(t)) return;
            mob.cooldown = mob.isElite() ? 28 : 38;
            UnrBoltEntity bolt = new UnrBoltEntity(mob.getWorld(), mob);
            double dx = t.getX() - mob.getX();
            double dy = t.getBodyY(0.5) - bolt.getY();
            double dz = t.getZ() - mob.getZ();
            bolt.setVelocity(dx, dy, dz, 1.1f, 1.5f);
            mob.getWorld().spawnEntity(bolt);
            mob.playSound(SoundEvents.ENTITY_BLAZE_SHOOT, 0.6f, 1.6f + mob.random.nextFloat() * 0.2f);
        }
    }

    /** A soldier closes in and cuts. */
    static final class SoldierMeleeGoal extends MeleeAttackGoal {
        private final UnrEnemyEntity mob;

        SoldierMeleeGoal(UnrEnemyEntity mob) {
            super(mob, 1.25, false);
            this.mob = mob;
        }

        @Override
        public boolean canStart() {
            return mob.isSoldier() && super.canStart();
        }

        @Override
        public boolean shouldContinue() {
            return mob.isSoldier() && super.shouldContinue();
        }
    }
}
