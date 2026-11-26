using System.Numerics;
using static SimBox.Item;
namespace SimBox;

public struct Slot { public Item Item; public int N; }
public struct AgentInput { public float Mx, My, Aim, AimDist; public bool Sprint, Attack; public Cmd Cmd; public int Arg; }

public sealed class Agent
{
    public int Id, Team, Kills; public bool IsHuman, IsLeader, Alive = true;
    // estatísticas da geração (zeradas em Reset): combate entre times e ferramentas
    public int EnemyKills, FriendKills, ToolsMade, Donations, StationsMade, GoodMeals, WastedMeals; public float EnemyDmg, FriendDmg;
    public bool[] ToolPaid = Array.Empty<bool>(), DonatePaid = Array.Empty<bool>(), StationPaid = Array.Empty<bool>();   // bônus já pago nesta geração (por item)
    public Vector2 Pos, Vel; public float Radius = 30, Aim;
    public float Hp = 255, Hunger = 255, Cold = 255, Stamina = 255, Rad;
    public Slot[] Slots = Array.Empty<Slot>(); public int Selected;
    public float Xp; public int Level, SkillPts; public bool[] Unlocked = Array.Empty<bool>();
    public float AttackCd, CraftLeft, CraftTotal, ReloadLeft, LastHurt = -99, BornAt; public int CraftRecipe = -1;
    public int[] Mag = Array.Empty<int>();
    public int Count(Item it) { int c = 0; foreach (var s in Slots) if (s.N > 0 && s.Item == it) c += s.N; return c; }
    public bool Has(Item it) => Count(it) > 0;
}

public sealed class Node
{
    public int Id; public NodeKind Kind; public Vector2 Pos; public float R; public bool Solid = true, Alive = true;
    public int Fruit; public float GrowAt, DieAt = -1, RespawnAt; public float Hp;
}
public sealed class Ghoul { public int Id; public GhoulKind Kind; public Vector2 Pos, Dir; public float Hp, MaxHp, Cd, WanderT; public bool Alive = true, Hostile; }
public sealed class Animal { public int Id; public Vector2 Pos, Dir; public float Hp = 50, WanderT, FleeT; public bool Alive = true; }
public sealed class Carcass { public int Id; public Vector2 Pos; public int Left = 4; public float DecayAt; }
public sealed class GroundItem { public int Id; public Item Item; public int N; public Vector2 Pos; public float DieAt, NoPick; }
public sealed class Projectile { public int Id, OwnerTeam, OwnerId; public Item Kind; public Vector2 Pos, Dir; public float Speed, Dmg, Left; public bool FromRobot; }
public sealed class Robot { public int Id, Team; public bool Hall; public Vector2 Pos; public float Hp, Cd, RepairT; public bool Alive = true; }
public sealed class FlareZone { public Vector2 Pos; public float Left; }
public struct Ev { public byte T; public float X, Y, X2, Y2; public int A; }

public sealed partial class World
{
    public readonly SimConfig Cfg;
    public readonly float W, H;
    public readonly int GW, GH;
    public Agent[] Agents;
    public readonly List<Node> Nodes = new();
    public readonly List<Ghoul> Ghouls = new();
    public readonly List<Animal> Animals = new();
    public readonly List<Carcass> Carcasses = new();
    public readonly List<GroundItem> Drops = new();
    public readonly List<Projectile> Projs = new();
    public readonly List<Robot> Robots = new();
    public readonly List<FlareZone> Flares = new();
    public readonly List<Ev> Events = new();
    public bool EmitEvents = true;
    public Action<Agent, string>? Log;

    public float Time; public long TickCount; public bool Done;
    public float[] Rewards;
    /// <summary>true = Rewards acumula até o próximo Reset() (treino por gerações); false = zera a cada Step (API clássica).</summary>
    public bool AccumulateRewards;
    /// <summary>Linhas de registro de eventos importantes (mortes, kills entre agentes, ferramentas, doações).</summary>
    public Action<string>? Trace;
    string T() => $"[{Time,7:0.0}s]";
    /// <summary>Muro vertical no meio do mapa (treino): bloqueia agentes, ghouls, tiros e golpes até <see cref="SimConfig.WallOpenTime"/>.</summary>
    public bool WallActive => Cfg.TeamWall && Time < Cfg.WallOpenTime;
    bool wallAnnounced;
    public long[] KillsByKind = new long[5]; public long GhoulsSpawned, Deaths;

