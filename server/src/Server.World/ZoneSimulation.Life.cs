namespace Realmforge.Server.World;

/// <summary>
/// Idle behaviour that makes the world feel inhabited: creatures roam around their spawn and NPCs
/// shuffle around (or patrol) the spot their schedule puts them at. Purely cosmetic for game rules,
/// but authoritative so every player sees the same movement.
/// </summary>
public sealed partial class ZoneSimulation
{
    public const float MobWanderRadiusMeters = 7f;
    public const float MobWanderSpeedMetersPerSecond = 1.6f;
    public const float NpcPatrolRadiusMeters = 14f;
    public const float NpcFidgetRadiusMeters = 2.5f;
    public const float NpcPatrolSpeedMetersPerSecond = 1.8f;
    public const float NpcStrollSpeedMetersPerSecond = 1.3f;
    private const int MinIdlePauseMilliseconds = 3000;
    private const int MaxIdlePauseMilliseconds = 9000;
    private const string PatrolActivityPrefix = "patrol";
    private static readonly HashSet<string> RestingActivities = new() { "sleep", "home" };

    private void UpdateIdleLife()
    {
        foreach (MobEntity mob in _entities.Values.OfType<MobEntity>())
        {
            UpdateMobRoaming(mob);
        }
        foreach (NpcEntity npc in _entities.Values.OfType<NpcEntity>())
        {
            UpdateNpcIdling(npc);
        }
    }

    private void UpdateMobRoaming(MobEntity mob)
    {
        if (mob.IsDead || mob.InCombat || mob.Definition.SpeedCells <= 0 || !HasArrived(mob))
        {
            return;
        }
        if (mob.DistanceTo(mob.SpawnX, mob.SpawnZ) > MobWanderRadiusMeters)
        {
            mob.SpeedMetersPerSecond = MobSpeedMetersPerSecond;
            mob.TargetX = mob.SpawnX;
            mob.TargetZ = mob.SpawnZ;
            return;
        }
        if (Now < mob.NextIdleMoveAt)
        {
            return;
        }
        (mob.TargetX, mob.TargetZ) = RandomPointAround(mob.SpawnX, mob.SpawnZ, MobWanderRadiusMeters);
        mob.SpeedMetersPerSecond = MobWanderSpeedMetersPerSecond;
        mob.NextIdleMoveAt = Now + IdlePauseSeconds();
    }

    private void UpdateNpcIdling(NpcEntity npc)
    {
        if (!npc.HasAnchor || !HasArrived(npc) || Now < npc.NextIdleMoveAt || RestingActivities.Contains(npc.Activity))
        {
            return;
        }
        bool patrols = npc.Activity.StartsWith(PatrolActivityPrefix, StringComparison.Ordinal);
        float radius = patrols ? NpcPatrolRadiusMeters : NpcFidgetRadiusMeters;
        (npc.TargetX, npc.TargetZ) = RandomPointAround(npc.AnchorX, npc.AnchorZ, radius);
        npc.SpeedMetersPerSecond = patrols ? NpcPatrolSpeedMetersPerSecond : NpcStrollSpeedMetersPerSecond;
        npc.NextIdleMoveAt = Now + IdlePauseSeconds();
    }

    /// <summary>A new schedule entry sends the NPC walking at full pace; idling only starts once it gets there.</summary>
    private void AssignNpcAnchor(NpcEntity npc, string activity, float x, float z)
    {
        if (npc.HasAnchor && npc.Activity == activity && npc.AnchorX == x && npc.AnchorZ == z)
        {
            return;
        }
        npc.Activity = activity;
        npc.AnchorX = x;
        npc.AnchorZ = z;
        npc.HasAnchor = true;
        npc.TargetX = x;
        npc.TargetZ = z;
        npc.SpeedMetersPerSecond = NpcSpeedMetersPerSecond;
        npc.NextIdleMoveAt = Now + IdlePauseSeconds();
    }

    private (float X, float Z) RandomPointAround(float centerX, float centerZ, float radius)
    {
        const int Resolution = 1000;
        float angle = _random.NextInt(0, Resolution) / (float)Resolution * MathF.Tau;
        float distance = MathF.Sqrt(_random.NextInt(0, Resolution) / (float)Resolution) * radius;
        float x = Math.Clamp(centerX + MathF.Cos(angle) * distance, 0, Zone.SizeMeters);
        float z = Math.Clamp(centerZ + MathF.Sin(angle) * distance, 0, Zone.SizeMeters);
        return (x, z);
    }

    private double IdlePauseSeconds() => _random.NextInt(MinIdlePauseMilliseconds, MaxIdlePauseMilliseconds) / 1000.0;

    private static bool HasArrived(WorldEntity entity) => entity.DistanceTo(entity.TargetX, entity.TargetZ) <= ArrivalToleranceMeters;
}
