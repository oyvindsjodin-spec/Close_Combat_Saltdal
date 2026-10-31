using System;
using System.Numerics;

namespace Saltdal.Sim;

public sealed partial class Simulation
{
    void MoveSoldier(Soldier s)
    {
        s.PreviousPosition = s.Position;
        if (!s.CanAct)
            return;

        UpdatePosture(s);
        if (!s.Fleeing && Time < s.Team.OrderDelayUntil)
            return;

        // Pinned and cowering men stay where they are; their route waits for them
        bool frozen = !s.Fleeing && s.Morale is MoraleState.Pinned or MoraleState.Cowering;
        if (frozen)
            return;

        var mode = s.Fleeing ? MoveMode.MoveFast : s.Team.Mode;
        float budget = TickSeconds;
        while (budget > 0 && s.Path.Count > 0)
        {
            var target = s.Path.Peek();
            var delta = target - s.Position;
            float dist = delta.Length();
            if (dist < 0.05f)
            {
                s.Path.Dequeue();
                continue;
            }
            var dir = delta / dist;
            s.Facing = MathF.Atan2(dir.Y, dir.X);
            float speed = Speed(s.Position, dir, mode);
            if (s.Health == Health.Wounded) speed *= 0.6f;
            float step = speed * budget;
            if (step >= dist)
            {
                s.Position = target;
                s.Path.Dequeue();
                budget -= dist / speed;
            }
            else
            {
                s.Position += dir * step;
                budget = 0;
            }
        }
        if (s.Path.Count == 0)
            s.Fleeing = false;
    }

    static void UpdatePosture(Soldier s)
    {
        if (s.Morale >= MoraleState.Pinned)
            s.Posture = Posture.Prone;
        else if (s.IsMoving)
            s.Posture = s.Team.Mode == MoveMode.Sneak ? Posture.Crouched : Posture.Standing;
        else
            s.Posture = s.Team.Order is TeamOrder.Defend or TeamOrder.Hide or TeamOrder.Fire
                ? Posture.Prone
                : Posture.Crouched;
    }

    /// <summary>Walking speed in metres per second at a point, heading in a direction.</summary>
    public float Speed(Vector2 at, Vector2 dir, MoveMode mode)
    {
        float baseSpeed = mode switch
        {
            MoveMode.Sneak => 0.6f,
            MoveMode.MoveFast => 3.0f,
            _ => 1.4f,
        };
        float terrain = TerrainInfo.MoveCost(Map.TerrainAt(at));
        if (float.IsInfinity(terrain))
            terrain = 3f; // wading out of a cell he should not be in
        float ahead = Map.HeightAt(at + dir * 2f) - Map.HeightAt(at);
        float grade = ahead / 2f;
        float slope = grade > 0 ? 1 + grade * 6f : 1 - grade * 1.5f;
        return baseSpeed / (terrain * slope);
    }
}
