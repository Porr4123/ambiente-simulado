using System.Numerics;
using static SimBox.Item;
namespace SimBox;

public sealed partial class World
{
    public long BuildingsLost;

    public bool InSun(Vector2 p)
    {
        if (IsDay) return true;
        float r2 = Cfg.FlareRadius * Cfg.FlareRadius;
        foreach (var f in Flares) if (Vector2.DistanceSquared(f.Pos, p) <= r2) return true;
        return false;
    }

    // ================================================================== ghouls
    void UpdateGhouls(float dt)
    {
        for (int i = 0; i < Ghouls.Count; i++)
        {
            var g = Ghouls[i]; if (!g.Alive) continue;
            var d = Defs.Ghouls[(int)g.Kind]; bool sun = InSun(g.Pos);
            g.Cd = MathF.Max(0, g.Cd - dt);
            if (sun) { g.Hp -= g.MaxHp / Cfg.SunDeathSeconds * dt; if (g.Hp <= 0) { KillGhoul(g, null); continue; } }
            if (g.Kind == GhoulKind.Radioactive)
                foreach (var a in Agents) if (a.Alive && Vector2.DistanceSquared(a.Pos, g.Pos) < 240f * 240f) a.Rad = MathF.Min(Defs.StatMax, a.Rad + 30f * dt);

            // alvo: agente ou robô mais próximo dentro do raio de detecção
            Agent? ta = null; Robot? tr = null; float best = d.Detect * d.Detect; Vector2 tp = default; float tradius = 30;
            foreach (var a in Agents) { if (!a.Alive) continue; float dd = Vector2.DistanceSquared(a.Pos, g.Pos); if (dd < best) { best = dd; ta = a; tr = null; tp = a.Pos; tradius = a.Radius; } }
            foreach (var r in Robots) { if (!r.Alive) continue; float dd = Vector2.DistanceSquared(r.Pos, g.Pos); if (dd < best) { best = dd; tr = r; ta = null; tp = r.Pos; tradius = 30; } }
            bool hostile = g.Hostile || Time >= d.UnlockCycle * Cfg.Cycle;
            float dmg = IsDay || sun ? d.DmgDay : d.DmgNight;
            if ((ta != null || tr != null) && hostile)
            {
                var dir = tp - g.Pos; float dist = dir.Length(); if (dist > 0.01f) dir /= dist;
                if (dist <= d.Radius + tradius + 12f)
                {
                    if (g.Cd <= 0)
                    {
                        g.Cd = d.AttackCd; Emit(9, g.Pos.X, g.Pos.Y, dir.X, dir.Y, (int)g.Kind);
                        if (ta != null) { HurtAgent(ta, dmg, true); Say(ta, $"{Defs.GhoulNames[(int)g.Kind]} te atacou (-{dmg * (1 - Defs.ArmorReduction[ArmorLevel(ta)]):0})"); }
                        else { tr!.Hp -= dmg; if (tr.Hp <= 0) tr.Alive = false; }
                    }
                }
                else
                {
                    int cell = CellOf(g.Pos + dir * (d.Radius + 16f));
                    if (cell >= 0 && BldGrid[cell] != 0 && Defs.Blds[BldGrid[cell]].SolidMob)
                    {
                        if (g.Cd <= 0) { g.Cd = d.AttackCd; Emit(9, g.Pos.X, g.Pos.Y, dir.X, dir.Y, (int)g.Kind); if (g.Kind == GhoulKind.Explosive) { KillGhoul(g, null); continue; } DamageBuilding(cell, dmg * Cfg.GhoulStructMult); }
                    }
                    else g.Pos = Resolve(g.Pos + dir * ((sun ? d.SpeedDay : d.SpeedNight) * 10f) * dt, d.Radius, false);
                }
                g.Dir = dir;
            }
            else
            {
                g.WanderT -= dt;
                if (g.WanderT <= 0) { g.Dir = rng.Chance(0.3f) ? Vector2.Zero : RandDir(); g.WanderT = rng.Float(2, 5); }
                if (g.Dir != Vector2.Zero) g.Pos = Resolve(g.Pos + g.Dir * 40f * dt, d.Radius, false);
            }
        }
        // separação entre ghouls (evita empilhar)
        if (Ghouls.Count > 1 && Ghouls.Count < 300)
            for (int i = 0; i < Ghouls.Count; i++)
                for (int j = i + 1; j < Ghouls.Count; j++)
                {
                    var a = Ghouls[i]; var b = Ghouls[j]; if (!a.Alive || !b.Alive) continue;
                    var dd = b.Pos - a.Pos; float m = Defs.Ghouls[(int)a.Kind].Radius + Defs.Ghouls[(int)b.Kind].Radius, l2 = dd.LengthSquared();
                    if (l2 < m * m && l2 > 0.01f) { float l = MathF.Sqrt(l2); var push = dd / l * ((m - l) * 0.5f); a.Pos -= push; b.Pos += push; }
                }
        Ghouls.RemoveAll(g => !g.Alive);
    }