    // construções (uma por célula de 100 u)
    public readonly byte[] BldGrid, FloorGrid; readonly float[] bldHp;
    readonly Dictionary<int, int[]> boxes = new();
    readonly Dictionary<int, float> fuel = new();       // fogueiras/abrasadores acesos: segundos de combustível
    readonly Dictionary<int, int> charge = new();       // smelter: operações restantes
    readonly List<Node>[] ngrid; const float NG = 250f; readonly int NGW, NGH;
    Rng rng; int nextId = 1; float prevTin, rotT;
    public float Dt => 1f / Cfg.Tps;

    public bool IsDay => Time % Cfg.Cycle < Cfg.DayLen;
    public int DayNumber => (int)(Time / Cfg.Cycle) + 1;
    public float TimeInCycle => Time % Cfg.Cycle;

    public World(SimConfig cfg, int agentCount, int seed, int humanIndex = -1)
    {
        Cfg = cfg; W = cfg.WorldW; H = cfg.WorldH;
        GW = (int)MathF.Ceiling(W / cfg.Cell); GH = (int)MathF.Ceiling(H / cfg.Cell);
        BldGrid = new byte[GW * GH]; FloorGrid = new byte[GW * GH]; bldHp = new float[GW * GH];
        NGW = (int)MathF.Ceiling(W / NG); NGH = (int)MathF.Ceiling(H / NG);
        ngrid = new List<Node>[NGW * NGH]; for (int i = 0; i < ngrid.Length; i++) ngrid[i] = new();
        Agents = new Agent[agentCount];
        for (int i = 0; i < agentCount; i++)
            Agents[i] = new Agent { Id = i, IsLeader = i == 0, IsHuman = i == humanIndex, Slots = new Slot[cfg.InventorySlots], Mag = new int[Defs.ItemCount], Unlocked = new bool[Defs.ItemCount], ToolPaid = new bool[Defs.ItemCount], DonatePaid = new bool[Defs.ItemCount], StationPaid = new bool[Defs.ItemCount] };
        Rewards = new float[agentCount];
        rng = new Rng(seed);
        Reset(seed);
    }

    // ------------------------------------------------------------------ reset / geração
    public void Reset(int seed)
    {
        rng = new Rng(seed); nextId = 1; Time = 0; TickCount = 0; Done = false; prevTin = 0; rotT = 0; wallAnnounced = false;
        Array.Clear(BldGrid); Array.Clear(FloorGrid); Array.Clear(bldHp); Array.Clear(Rewards);
        boxes.Clear(); fuel.Clear(); charge.Clear();
        Nodes.Clear(); Ghouls.Clear(); Animals.Clear(); Carcasses.Clear(); Drops.Clear(); Projs.Clear(); Robots.Clear(); Flares.Clear(); Events.Clear();
        foreach (var l in ngrid) l.Clear();
        Array.Clear(KillsByKind); GhoulsSpawned = Deaths = 0;
        GenerateWorld();
        int k = 0; var inTeam = new int[2];
        foreach (var a in Agents)
        {
            ResetAgent(a, true);
            if (Cfg.TeamSpawns) { int t = a.Team & 1, j = inTeam[t]++; var b = TeamBase(t); a.Pos = new Vector2(b.X + (j % 4 - 1.5f) * Cfg.TeamSpawnSpacing, b.Y + (j / 4 - 1.5f) * Cfg.TeamSpawnSpacing); }
            else a.Pos = new Vector2(W / 2 + (k % 3 - 1) * 70, H / 2 + (k / 3 - 1) * 70);
            k++;
        }
        TopUpAnimals();
    }

    public Vector2 TeamBase(int team) => new(W * ((team & 1) == 0 ? 0.25f : 0.75f), H / 2);

