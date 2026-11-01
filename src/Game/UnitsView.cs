using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;
using Vec = System.Numerics.Vector2;

namespace Saltdal.Game;

/// <summary>
/// Draws flags, soldiers, tracers and the selected team's route. Enemy soldiers
/// are shown only while the player's men can see them (fog of war).
/// </summary>
public partial class UnitsView : Node2D
{
    public Simulation? Sim { get; set; }
    public Side PlayerSide { get; set; } = Side.Allied;
    public Team? Selected { get; set; }
    /// <summary>Show every soldier, for testing.</summary>
    public bool RevealAll { get; set; }
    /// <summary>How far between the last tick and the next we are drawing (0 to 1).</summary>
    public float Alpha { get; set; }
    /// <summary>Camera zoom, so dots and labels keep a readable size on screen.</summary>
    public float Zoom { get; set; } = 1f;

    // Sizes in screen pixels
    const float RadiusPx = 4f;
    const int FontPx = 12;
    const float LastKnownSeconds = 20f;
    const float FallSeconds = 0.5f;
    /// <summary>Battle time between ticks, so animations run smoothly. Positions are drawn between the last two ticks.</summary>
    float Now => Sim!.Time + (Alpha - 1) * Simulation.TickSeconds;

    /// <summary>
    /// What each man looks like on screen, eased towards what the simulation
    /// says: the way he faces turns rather than snaps, he drops to the ground
    /// and gets up over a moment, and his legs move with the ground he covers.
    /// </summary>
    sealed class Pose
    {
        public float Facing;
        /// <summary>0 standing, 1 lying down.</summary>
        public float Low;
        /// <summary>Step cycle, advanced by distance walked, so feet do not slide.</summary>
        public float Stride;
        /// <summary>How much he is walking now (0 to 1), so the legs settle when he stops.</summary>
        public float Walking;
        public Vector2 LastAt;
        public bool Fresh = true;
    }
    readonly System.Collections.Generic.Dictionary<Soldier, Pose> _poses = new();
    Simulation? _poseSim;
    ulong _lastDrawUsec;
    float _dt;

    const float TurnRate = 7f;        // radians a second
    const float DropRate = 3.5f;      // a man gets down in under a third of a second
    const float StrideMetres = 1.6f;  // ground covered in one full step cycle

    Pose PoseOf(Soldier s, Vector2 at)
    {
        if (!_poses.TryGetValue(s, out var pose))
            _poses[s] = pose = new Pose();
        float lowTarget = s.Health >= Health.Incapacitated || s.Posture == Posture.Prone ? 1f
            : s.Posture == Posture.Crouched ? 0.35f : 0f;
        if (pose.Fresh)
        {
            pose.Facing = s.Facing;
            pose.Low = lowTarget;
            pose.LastAt = at;
            pose.Fresh = false;
            return pose;
        }
        // Turn the short way round, no faster than a man turns
        float diff = Mathf.Wrap(s.Facing - pose.Facing, -Mathf.Pi, Mathf.Pi);
        float turn = TurnRate * _dt;
        pose.Facing += Mathf.Abs(diff) <= turn ? diff : Mathf.Sign(diff) * turn;
        pose.Low = Mathf.MoveToward(pose.Low, lowTarget, DropRate * _dt);
        // Steps follow the distance actually covered on screen this frame
        float moved = (at - pose.LastAt).Length() / View.Scale;
        pose.LastAt = at;
        if (moved < 5f) pose.Stride += moved / StrideMetres * Mathf.Tau;
        float walking = s.IsMoving && moved > 0.0005f ? 1f : 0f;
        pose.Walking = Mathf.MoveToward(pose.Walking, walking, 6f * _dt);
        return pose;
    }

    public override void _Draw()
    {
        if (Sim == null) return;
        if (_poseSim != Sim)
        {
            _poses.Clear();
            _poseSim = Sim;
        }
        ulong usec = Time.GetTicksUsec();
        _dt = _lastDrawUsec == 0 ? 0 : Mathf.Min((usec - _lastDrawUsec) / 1e6f, 0.1f);
        _lastDrawUsec = usec;
        float k = 1f / Zoom;
        foreach (var flag in Sim.Flags)
            DrawFlag(flag, k);
        foreach (var team in Sim.Teams)
        {
            if (team == Selected)
                DrawRoute(team);
            foreach (var s in team.Soldiers)
                DrawSoldier(s, team == Selected, k);
        }
        DrawShots(k);
        DrawBlasts(k);
        DrawAircraft(k);
        foreach (var team in Sim.Teams)
            if (team.Side == PlayerSide || RevealAll)
                DrawLabel(View.ToScreen(team.Center) + new Vector2(-30, -26) * k, team.Name,
                    team == Selected ? Colors.White : new Color(1, 1, 1, 0.75f), k);
    }