    public void DamageGhoul(Ghoul g, float dmg, Agent? src, Item weapon = Wood)
    {
        if (!g.Alive) return;
        if (src != null) Rewards[src.Id] += Rw.HitGhoul * MathF.Min(dmg, MathF.Max(0, g.Hp)) * Defs.TechMult(weapon);   // bônus de combate cresce com a tecnologia da arma
        g.Hp -= dmg; g.Hostile = true; Emit(3, g.Pos.X, g.Pos.Y);
        if (g.Hp <= 0) KillGhoul(g, src, weapon);
    }

    void KillGhoul(Ghoul g, Agent? src, Item weapon = Wood)
    {
        if (!g.Alive) return; g.Alive = false; KillsByKind[(int)g.Kind]++;
        var d = Defs.Ghouls[(int)g.Kind];
        Emit(7, g.Pos.X, g.Pos.Y, 0, 0, (int)g.Kind);
        if (src != null) { src.Kills++; AddXp(src, d.Xp); Rewards[src.Id] += (Rw.KillGhoulBase + Rw.KillGhoulPerKind * (int)g.Kind) * Defs.TechMult(weapon); Say(src, $"{d.Name} abatido (+{d.Xp} XP)."); }
        switch (g.Kind)   // [wiki] drops
        {
            case GhoulKind.Normal: DropAt(g.Pos, Fat, 1, 0); break;
            case GhoulKind.Fast: DropAt(g.Pos, GhoulBlood, 1, 0); break;
            case GhoulKind.Explosive: DropAt(g.Pos, Fat, rng.Range(1, 2), 0); DropAt(g.Pos, Sulfur, rng.Range(1, 3), 0); DropAt(g.Pos, Scrap, rng.Range(1, 3), 0); break;
            case GhoulKind.Radioactive: DropAt(g.Pos, Uranium, 1, 0); break;
            case GhoulKind.Armored: DropAt(g.Pos, Alloy, rng.Range(1, 2), 0); DropAt(g.Pos, Iron, 3, 0); break;
        }
        if (g.Kind == GhoulKind.Explosive) Explode(g.Pos);
    }

    void Explode(Vector2 p)
    {
        Emit(4, p.X, p.Y, 220);
        foreach (var a in Agents) if (a.Alive && Vector2.DistanceSquared(a.Pos, p) < 220f * 220f) HurtAgent(a, 127f, false);      // ~metade da vida [wiki]
        foreach (var r in Robots) if (r.Alive && Vector2.DistanceSquared(r.Pos, p) < 220f * 220f) { r.Hp -= 200; if (r.Hp <= 0) r.Alive = false; }
        int cx = (int)(p.X / Cfg.Cell), cy = (int)(p.Y / Cfg.Cell);
        for (int gy = cy - 2; gy <= cy + 2; gy++)
            for (int gx = cx - 2; gx <= cx + 2; gx++)
            {
                if (gx < 0 || gy < 0 || gx >= GW || gy >= GH) continue; int c = gy * GW + gx;
                if (BldGrid[c] != 0 && Vector2.DistanceSquared(CellCenter(c), p) < 240f * 240f) DamageBuilding(c, 600f);
            }
    }