    void ResetAgent(Agent a, bool full)
    {
        a.Alive = true; a.Hp = a.Hunger = a.Cold = a.Stamina = Defs.StatMax; a.Rad = 0; a.AttackCd = a.CraftLeft = a.ReloadLeft = 0; a.CraftRecipe = -1;
        a.LastHurt = -99; a.Selected = 0; a.BornAt = Time; a.Vel = Vector2.Zero;
        Array.Clear(a.Slots); Array.Clear(a.Mag);
        if (full) { a.Xp = 0; a.Level = 0; a.SkillPts = 0; a.Kills = 0; a.EnemyKills = a.FriendKills = a.ToolsMade = a.Donations = a.StationsMade = a.GoodMeals = a.WastedMeals = 0; a.EnemyDmg = a.FriendDmg = 0; Array.Clear(a.ToolPaid); Array.Clear(a.DonatePaid); Array.Clear(a.StationPaid); }
        Array.Clear(a.Unlocked);
        foreach (var r in Defs.Recipes) if (r.SkillPts == 0) a.Unlocked[(int)r.Out] = true;
        AddItem(a, Stone, 2);                       // [wiki] você nasce com 2 pedras para fazer o machado
        if (!full) { a.Level = Defs.LevelFor(a.Xp); a.SkillPts = a.Level; }
    }

    float[] Noise(int cell)
    {
        int gw = (int)(W / cell) + 2, gh = (int)(H / cell) + 2; var g = new float[gw * gh];
        for (int i = 0; i < g.Length; i++) g[i] = rng.Float();
        var o = new float[gw * gh]; return g;      // grade grossa; amostrada com Sample()
    }
    static float Sample(float[] g, int gw, float cell, float x, float y)
    {
        float fx = x / cell, fy = y / cell; int ix = (int)fx, iy = (int)fy; float tx = fx - ix, ty = fy - iy;
        float a = g[iy * gw + ix], b = g[iy * gw + ix + 1], c = g[(iy + 1) * gw + ix], d = g[(iy + 1) * gw + ix + 1];
        return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
    }

    Node AddNode(NodeKind k, Vector2 p)
    {
        float r = k switch { NodeKind.Tree => 30, NodeKind.Rock => 38, NodeKind.IronOre => 38, NodeKind.SulfurOre => 36, NodeKind.Crate => 30, _ => 26 };
        var n = new Node { Id = nextId++, Kind = k, Pos = p, R = r, Solid = k != NodeKind.OrangeBush && k != NodeKind.TomatoBush, Hp = k == NodeKind.Crate ? 60 : 0 };
        if (k == NodeKind.OrangeBush || k == NodeKind.TomatoBush) n.Fruit = 3;
        Nodes.Add(n); ngrid[NodeCell(p)].Add(n); return n;
    }
    int NodeCell(Vector2 p) => Math.Clamp((int)(p.Y / NG), 0, NGH - 1) * NGW + Math.Clamp((int)(p.X / NG), 0, NGW - 1);

    bool NodeFree(Vector2 p, float r)
    {
        foreach (var n in NodesNear(p, r + 70)) if (n.Alive && Vector2.DistanceSquared(n.Pos, p) < (n.R + r + 24) * (n.R + r + 24)) return false;
        return true;
    }