    /// <summary>
    /// A soldier seen from above, as a small figure: helmet, shoulders and pack,
    /// and his weapon pointing where he faces. Drawn larger than life so a man
    /// can be picked out at a glance.
    /// </summary>
    void DrawSoldier(Soldier s, bool selected, float k)
    {
        var p = View.ToScreen(Vec.Lerp(s.PreviousPosition, s.Position, Alpha));
        bool friendly = s.Side == PlayerSide || RevealAll;
        if (!friendly && !s.IsVisibleToEnemy(Sim!.Time))
        {
            // Out of sight: a faint marker where he was last seen, for a while
            if (s.IsAlive && Sim.Time - s.LastSeenByEnemy < LastKnownSeconds)
                DrawArc(View.ToScreen(s.LastKnownPosition), RadiusPx * k, 0, Mathf.Tau, 12,
                    new Color(View.SideColor(s.Side), 0.5f), 1.2f * k);
            return;
        }

        // Figures grow a little when zoomed in, but stay readable when zoomed out
        float f = k * Mathf.Clamp(Mathf.Sqrt(Zoom), 1.0f, 2.2f);
        var look = Uniform.For(s);
        bool down = s.Health >= Health.Incapacitated;
        var pose = PoseOf(s, p);
        // Lying down once he is mostly down; on the way down or up he shows as a crouch
        bool prone = down || pose.Low > 0.7f;
        float facing = pose.Facing;
        // Body sway with each step: the shoulders turn a little from side to side
        float sway = pose.Walking * Mathf.Sin(pose.Stride) * (prone ? 0.10f : 0.13f);

        _sprites ??= SoldierSprites.Load() ?? SoldierSprites.Missing;
        if (_sprites != SoldierSprites.Missing)
        {
            DrawSoldierSprite(s, p, pose, prone, down, facing, f, selected);
            return;
        }

        // Shadow falls to the south-east, away from the light
        DrawSetTransform(p + new Vector2(1.6f, 1.6f) * f, facing, Vector2.One * f);
        if (prone) DrawEllipse(new Vector2(-2, 0), 7.5f, 3.2f, new Color(0, 0, 0, 0.28f));
        else DrawEllipse(Vector2.Zero, 3.6f, 5.2f, new Color(0, 0, 0, 0.28f));

        DrawSetTransform(p, facing + sway, Vector2.One * f);
        // Light comes from the north-west, as on the painted ground; turn it into the man's own frame
        _light = new Vector2(-1, -1).Normalized().Rotated(-(facing + sway));
        if (selected && !down)
            DrawArc(prone ? new Vector2(-2.5f, 0) : Vector2.Zero, prone ? 7.5f : 5.6f, 0, Mathf.Tau, 24, new Color(1, 1, 0.85f, 0.55f), 0.7f);

        // Falling: for half a second after being hit the body pitches over and stretches out
        float sinceDown = s.DownAt < 0 ? 99f : Now - s.DownAt;
        if (down && sinceDown < FallSeconds)
        {
            float t = sinceDown / FallSeconds;
            float ease = 1 - (1 - t) * (1 - t);
            DrawSetTransform(p, facing + (1 - ease) * 0.5f, new Vector2(f * (0.4f + 0.6f * ease), f));
            DrawProne(look.Dimmed(0.3f * ease), s, false, pose);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }

        if (s.Health == Health.Dead)
        {
            var dead = look.Dimmed(0.45f);
            DrawProne(dead, s, false, pose);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }
        if (s.Health == Health.Incapacitated)
        {
            DrawProne(look.Dimmed(0.25f), s, false, pose);
            DrawCircle(new Vector2(-1, 0.5f), 1.1f, new Color(0.6f, 0.05f, 0.05f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }

        if (prone)
        {
            // Settling to the ground: the body stretches out over the last moment
            float settle = Mathf.Clamp((pose.Low - 0.7f) / 0.3f, 0, 1);
            if (settle < 1) DrawSetTransform(p, facing + sway, new Vector2(f * (0.75f + 0.25f * settle), f));
            DrawProne(look, s, true, pose);
        }
        else DrawUpright(look, s, pose.Low > 0.2f, pose);

        // Muzzle flash for a moment after each shot
        if (Sim!.Time - s.LastShotTime < 0.12f && !s.Weapon.IsIndirect)
        {
            var tip = prone ? new Vector2(7.5f, 0.6f) : new Vector2(7.2f, 1.6f);
            DrawCircle(tip + new Vector2(1.2f, 0), 1.4f, new Color(1f, 0.85f, 0.4f, 0.95f));
        }
        if (s.Health == Health.Wounded)
            DrawCircle(prone ? new Vector2(-1, 0) : new Vector2(-1.2f, -1.5f), 0.9f, new Color(0.7f, 0.05f, 0.05f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // A small coloured pip over a man who is shaken, so trouble is easy to spot
        var mood = s.Morale == MoraleState.Surrendered ? Colors.White : View.MoraleColor(s.Morale);
        if (s.Morale != MoraleState.Ready)
            DrawCircle(p + new Vector2(0, -8f) * f, 1.8f * f, mood);
    }

    SoldierSprites? _sprites;
    /// <summary>How much larger than life the figures are drawn, so a man can be picked out at a glance.</summary>
    const float FigureScale = 5.5f;

    /// <summary>
    /// A man drawn from the pre-rendered figures: the pose follows what he is
    /// doing, the frame follows the ground his feet have covered, and the
    /// figure faces the nearest of 16 directions.
    /// </summary>
    void DrawSoldierSprite(Soldier s, Vector2 p, Pose pose, bool prone, bool down, float facing, float f, bool selected)
    {
        var sp = _sprites!;
        string uniform = s.Side == Side.German ? "german" : s.Team.Nation == "norwegian" ? "norwegian" : "british";
        var w = s.Weapon;
        string weapon = w.IsIndirect ? "rifle" : w.Burst >= 5 && w.Range >= 800 ? "mg" : w.Range <= 200 ? "smg" : "rifle";
        string frame;
        bool moving = pose.Walking > 0.15f;
        if (down) frame = "dead";
        else if (prone) frame = moving ? $"crawl{Mathf.PosMod((int)(pose.Stride * 1.5f / Mathf.Tau * 4), 4)}" : "prone";
        else if (moving) frame = $"walk{Mathf.PosMod((int)(pose.Stride / Mathf.Tau * 8), 8)}";
        else frame = pose.Low > 0.2f ? "crouch" : "stand";
        var region = sp.Region(uniform, weapon, frame, facing);
        if (region == null) return;

        // Screen units per sprite pixel: true size times how much larger than life
        float px = View.Scale / sp.PixelsPerMetre * FigureScale * f;
        var tint = Colors.White;
        float sinceDown = s.DownAt < 0 ? 99f : Now - s.DownAt;
        if (s.Health == Health.Dead) tint = new Color(0.62f, 0.60f, 0.58f);
        else if (s.Health == Health.Incapacitated) tint = new Color(0.8f, 0.78f, 0.76f);
        if (down && sinceDown < FallSeconds)
        {
            // Falling: he pitches over, the body lengthening onto the ground
            float t = sinceDown / FallSeconds;
            tint.A = 0.4f + 0.6f * t;
        }
        if (selected && !down)
            DrawArc(p, (prone ? 0.75f : 0.45f) * FigureScale * View.Scale * f, 0, Mathf.Tau, 28, new Color(1, 1, 0.85f, 0.35f), 0.6f * f);
        var size = new Vector2(sp.Size, sp.Size) * px;
        DrawTextureRectRegion(sp.Texture, new Rect2(p - sp.Anchor * px, size), region.Value, tint);

        var dir = new Vector2(Mathf.Cos(facing), Mathf.Sin(facing));
        float m = View.Scale * FigureScale * f;      // screen units per metre of figure
        if (w.IsIndirect && !down)
        {
            // The mortar set up beside him
            var at = p + dir * 0.7f * m + dir.Orthogonal() * 0.35f * m;
            DrawCircle(at, 0.16f * m, new Color(0.12f, 0.12f, 0.11f));
            DrawCircle(at, 0.09f * m, new Color(0.3f, 0.3f, 0.28f));
        }
        // Muzzle flash for a moment after each shot
        if (Sim!.Time - s.LastShotTime < 0.12f && !w.IsIndirect && !down)
        {
            var tip = p + dir * (prone ? 1.75f : 1.15f) * m + new Vector2(0, prone ? -0.1f : -0.45f) * m;
            DrawCircle(tip, 0.14f * m, new Color(1f, 0.85f, 0.4f, 0.95f));
            DrawCircle(tip, 0.07f * m, new Color(1f, 1f, 0.8f, 1f));
        }
        if (s.Health is Health.Wounded or Health.Incapacitated)
            DrawCircle(p + new Vector2(0, prone || down ? -0.1f : -0.6f) * m, 0.08f * m, new Color(0.7f, 0.05f, 0.05f));
        // A small coloured pip over a man who is shaken, so trouble is easy to spot
        var mood = s.Morale == MoraleState.Surrendered ? Colors.White : View.MoraleColor(s.Morale);
        if (s.Morale != MoraleState.Ready && !down)
            DrawCircle(p + new Vector2(0, -1.0f) * m, 0.13f * m, mood);
    }

    /// <summary>Standing or crouching: shoulders across, pack behind, helmet on top.</summary>
    void DrawUpright(Uniform u, Soldier s, bool crouched, Pose pose)
    {
        if (pose.Walking > 0.01f)
        {
            // Legs striding out in front and behind in time with the ground covered;
            // a longer stride when running, shorter when crouched
            bool running = s.Team.Mode == MoveMode.MoveFast || s.Fleeing || s.EscapingFire;
            float reach = (running ? 4.8f : crouched ? 2.8f : 3.8f) * pose.Walking;
            float swing = Mathf.Sin(pose.Stride) * reach;
            // A foot is lifted while it swings forward: draw it a touch longer and lighter
            var legs = u.Cloth.Darkened(0.45f);
            var boot = new Color(0.13f, 0.11f, 0.09f);
            var l = new Vector2(-0.5f + swing, -1.6f);
            var r = new Vector2(-0.5f - swing, 1.6f);
            var rim = new Color(0.08f, 0.07f, 0.05f, 0.85f);
            DrawLine(new Vector2(-0.5f, -1.4f), l, rim, 2.6f);
            DrawLine(new Vector2(-0.5f, 1.4f), r, rim, 2.6f);
            DrawLine(new Vector2(-0.5f, -1.4f), l, legs, 1.7f);
            DrawLine(new Vector2(-0.5f, 1.4f), r, legs, 1.7f);
            DrawCircle(l, 0.9f, boot);
            DrawCircle(r, 0.9f, boot);
            // Arms swing against the legs: the free hand at the side
            float arm = -swing * 0.5f;
            DrawLine(new Vector2(-0.2f, -4.0f), new Vector2(0.6f + arm, -4.3f), u.Cloth.Darkened(0.2f), 1.3f);
        }
        float w = crouched ? 4.2f : 4.8f;    // half the shoulder width
        // Crouching: hunched forward, the pack rides higher
        float lean = crouched ? 0.6f : 0f;
        DrawShaded(new Vector2(-0.2f + lean, 0), 2.5f, w, u.Cloth);
        DrawShaded(new Vector2(-2.6f + lean, 0), 1.1f, 2.2f, u.Kit);
        if (!s.Weapon.IsIndirect && pose.Walking < 0.5f)
        {
            // Arms forward to the weapon: right hand at the grip, left hand under the fore-end
            var rim = new Color(0.08f, 0.07f, 0.05f, 0.85f);
            var sleeve = u.Cloth.Darkened(0.12f);
            (Vector2 a, Vector2 b)[] arms = { (new(lean, 3.4f), new(2.0f + lean, 2.7f)), (new(lean, -3.4f), new(4.4f + lean, 1.9f)) };
            foreach (var (a, b) in arms) DrawLine(a, b, rim, 2.3f);
            foreach (var (a, b) in arms) DrawLine(a, b, sleeve, 1.4f);
        }
        DrawWeapon(s, upright: true);
        DrawHelmet(u, new Vector2(0.3f + lean * 1.4f, 0));
    }

    /// <summary>Lying down: the body stretched out behind the head, legs apart.</summary>
    void DrawProne(Uniform u, Soldier s, bool withWeapon, Pose pose)
    {
        var legs = u.Cloth.Darkened(0.2f);
        // Crawling: the legs work in turn, in time with the ground covered
        float crawl = withWeapon ? Mathf.Sin(pose.Stride * 1.5f) * 1.6f * pose.Walking : 0f;
        var rim = new Color(0.08f, 0.07f, 0.05f, 0.85f);
        var footL = new Vector2(-10.5f + crawl, -2.6f - crawl * 0.4f);
        var footR = new Vector2(-10.5f - crawl, 2.4f - crawl * 0.4f);
        DrawLine(new Vector2(-5, -1.2f), footL, rim, 2.7f);
        DrawLine(new Vector2(-5, 1.2f), footR, rim, 2.7f);
        DrawLine(new Vector2(-5, -1.2f), footL, legs.Darkened(0.15f), 1.8f);
        DrawLine(new Vector2(-5, 1.2f), footR, legs.Darkened(0.15f), 1.8f);
        DrawCircle(footL, 0.8f, new Color(0.13f, 0.11f, 0.09f));
        DrawCircle(footR, 0.8f, new Color(0.13f, 0.11f, 0.09f));
        DrawShaded(new Vector2(-1.9f, 0), 4.6f, 2.7f, u.Cloth);
        DrawShaded(new Vector2(-3.1f, 0), 1.4f, 1.6f, u.Kit);
        if (withWeapon) DrawWeapon(s, upright: false);
        DrawHelmet(u, new Vector2(3.2f, 0));
    }

    void DrawWeapon(Soldier s, bool upright)
    {
        var w = s.Weapon;
        var metal = new Color(0.12f, 0.12f, 0.11f);
        var wood = new Color(0.36f, 0.24f, 0.14f);
        var from = upright ? new Vector2(0.5f, 2.6f) : new Vector2(2f, 0.9f);
        // Recoil: the weapon kicks back for a moment as it fires
        float sinceShot = Now - s.LastShotTime;
        if (sinceShot >= 0 && sinceShot < 0.12f) from -= new Vector2(1.2f * (1 - sinceShot / 0.12f), 0);
        if (w.IsIndirect)
        {
            // The mortar tube set up beside him, seen end-on
            var at = upright ? new Vector2(5.5f, 3.5f) : new Vector2(6f, 2.8f);
            DrawCircle(at, 2.1f, metal);
            DrawCircle(at, 1.2f, new Color(0.3f, 0.3f, 0.28f));
            DrawLine(at, at + new Vector2(-2.5f, -2.5f), metal, 0.7f);
            DrawLine(at, at + new Vector2(-2.5f, 2.5f), metal, 0.7f);
            return;
        }
        bool mg = w.Burst >= 5 && w.Range >= 800;
        bool smg = w.Range <= 200;
        float length = mg ? 9.5f : smg ? 5f : 8f;
        var to = from + new Vector2(length, upright ? -1.2f : -0.3f);
        DrawLine(from, from + (to - from) * 0.45f, smg ? metal : wood, mg ? 1.6f : 1.2f);
        DrawLine(from + (to - from) * 0.45f, to, metal, mg ? 1.4f : 0.9f);
        if (mg)
        {
            // Bipod legs and the magazine on top (Bren) or the belt (MG 34)
            DrawLine(to - new Vector2(1.5f, 0), to + new Vector2(-0.5f, 1.6f), metal, 0.6f);
            DrawLine(to - new Vector2(1.5f, 0), to + new Vector2(-0.5f, -1.6f), metal, 0.6f);
            DrawRect(new Rect2(from + (to - from) * 0.35f - new Vector2(0.6f, 1.6f), new Vector2(1.4f, 1.4f)), metal);
        }
    }

    void DrawHelmet(Uniform u, Vector2 at)
    {
        if (u.WideBrim)
        {
            // The British Brodie: a flat, wide brim around a shallow bowl
            DrawShaded(at, 2.8f, 2.8f, u.Helmet.Darkened(0.12f));
            DrawCircle(at + _light * 0.5f, 1.5f, u.Helmet.Lightened(0.08f));
            DrawCircle(at + _light * 0.9f, 0.7f, u.Helmet.Lightened(0.25f));
        }
        else
        {
            // The German Stahlhelm (or Norwegian helmet): a deep bowl flared at the back
            DrawShaded(at + new Vector2(-0.35f, 0), 2.2f, 2.0f, u.Helmet.Darkened(0.15f));
            DrawShaded(at, 1.6f, 1.6f, u.Helmet);
            DrawCircle(at + _light * 0.8f, 0.7f, u.Helmet.Lightened(0.25f));
        }
    }

    /// <summary>The light direction in the frame of the man being drawn.</summary>
    Vector2 _light = new(-0.7f, -0.7f);

    /// <summary>
    /// A rounded body part in the painted style: a dark rim so it stands out
    /// from the ground, the shaded side away from the light, and a highlight
    /// on the side facing the north-west sun.
    /// </summary>
    void DrawShaded(Vector2 c, float rx, float ry, Color color)
    {
        DrawEllipse(c, rx + 0.45f, ry + 0.45f, new Color(0.08f, 0.07f, 0.05f, 0.85f));
        DrawEllipse(c, rx, ry, color.Darkened(0.32f));
        var lit = new Vector2(_light.X * rx, _light.Y * ry) * 0.22f;
        DrawEllipse(c + lit, rx * 0.8f, ry * 0.8f, color);
        DrawEllipse(c + lit * 2.2f, rx * 0.38f, ry * 0.38f, color.Lightened(0.18f));
    }

    void DrawEllipse(Vector2 c, float rx, float ry, Color color)
    {
        const int n = 14;
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Tau * i / n;
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(pts, color);
    }

    /// <summary>Colours of a soldier's uniform, kit and helmet.</summary>
    readonly record struct Uniform(Color Cloth, Color Kit, Color Helmet, bool WideBrim)
    {
        static readonly Uniform British = new(new Color(0.56f, 0.46f, 0.29f), new Color(0.66f, 0.62f, 0.45f), new Color(0.40f, 0.41f, 0.27f), true);
        static readonly Uniform German = new(new Color(0.43f, 0.47f, 0.41f), new Color(0.32f, 0.28f, 0.22f), new Color(0.31f, 0.35f, 0.31f), false);
        static readonly Uniform Norwegian = new(new Color(0.40f, 0.44f, 0.36f), new Color(0.50f, 0.44f, 0.32f), new Color(0.36f, 0.39f, 0.32f), false);

        public static Uniform For(Soldier s) =>
            s.Side == Side.German ? German
            : s.Team.Nation == "norwegian" ? Norwegian
            : British;

        public Uniform Dimmed(float amount) => new(Cloth.Darkened(amount), Kit.Darkened(amount), Helmet.Darkened(amount), WideBrim);
    }

    void DrawShots(float k)
    {
        foreach (var shot in Sim!.RecentShots)
        {
            // Hide the shooter's end of an unseen enemy's tracer; the impact still shows
            var from = View.ToScreen(shot.From);
            var to = View.ToScreen(shot.To);
            if (shot.Side != PlayerSide && !RevealAll)
            {
                var shooter = FindSoldier(shot.ShooterId);
                if (shooter != null && !shooter.IsVisibleToEnemy(Sim.Time))
                    from = to + (from - to).Normalized() * 40f * k;
            }
            float age = Sim.Time - shot.Time;
            float fade = Mathf.Clamp(1f - age / 0.6f, 0f, 1f);
            var color = shot.Side == Side.Allied ? new Color(1f, 0.9f, 0.5f, fade) : new Color(1f, 0.6f, 0.4f, fade);
            DrawLine(from, to, color, 1f * k);
        }
    }

    void DrawBlasts(float k)
    {
        foreach (var b in Sim!.RecentBlasts)
        {
            float age = Sim.Time - b.Time;
            var p = View.ToScreen(b.At);
            float size = b.Explosive.KillRadius * View.Scale;
            if (age < 0.4f)
            {
                // The flash, growing fast
                float t = age / 0.4f;
                DrawCircle(p, size * (0.6f + 1.2f * t), new Color(1f, 0.75f, 0.3f, 0.9f * (1 - t)));
                DrawCircle(p, size * 0.5f, new Color(1f, 1f, 0.8f, 1 - t));
            }
            // Earth and stones thrown up and falling back
            if (age < 1.0f)
            {
                int seed = (int)(b.At.X * 13 + b.At.Y * 7);
                for (int i = 0; i < 10; i++)
                {
                    float angle = Shapes.Hash(seed, i) * Mathf.Tau;
                    float speed = size * (0.8f + 1.4f * Shapes.Hash(seed, i + 20));
                    float lift = size * (1.5f + 2f * Shapes.Hash(seed, i + 40));
                    var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    // Seen from above, a clod flies out and its rise shows as a drift up the screen
                    var at = p + dir * speed * age + new Vector2(0, -(lift * age - lift * age * age));
                    DrawCircle(at, (1.2f + Shapes.Hash(seed, i + 60)) * Mathf.Max(1, size / 10), new Color(0.27f, 0.22f, 0.16f, 1 - age));
                }
            }
            // Smoke that drifts and fades over a few seconds
            float fade = Mathf.Clamp(1f - age / 3f, 0f, 1f);
            DrawCircle(p + new Vector2(age * 4f, -age * 2f), size * (1f + age * 0.6f), new Color(0.35f, 0.33f, 0.3f, 0.45f * fade));
        }
    }

    void DrawAircraft(float k)
    {
        foreach (var a in Sim!.AircraftOverhead)
        {
            if (!a.IsFlying(Sim.Time)) continue;
            var p = View.ToScreen(a.PositionAt(Sim.Time));
            var dir = (View.ToScreen(a.To) - View.ToScreen(a.From)).Normalized();
            var side = new Vector2(-dir.Y, dir.X);
            float s = 14f * k;
            var dark = new Color(0.12f, 0.12f, 0.12f, 0.9f);
            // A gull-winged silhouette: fuselage, wings, tail
            DrawLine(p - dir * s, p + dir * s, dark, 3f * k);
            DrawPolyline(new[] { p + side * s * 1.2f + dir * s * 0.1f, p + side * s * 0.5f + dir * s * 0.3f, p, p - side * s * 0.5f + dir * s * 0.3f, p - side * s * 1.2f + dir * s * 0.1f }, dark, 3f * k);
            DrawLine(p - dir * s + side * s * 0.4f, p - dir * s - side * s * 0.4f, dark, 2f * k);
            DrawCircle(p + new Vector2(10, 12) * k, s * 0.6f, new Color(0, 0, 0, 0.15f));
        }
    }

    Soldier? FindSoldier(int id)
    {
        foreach (var team in Sim!.Teams)
            foreach (var s in team.Soldiers)
                if (s.Id == id) return s;
        return null;
    }

    void DrawFlag(VictoryFlag flag, float k)
    {
        var p = View.ToScreen(flag.Position);
        var color = flag.Owner == null ? new Color(0.8f, 0.8f, 0.8f) : View.SideColor(flag.Owner.Value);
        DrawCircle(p, flag.Radius * View.Scale, new Color(color, 0.12f));
        DrawArc(p, flag.Radius * View.Scale, 0, Mathf.Tau, 48, new Color(color, 0.7f), 1.5f * k);
        // A little pennant on a pole
        var top = p + new Vector2(0, -22) * k;
        DrawLine(p, top, Colors.Black, 2f * k);
        DrawColoredPolygon(new[] { top, top + new Vector2(14, 4) * k, top + new Vector2(0, 8) * k }, color);
        string owner = flag.Owner == null ? "" : $" ({View.SideName(flag.Owner.Value)})";
        DrawLabel(p + new Vector2(8, -24) * k, flag.Name + owner, Colors.White, k);
    }

    void DrawLabel(Vector2 at, string text, Color color, float k)
    {
        DrawSetTransform(at, 0, Vector2.One * k);
        DrawString(ThemeDB.FallbackFont, new Vector2(1, 1), text, HorizontalAlignment.Left, -1, FontPx, new Color(0, 0, 0, color.A * 0.8f));
        DrawString(ThemeDB.FallbackFont, Vector2.Zero, text, HorizontalAlignment.Left, -1, FontPx, color);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    void DrawRoute(Team team)
    {
        Soldier? guide = null;
        foreach (var s in team.Soldiers)
            if (s.CanAct && s.IsMoving) { guide = s; break; }
        if (guide == null)
        {
            if (team.Order == TeamOrder.Fire)
                DrawLine(View.ToScreen(team.Center), View.ToScreen(team.OrderPoint), new Color(1, 0.3f, 0.2f, 0.6f), 1.5f / Zoom);
            return;
        }
        var from = View.ToScreen(guide.Position);
        foreach (var w in guide.Waypoints)
        {
            var to = View.ToScreen(w);
            DrawLine(from, to, new Color(1, 1, 1, 0.6f), 1.5f / Zoom);
            from = to;
        }
    }
}