    public void DamageBuilding(int cell, float dmg)
    {
        if (BldGrid[cell] == 0) return;
        bldHp[cell] -= dmg; Emit(3, CellCenter(cell).X, CellCenter(cell).Y);
        if (bldHp[cell] > 0) return;
        if (boxes.TryGetValue(cell, out var box)) for (int k = 0; k < box.Length; k++) if (box[k] > 0) DropAt(CellCenter(cell), (Item)k, box[k], 0);
        RemoveBuilding(cell, null); BuildingsLost++;
    }
    public float BuildingHp(int cell) => bldHp[cell];
    public int DebugPlace(Bld b, int gx, int gy) { int c = gy * GW + gx; BldGrid[c] = (byte)b; bldHp[c] = Defs.Blds[(int)b].Hp; if (b == Bld.Chest || b == Bld.Fridge) boxes[c] = new int[Defs.ItemCount]; if (b == Bld.Campfire || b == Bld.Abrasador) fuel[c] = 600; return c; }
    public void DebugSetTime(float t) { Time = t; prevTin = TimeInCycle; }
    public Node DebugNode(NodeKind k, Vector2 p) => AddNode(k, p);
    public void DebugClearArea(Vector2 c, float r) { foreach (var n in Nodes) if (n.Alive && Vector2.Distance(n.Pos, c) < r) n.Alive = false; }
    public Ghoul DebugGhoul(GhoulKind k, Vector2 p, bool hostile = true) { var d = Defs.Ghouls[(int)k]; var g = new Ghoul { Id = nextId++, Kind = k, Pos = p, Hp = d.Hp, MaxHp = d.Hp, Hostile = hostile }; Ghouls.Add(g); return g; }

    // ================================================================== animais
    void UpdateAnimals(float dt)
    {
        foreach (var an in Animals)
        {
            if (!an.Alive) continue;
            an.FleeT = MathF.Max(0, an.FleeT - dt);
            Vector2 move = Vector2.Zero;
            if (an.FleeT > 0)
            {
                Agent? near = null; float bd = 400 * 400;
                foreach (var a in Agents) { if (!a.Alive) continue; float dd = Vector2.DistanceSquared(a.Pos, an.Pos); if (dd < bd) { bd = dd; near = a; } }
                move = near != null ? Vector2.Normalize(an.Pos - near.Pos) * 220f : an.Dir * 120f;
            }
            else
            {
                an.WanderT -= dt; if (an.WanderT <= 0) { an.Dir = rng.Chance(0.4f) ? Vector2.Zero : RandDir(); an.WanderT = rng.Float(2, 6); }
                move = an.Dir * 50f;
            }
            if (move != Vector2.Zero) an.Pos = Resolve(an.Pos + move * dt, 24, false);
        }
        Animals.RemoveAll(a => !a.Alive);
    }
    public void DamageAnimal(Animal an, float dmg, Agent? src)
    {
        if (!an.Alive) return; an.Hp -= dmg; an.FleeT = 5; Emit(3, an.Pos.X, an.Pos.Y);
        if (an.Hp > 0) return;
        an.Alive = false; Carcasses.Add(new Carcass { Id = nextId++, Pos = an.Pos, Left = 4, DecayAt = Time + 180 });
        if (src != null) { AddXp(src, 20); Rewards[src.Id] += 0.1f; }
    }