    void GenerateWorld()
    {
        int gw = (int)(W / 600) + 2, gh = (int)(H / 600) + 2;
        var forest = new float[gw * gh]; var rocky = new float[gw * gh];
        for (int i = 0; i < forest.Length; i++) { forest[i] = rng.Float(); rocky[i] = rng.Float(); }
        var center = new Vector2(W / 2, H / 2);
        void Scatter(NodeKind k, int count, Func<float, float, float> weight)
        {
            int placed = 0, tries = 0;
            while (placed < count && tries++ < count * 40)
            {
                var p = new Vector2(rng.Float(80, W - 80), rng.Float(80, H - 80));
                if (Vector2.Distance(p, center) < 350) continue;
                if (Cfg.TeamSpawns && (Vector2.Distance(p, TeamBase(0)) < 450 || Vector2.Distance(p, TeamBase(1)) < 450)) continue;
                if (Cfg.TeamWall && MathF.Abs(p.X - W / 2) < Cfg.Cell / 2 + 60) continue;      // nada enterrado dentro do muro
                if (!rng.Chance(weight(Sample(forest, gw, 600, p.X, p.Y), Sample(rocky, gw, 600, p.X, p.Y)))) continue;
                float r = k == NodeKind.Tree ? 30 : 38; if (!NodeFree(p, r)) continue;
                AddNode(k, p); placed++;
            }
        }
        Scatter(NodeKind.Tree, Cfg.Trees, (f, r) => 0.1f + 0.9f * f * f);
        Scatter(NodeKind.Rock, Cfg.Rocks, (f, r) => 0.1f + 0.9f * r * r);
        Scatter(NodeKind.IronOre, Cfg.IronNodes, (f, r) => r > 0.55f ? 1f : 0.05f);
        Scatter(NodeKind.SulfurOre, Cfg.SulfurNodes, (f, r) => r > 0.7f ? 1f : 0.02f);
        Scatter(NodeKind.OrangeBush, Cfg.Bushes * 2 / 3, (f, r) => f > 0.4f ? 1f : 0.1f);
        Scatter(NodeKind.TomatoBush, Cfg.Bushes / 3, (f, r) => f > 0.4f ? 1f : 0.1f);
        // "casas abandonadas": aglomerados de caixotes com sucata/enxofre
        int clusters = Math.Max(1, Cfg.Crates / 5);
        for (int c = 0; c < clusters; c++)
        {
            var cp = new Vector2(rng.Float(300, W - 300), rng.Float(300, H - 300));
            if (Vector2.Distance(cp, center) < 500 || (Cfg.TeamSpawns && (Vector2.Distance(cp, TeamBase(0)) < 600 || Vector2.Distance(cp, TeamBase(1)) < 600))) { c--; continue; }
            for (int i = 0; i < 5; i++) { var p = cp + new Vector2(rng.Float(-110, 110), rng.Float(-110, 110)); if (NodeFree(p, 30)) AddNode(NodeKind.Crate, p); }
        }
    }