    // ================================================================== robôs (Hall 9000 = torreta de regras fixas; Lapada bot = reparo/corpo a corpo)
    void UpdateRobots(float dt)
    {
        foreach (var r in Robots)
        {
            if (!r.Alive) continue; r.Cd = MathF.Max(0, r.Cd - dt);
            Ghoul? tg = null; float bd = float.MaxValue;
            foreach (var g in Ghouls) { if (!g.Alive) continue; float dd = Vector2.DistanceSquared(g.Pos, r.Pos); if (dd < bd) { bd = dd; tg = g; } }
            if (r.Hall)
            {
                // só atira em ghouls: nunca em agentes nem em outros robôs do time
                if (tg != null && bd <= 700f * 700f && r.Cd <= 0) { r.Cd = 0.35f; Emit(1, r.Pos.X, r.Pos.Y, tg.Pos.X, tg.Pos.Y, -1); DamageGhoul(tg, 25f, null); }
            }
            else
            {
                const float speed = 80f;     // [wiki] lapabot_repair: velocidade 8
                if (tg != null && bd <= 600f * 600f)
                {
                    float dist = MathF.Sqrt(bd);
                    if (dist <= 80f) { if (r.Cd <= 0) { r.Cd = 0.8f; DamageGhoul(tg, 30f, null); } }
                    else r.Pos = Resolve(r.Pos + Vector2.Normalize(tg.Pos - r.Pos) * speed * dt, 30, true);
                    continue;
                }
                int bc = -1; float bdist = 500f * 500f; int rr = 5; int cx = (int)(r.Pos.X / Cfg.Cell), cy = (int)(r.Pos.Y / Cfg.Cell);
                for (int gy = Math.Max(0, cy - rr); gy <= Math.Min(GH - 1, cy + rr); gy++)
                    for (int gx = Math.Max(0, cx - rr); gx <= Math.Min(GW - 1, cx + rr); gx++)
                    {
                        int c = gy * GW + gx; if (BldGrid[c] == 0 || bldHp[c] >= Defs.Blds[BldGrid[c]].Hp) continue;
                        float dd = Vector2.DistanceSquared(CellCenter(c), r.Pos); if (dd < bdist) { bdist = dd; bc = c; }
                    }
                if (bc < 0) continue;
                if (bdist <= 110f * 110f) bldHp[bc] = MathF.Min(Defs.Blds[BldGrid[bc]].Hp, bldHp[bc] + 60f * dt);
                else r.Pos = Resolve(r.Pos + Vector2.Normalize(CellCenter(bc) - r.Pos) * speed * dt, 30, true);
            }
        }
        Robots.RemoveAll(r => !r.Alive);
    }

    // ================================================================== projéteis (flechas, balas, chumbo, lanças)
    void UpdateProjectiles(float dt)
    {
        for (int i = Projs.Count - 1; i >= 0; i--)
        {
            var p = Projs[i]; float travel = p.Speed * dt; int steps = Math.Max(1, (int)MathF.Ceiling(travel / 20f)); bool dead = false;
            for (int s = 1; s <= steps && !dead; s++)
            {
                var np = p.Pos + p.Dir * (travel * s / steps);
                if (WallActive && MathF.Abs(np.X - W / 2) <= Cfg.Cell / 2) { dead = true; break; }      // o muro do meio para qualquer projétil
                Agent? owner = !p.FromRobot && p.OwnerId >= 0 && p.OwnerId < Agents.Length ? Agents[p.OwnerId] : null;
                foreach (var g in Ghouls)
                    if (g.Alive && Vector2.DistanceSquared(np, g.Pos) <= MathF.Pow(Defs.Ghouls[(int)g.Kind].Radius + 6, 2)) { DamageGhoul(g, p.Dmg, owner, p.Kind); dead = true; break; }
                if (dead) break;
                foreach (var o in Agents)      // tiro/flecha/lança também acerta agentes (fogo amigo incluso); o dono nunca se acerta
                    if (o.Alive && o.Id != p.OwnerId && Vector2.DistanceSquared(np, o.Pos) <= (o.Radius + 6) * (o.Radius + 6))
                    { if (owner != null) DamageAgentBy(o, p.Dmg, owner, p.Kind, false); else HurtAgent(o, p.Dmg, false); dead = true; break; }
                if (dead) break;
                foreach (var an in Animals) if (an.Alive && Vector2.DistanceSquared(np, an.Pos) <= 28f * 28f) { DamageAnimal(an, p.Dmg, owner); dead = true; break; }
                if (dead) break;
                foreach (var n in NodesNear(np, 70)) if (n.Alive && n.Solid && n.Kind != NodeKind.Crate && Vector2.DistanceSquared(np, n.Pos) <= n.R * n.R) { dead = true; break; }
                if (dead) break;
                int c = CellOf(np);
                if (c < 0) { dead = true; break; }
                if (BldGrid[c] != 0 && Defs.Blds[BldGrid[c]].BlocksBullets) dead = true;      // muros/portões altos param a bala; baixos e firepit não [wiki]
            }
            if (!dead) { p.Pos += p.Dir * travel; p.Left -= travel; if (p.Left <= 0) dead = true; }
            else p.Pos += p.Dir * travel * 0.5f;
            if (dead) { if (p.Kind == Spear) DropAt(p.Pos, Spear, 1, 0); Projs.RemoveAt(i); }
        }
    }
}