    IEnumerable<Node> NodesNear(Vector2 p, float r)
    {
        int x0 = Math.Max(0, (int)((p.X - r) / NG)), x1 = Math.Min(NGW - 1, (int)((p.X + r) / NG));
        int y0 = Math.Max(0, (int)((p.Y - r) / NG)), y1 = Math.Min(NGH - 1, (int)((p.Y + r) / NG));
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) { var l = ngrid[y * NGW + x]; for (int i = 0; i < l.Count; i++) yield return l[i]; }
    }

    void TopUpAnimals()
    {
        int alive = 0; foreach (var a in Animals) if (a.Alive) alive++;
        int tries = 0;
        while (alive < Cfg.Animals && tries++ < 400)
        {
            var p = new Vector2(rng.Float(100, W - 100), rng.Float(100, H - 100));
            if (!NodeFree(p, 24) || Vector2.Distance(p, new Vector2(W / 2, H / 2)) < 250) continue;
            Animals.Add(new Animal { Id = nextId++, Pos = p, Dir = RandDir() }); alive++;
        }
    }
    Vector2 RandDir() { float a = rng.Float(0, 6.2832f); return new Vector2(MathF.Cos(a), MathF.Sin(a)); }

    // ------------------------------------------------------------------ colisão
    public Vector2 Resolve(Vector2 p, float r, bool agent)
    {
        for (int it = 0; it < 3; it++)
        {
            bool moved = false;
            foreach (var n in NodesNear(p, r + 70))
            {
                if (!n.Alive || !n.Solid) continue;
                float dx = p.X - n.Pos.X, dy = p.Y - n.Pos.Y, m = r + n.R, d2 = dx * dx + dy * dy;
                if (d2 >= m * m) continue;
                float d = MathF.Sqrt(d2); if (d < 0.001f) { dx = 1; dy = 0; d = 1; }
                p += new Vector2(dx, dy) / d * (m - d); moved = true;
            }
            int gx0 = (int)MathF.Floor((p.X - r) / Cfg.Cell), gx1 = (int)MathF.Floor((p.X + r) / Cfg.Cell);
            int gy0 = (int)MathF.Floor((p.Y - r) / Cfg.Cell), gy1 = (int)MathF.Floor((p.Y + r) / Cfg.Cell);
            for (int gy = gy0; gy <= gy1; gy++)
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    if (gx < 0 || gy < 0 || gx >= GW || gy >= GH) continue;
                    byte b = BldGrid[gy * GW + gx]; if (b == 0) continue;
                    var def = Defs.Blds[b]; if (!(agent ? def.SolidAgent : def.SolidMob)) continue;
                    float rx = gx * Cfg.Cell, ry = gy * Cfg.Cell, cx = Math.Clamp(p.X, rx, rx + Cfg.Cell), cy = Math.Clamp(p.Y, ry, ry + Cfg.Cell);
                    float dx = p.X - cx, dy = p.Y - cy, d2 = dx * dx + dy * dy;
                    if (d2 >= r * r) continue;
                    if (d2 > 0.0001f) { float d = MathF.Sqrt(d2); p += new Vector2(dx, dy) / d * (r - d); }
                    else
                    {
                        float l = p.X - rx, rr = rx + Cfg.Cell - p.X, t = p.Y - ry, bt = ry + Cfg.Cell - p.Y, mn = MathF.Min(MathF.Min(l, rr), MathF.Min(t, bt));
                        if (mn == l) p.X = rx - r; else if (mn == rr) p.X = rx + Cfg.Cell + r; else if (mn == t) p.Y = ry - r; else p.Y = ry + Cfg.Cell + r;
                    }
                    moved = true;
                }
            if (WallActive)      // muro do meio: empurra para o lado em que a entidade já estava
            {
                float lo = W / 2 - Cfg.Cell / 2 - r, hi = W / 2 + Cfg.Cell / 2 + r;
                if (p.X > lo && p.X < hi) { p.X = p.X < W / 2 ? lo : hi; moved = true; }
            }
            p.X = Math.Clamp(p.X, r, W - r); p.Y = Math.Clamp(p.Y, r, H - r);
            if (!moved) break;
        }
        return p;
    }

    public int CellOf(Vector2 p) { int gx = (int)MathF.Floor(p.X / Cfg.Cell), gy = (int)MathF.Floor(p.Y / Cfg.Cell); return gx < 0 || gy < 0 || gx >= GW || gy >= GH ? -1 : gy * GW + gx; }
    public Vector2 CellCenter(int idx) => new((idx % GW + 0.5f) * Cfg.Cell, (idx / GW + 0.5f) * Cfg.Cell);

    // ------------------------------------------------------------------ eventos
    void Emit(byte t, float x, float y, float x2 = 0, float y2 = 0, int a = 0) { if (EmitEvents) Events.Add(new Ev { T = t, X = x, Y = y, X2 = x2, Y2 = y2, A = a }); }
    void Say(Agent a, string msg) { if (a.IsHuman) Log?.Invoke(a, msg); }
    void Announce(string msg) { foreach (var a in Agents) if (a.IsHuman && a.Alive) Log?.Invoke(a, msg); }

    // ------------------------------------------------------------------ loop principal
    public void Step(ReadOnlySpan<AgentInput> inputs)
    {
        if (Done) return;
        float dt = Dt;
        if (EmitEvents) Events.Clear();
        if (!AccumulateRewards) Array.Clear(Rewards);
        if (!wallAnnounced && Cfg.TeamWall && Time >= Cfg.WallOpenTime) { wallAnnounced = true; Trace?.Invoke($"{T()} O muro entre os times caiu"); Announce("O muro entre os times caiu."); }
        float tin = TimeInCycle;
        if (tin < prevTin) DayStart();                                   // virou o ciclo: amanheceu
        if (prevTin < Cfg.DayLen && tin >= Cfg.DayLen) DuskStart();      // anoiteceu
        prevTin = tin;

        for (int i = 0; i < Agents.Length; i++)
        {
            var a = Agents[i]; var inp = i < inputs.Length ? inputs[i] : default;
            if (!a.Alive) { if (inp.Cmd == Cmd.Respawn) Respawn(a); continue; }
            UpdateAgent(a, inp, dt);
        }
        UpdateGhouls(dt); UpdateAnimals(dt); UpdateRobots(dt); UpdateProjectiles(dt); UpdatePlants();
        UpdateFires(dt); UpdateDrops(dt);
        for (int i = Flares.Count - 1; i >= 0; i--) if ((Flares[i].Left -= dt) <= 0) Flares.RemoveAt(i);
        rotT += dt; if (rotT >= Cfg.RotCheckSeconds) { rotT = 0; RotFood(); }
        Time += dt; TickCount++;
        bool any = false; foreach (var a in Agents) if (a.Alive) { any = true; break; }
        if (!any && Agents.Length > 0 && !Agents.Any(a => a.IsHuman)) Done = true;
    }

    void DayStart()
    {
        TopUpAnimals();
        foreach (var n in Nodes)
        {
            if (!n.Alive) { if (Time >= n.RespawnAt) { n.Alive = true; n.Hp = 60; } continue; }
            if (n.Kind == NodeKind.OrangeBush || n.Kind == NodeKind.TomatoBush) n.Fruit = 3;
        }
        if (Time > 0) Announce($"Amanheceu (dia {DayNumber}). Os ghouls começam a morrer.");
    }

    void DuskStart()
    {
        int night = (int)(Time / Cfg.Cycle) + 1;
        var kinds = new List<GhoulKind>();
        for (int k = 0; k < 5; k++) if (Time >= Defs.Ghouls[k].UnlockCycle * Cfg.Cycle) kinds.Add((GhoulKind)k);
        if (kinds.Count == 0) { Announce("Anoiteceu. Os ghouls ainda não estão hostis: noite calma."); return; }
        var alive = Agents.Where(a => a.Alive).ToList(); if (alive.Count == 0) return;
        int n = (int)((Cfg.NightGhoulsBase + Cfg.NightGhoulsPerNight * night) * (1 + 0.25f * (alive.Count - 1)));
        for (int i = 0; i < n; i++)
        {
            var kind = i == 0 ? kinds[^1] : kinds[rng.Int(kinds.Count)];
            var anchor = alive[rng.Int(alive.Count)].Pos;
            Vector2 p = anchor; for (int t = 0; t < 12; t++) { p = anchor + RandDir() * rng.Float(900, 1500); p.X = Math.Clamp(p.X, 60, W - 60); p.Y = Math.Clamp(p.Y, 60, H - 60); if (NodeFree(p, 34)) break; }
            var d = Defs.Ghouls[(int)kind];
            Ghouls.Add(new Ghoul { Id = nextId++, Kind = kind, Pos = p, Dir = RandDir(), Hp = d.Hp, MaxHp = d.Hp, Hostile = true }); GhoulsSpawned++;
        }
        Announce($"Anoiteceu: {n} ghouls! Novo tipo: {Defs.GhoulNames[(int)kinds[^1]]}. Fique perto de uma fogueira acesa.");
    }

    void RotFood()
    {
        foreach (var a in Agents)
        {
            if (!a.Alive) continue; int bad = 0;
            for (int i = 0; i < a.Slots.Length; i++)
            {
                var s = a.Slots[i]; if (s.N <= 0 || !Defs.IsFood(s.Item)) continue;
                int rot = 0; for (int k = 0; k < s.N; k++) if (rng.Chance(Cfg.RotChance)) rot++;
                if (rot == 0) continue; bad += rot;
                a.Slots[i].N -= rot; var to = s.Item == Meat || s.Item == CookedMeat ? RottenMeat : RottenOrange;
                if (a.Slots[i].N <= 0) a.Slots[i] = default;
                AddItem(a, to, rot);
            }
            if (bad > 0) Say(a, $"{bad} comida(s) apodreceram. (Geladeira não estraga.)");
        }
    }
}
