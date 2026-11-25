using System.Numerics;
using static SimBox.Item;
namespace SimBox;

public sealed partial class World
{
    // ================================================================== inventário (slots, pilha máx. 255)
    public int FreeSpace(Agent a, Item it)
    {
        int space = 0;
        foreach (var s in a.Slots) { if (s.N <= 0) space += Defs.StackMax; else if (s.Item == it) space += Defs.StackMax - s.N; }
        return space;
    }
    /// <summary>Adiciona itens; devolve o que NÃO coube.</summary>
    public int AddItem(Agent a, Item it, int n)
    {
        for (int i = 0; i < a.Slots.Length && n > 0; i++)
            if (a.Slots[i].N > 0 && a.Slots[i].Item == it) { int t = Math.Min(n, Defs.StackMax - a.Slots[i].N); a.Slots[i].N += t; n -= t; }
        for (int i = 0; i < a.Slots.Length && n > 0; i++)
            if (a.Slots[i].N <= 0) { int t = Math.Min(n, Defs.StackMax); a.Slots[i] = new Slot { Item = it, N = t }; n -= t; }
        return n;
    }
    public bool RemoveItem(Agent a, Item it, int n)
    {
        if (a.Count(it) < n) return false;
        for (int i = a.Slots.Length - 1; i >= 0 && n > 0; i--)
            if (a.Slots[i].N > 0 && a.Slots[i].Item == it) { int t = Math.Min(n, a.Slots[i].N); a.Slots[i].N -= t; n -= t; if (a.Slots[i].N <= 0) a.Slots[i] = default; }
        return true;
    }
    void GiveOrDrop(Agent a, Item it, int n) { int left = AddItem(a, it, n); if (left > 0) DropAt(a.Pos, it, left, 0); }
    void DropAt(Vector2 p, Item it, int n, float noPick)
    {
        Drops.Add(new GroundItem { Id = nextId++, Item = it, N = n, Pos = p + RandDir() * rng.Float(10, 40), DieAt = Time + 300, NoPick = Time + noPick });
    }
    public Item? SelectedItem(Agent a) => a.Selected >= 0 && a.Selected < a.Slots.Length && a.Slots[a.Selected].N > 0 ? a.Slots[a.Selected].Item : null;
    public static int ArmorLevel(Agent a) => a.Has(ArmorGladiator) ? 3 : a.Has(ArmorSmith) ? 2 : a.Has(ArmorBear) ? 1 : 0;

    // ================================================================== XP / pesquisa
    public void AddXp(Agent a, float xp)
    {
        a.Xp += xp;
        while (Defs.LevelFor(a.Xp) > a.Level) { a.Level++; a.SkillPts++; Say(a, $"Nível {a.Level}! +1 ponto de habilidade (tecla R: pesquisar)."); }
    }
    public string? ResearchCheck(Agent a, Item item)
    {
        var rec = Defs.Recipes.FirstOrDefault(r => r.Out == item && r.SkillPts > 0);
        if (rec == null) return "não precisa pesquisar";
        if (a.Unlocked[(int)item]) return "já pesquisado";
        if (a.Level < rec.Level) return $"precisa de nível {rec.Level}";
        if (a.SkillPts < rec.SkillPts) return "sem pontos de habilidade";
        if (rec.Prereq is Item pre && !a.Unlocked[(int)pre]) return $"pesquise antes: {Defs.Name(pre)}";
        return null;
    }
    public bool Research(Agent a, Item item)
    {
        var why = ResearchCheck(a, item);
        if (why != null) { Say(a, "Pesquisa: " + why); return false; }
        var rec = Defs.Recipes.First(r => r.Out == item && r.SkillPts > 0);
        a.SkillPts -= rec.SkillPts; a.Unlocked[(int)item] = true; Say(a, $"Pesquisado: {Defs.Name(item)}"); return true;
    }

    // ================================================================== loop do agente
    void UpdateAgent(Agent a, in AgentInput inp, float dt)
    {
        a.Aim = inp.Aim;
        a.AttackCd = MathF.Max(0, a.AttackCd - dt);
        if (a.ReloadLeft > 0) { a.ReloadLeft -= dt; if (a.ReloadLeft <= 0) FinishReload(a); }
        if (inp.Cmd != Cmd.None) DoCmd(a, inp);
        if (!a.Alive) return;

        // movimento
        var dir = new Vector2(inp.Mx, inp.My); if (dir.LengthSquared() > 1f) dir = Vector2.Normalize(dir);
        bool moving = dir.LengthSquared() > 0.01f, sprint = inp.Sprint && moving && a.Stamina > 4f;
        float speed = sprint ? Cfg.SprintSpeed : Cfg.WalkSpeed; if (a.CraftLeft > 0) speed *= Cfg.CraftSlow;
        a.Vel = dir * speed;
        if (moving) a.Pos = Resolve(a.Pos + a.Vel * dt, a.Radius, true);
        a.Stamina = Math.Clamp(a.Stamina + (sprint ? -Cfg.StaminaDrain : Cfg.StaminaRegen) * dt, 0, Defs.StatMax);

        // sobrevivência [wiki]
        a.Hunger = MathF.Max(0, a.Hunger - Cfg.HungerRate * dt);
        float coldRate = a.Has(WinterCoat) ? 1f : a.Has(Chapka) ? 2f : a.Has(Headscarf) ? 3f : Cfg.ColdRate;
        if (NearLitFire(a.Pos)) a.Cold += Cfg.ColdRegenFire * dt;
        else if (IsDay) a.Cold += Cfg.ColdRegenDay * dt;
        else a.Cold -= coldRate * dt;
        a.Cold = Math.Clamp(a.Cold, 0, Defs.StatMax);
        if (a.Hunger <= 0) HurtAgent(a, Cfg.StarveDamage * dt, false);
        if (a.Cold <= 0) HurtAgent(a, Cfg.FreezeDamage * dt, false);
        a.Rad = MathF.Max(0, a.Rad - 2f * dt);
        if (a.Rad > 128) HurtAgent(a, (a.Rad - 128) / 25f * dt, false);
        if (a.Alive && Time - a.LastHurt > 6f && a.Hunger > 0 && a.Cold > 0 && a.Rad < 128) a.Hp = MathF.Min(Defs.StatMax, a.Hp + Cfg.HealthRegen * dt);
        Rewards[a.Id] += 0.0005f * dt * Cfg.Tps;

        // fabricação em andamento
        if (a.CraftLeft > 0 && (a.CraftLeft -= dt) <= 0) FinishCraft(a);
        // coleta automática de itens no chão
        for (int i = Drops.Count - 1; i >= 0; i--)
        {
            var d = Drops[i]; if (Time < d.NoPick || Vector2.DistanceSquared(d.Pos, a.Pos) > Cfg.PickupRadius * Cfg.PickupRadius) continue;
            int left = AddItem(a, d.Item, d.N);
            if (left < d.N) { Say(a, $"+{d.N - left} {Defs.Name(d.Item)}"); Rewards[a.Id] += 0.01f; }
            if (left == 0) Drops.RemoveAt(i); else d.N = left;
        }
        if (inp.Attack) UseSelected(a, inp);
    }

    public void HurtAgent(Agent a, float dmg, bool melee)
    {
        if (!a.Alive || dmg <= 0) return;
        if (melee) dmg *= 1f - Defs.ArmorReduction[ArmorLevel(a)];
        a.Hp -= dmg; a.LastHurt = Time; Rewards[a.Id] -= 0.002f * dmg;
        if (a.Hp <= 0) Die(a);
    }
    void Die(Agent a)
    {
        a.Alive = false; a.Hp = 0; Deaths++; Rewards[a.Id] -= 3f; a.CraftLeft = 0; a.CraftRecipe = -1;
        for (int i = 0; i < a.Slots.Length; i++) if (a.Slots[i].N > 0) { DropAt(a.Pos, a.Slots[i].Item, a.Slots[i].N, 2f); a.Slots[i] = default; }
        Emit(8, a.Pos.X, a.Pos.Y, 0, 0, a.Id); Say(a, "Você morreu. Pressione o botão para renascer com metade do nível.");
    }
    public void Respawn(Agent a)
    {
        a.Xp = Defs.XpFor(a.Level / 2); ResetAgent(a, false);
        a.Pos = new Vector2(W / 2 + rng.Float(-120, 120), H / 2 + rng.Float(-120, 120));
        Say(a, $"Você renasceu no nível {a.Level}.");
    }

    // ================================================================== comandos
    void DoCmd(Agent a, in AgentInput inp)
    {
        switch (inp.Cmd)
        {
            case Cmd.Select: if (inp.Arg >= 0 && inp.Arg < a.Slots.Length) { a.Selected = inp.Arg; a.ReloadLeft = 0; } break;
            case Cmd.Craft: StartCraft(a, inp.Arg); break;
            case Cmd.Research: if (inp.Arg >= 0 && inp.Arg < Defs.ItemCount) Research(a, (Item)inp.Arg); break;
            case Cmd.Interact: Interact(a); break;
            case Cmd.Store: Store(a); break;
            case Cmd.Take: Take(a); break;
            case Cmd.Drop: DropSelected(a); break;
            case Cmd.Reload: ReloadSelected(a); break;
            case Cmd.Eat: if (inp.Arg >= 0 && inp.Arg < Defs.ItemCount) EatItem(a, (Item)inp.Arg); break;
        }
    }

    void DropSelected(Agent a)
    {
        if (a.Selected < 0 || a.Selected >= a.Slots.Length || a.Slots[a.Selected].N <= 0) return;
        var s = a.Slots[a.Selected]; a.Slots[a.Selected] = default; DropAt(a.Pos + new Vector2(MathF.Cos(a.Aim), MathF.Sin(a.Aim)) * 60, s.Item, s.N, 1.5f);
    }

    // ---------------------------------------------------------------- estações e fogo
    public int FindStation(Vector2 p, Bld b, float reach = 260f)
    {
        int best = -1; float bd = reach * reach; int r = (int)(reach / Cfg.Cell) + 1;
        int cx = (int)(p.X / Cfg.Cell), cy = (int)(p.Y / Cfg.Cell);
        for (int gy = Math.Max(0, cy - r); gy <= Math.Min(GH - 1, cy + r); gy++)
            for (int gx = Math.Max(0, cx - r); gx <= Math.Min(GW - 1, cx + r); gx++)
            {
                int i = gy * GW + gx; if (BldGrid[i] != (byte)b) continue;
                float d = Vector2.DistanceSquared(CellCenter(i), p); if (d < bd) { bd = d; best = i; }
            }
        return best;
    }
    public bool NearLitFire(Vector2 p)
    {
        float r2 = Cfg.FireRadius * Cfg.FireRadius;
        foreach (var kv in fuel) if (kv.Value > 0 && Vector2.DistanceSquared(CellCenter(kv.Key), p) <= r2) return true;
        return false;
    }
    public bool IsLit(int cell) => fuel.TryGetValue(cell, out var f) && f > 0;
    public float FuelLeft(int cell) => fuel.TryGetValue(cell, out var f) ? f : 0;
    void UpdateFires(float dt)
    {
        if (fuel.Count == 0) return;
        foreach (var k in fuel.Keys.ToList()) { fuel[k] -= dt; if (fuel[k] <= 0) fuel.Remove(k); }
    }
    public int SmelterCharge(int cell) => charge.GetValueOrDefault(cell);
    public int BoxCount(int cell, Item it) => boxes.TryGetValue(cell, out var b) ? b[(int)it] : -1;

    void Interact(Agent a)
    {
        int f = FindStation(a.Pos, Bld.Campfire, 220f), g = FindStation(a.Pos, Bld.Abrasador, 220f);
        int cell = f >= 0 && (g < 0 || Vector2.DistanceSquared(CellCenter(f), a.Pos) <= Vector2.DistanceSquared(CellCenter(g), a.Pos)) ? f : g;
        if (cell < 0) { Say(a, "Nada para interagir por perto."); return; }
        int n = Math.Min(10, a.Count(Wood)); if (n <= 0) { Say(a, "Você precisa de madeira para alimentar o fogo."); return; }
        RemoveItem(a, Wood, n); fuel[cell] = MathF.Min(Cfg.FireMaxSeconds, FuelLeft(cell) + n * Cfg.FireWoodSeconds);
        Say(a, $"Fogo alimentado: {FuelLeft(cell):0}s de combustível."); Rewards[a.Id] += 0.02f;
    }

    void Store(Agent a)
    {
        bool any = false; int c = FindStation(a.Pos, Bld.Chest, 220f), f = FindStation(a.Pos, Bld.Fridge, 220f);
        if (c < 0 && f < 0) { Say(a, "Nenhum baú/geladeira por perto."); return; }
        if (c >= 0) any |= MoveToBox(a, c, Defs.ChestItems);
        if (f >= 0) any |= MoveToBox(a, f, Defs.FridgeItems);
        if (!any) Say(a, "Nada para guardar (baú não guarda comida; geladeira só comida) ou está cheio.");
    }
    bool MoveToBox(Agent a, int cell, Item[] allowed)
    {
        var box = boxes[cell]; bool moved = false;
        foreach (var it in allowed)
        {
            int have = a.Count(it); if (have <= 0) continue;
            int slots = 0; for (int i = 0; i < box.Length; i++) slots += (box[i] + Defs.StackMax - 1) / Defs.StackMax;
            int room = (Cfg.ChestSlots - slots) * Defs.StackMax + (box[(int)it] > 0 ? (Defs.StackMax - box[(int)it] % Defs.StackMax) % Defs.StackMax : 0);
            int t = Math.Min(have, room); if (t <= 0) continue;
            RemoveItem(a, it, t); box[(int)it] += t; moved = true; Rewards[a.Id] += 0.005f * t;
        }
        return moved;
    }
    void Take(Agent a)
    {
        bool any = false;
        foreach (var b in new[] { Bld.Chest, Bld.Fridge })
        {
            int cell = FindStation(a.Pos, b, 220f); if (cell < 0) continue; var box = boxes[cell];
            for (int i = 0; i < box.Length; i++)
            {
                if (box[i] <= 0) continue; int left = AddItem(a, (Item)i, box[i]); if (left < box[i]) any = true; box[i] = left;
            }
        }
        if (!any) Say(a, "Nada para pegar (ou inventário cheio).");
    }

    // ================================================================== fabricação (com tempo)
    public string? CraftCheck(Agent a, int r, out int station)
    {
        station = -1;
        if (r < 0 || r >= Defs.Recipes.Length) return "receita inválida";
        var rec = Defs.Recipes[r];
        if (!a.Alive) return "morto";
        if (!rec.BotOk && !a.IsHuman) return "bots não conseguem fabricar isto";
        if (a.CraftLeft > 0) return "já está fabricando algo";
        if (!a.Unlocked[(int)rec.Out]) return "precisa pesquisar antes (tecla R)";
        if (rec.SkillPts == 0 && rec.Level > 1 && a.Level < rec.Level) return $"precisa de nível {rec.Level}";   // nível 1 = disponível desde o início
        if (rec.Station != Bld.None)
        {
            station = FindStation(a.Pos, rec.Station);
            if (station < 0 && rec.Alt != Bld.None) station = FindStation(a.Pos, rec.Alt);
            if (station < 0 && rec.Alt != Bld.None && rec.Alt == Bld.None) station = -1;
            if (station < 0) return $"precisa estar perto de: {Defs.Blds[(int)rec.Station].Name}";
            var sb = (Bld)BldGrid[station];
            if ((sb == Bld.Campfire || sb == Bld.Abrasador) && !IsLit(station)) return "o fogo está apagado (interaja com madeira)";
        }
        foreach (var (it, n) in rec.In) if (a.Count(it) < n) return $"falta {n - a.Count(it)} {Defs.Name(it)}";
        if (station >= 0 && BldGrid[station] == (byte)Bld.Smelter && charge.GetValueOrDefault(station) <= 0 && !a.Has(Gasoline) && !a.Has(Diesel))
            return "smelter sem combustível (Gasolina ou Diesel)";
        return null;
    }
    void StartCraft(Agent a, int r)
    {
        var why = CraftCheck(a, r, out int station);
        if (why != null) { Say(a, "Não dá: " + why); return; }
        var rec = Defs.Recipes[r];
        if (station >= 0 && BldGrid[station] == (byte)Bld.Smelter)
        {
            int c = charge.GetValueOrDefault(station);
            if (c <= 0) { if (a.Has(Diesel)) { RemoveItem(a, Diesel, 1); c = 12; } else { RemoveItem(a, Gasoline, 1); c = 4; } }
            charge[station] = c - 1;                       // 1 gasolina = 4 operações; 1 diesel = 12 [user]
        }
        foreach (var (it, n) in rec.In) RemoveItem(a, it, n);
        a.CraftTotal = a.CraftLeft = MathF.Max(0.05f, rec.Secs * Cfg.CraftTimeScale); a.CraftRecipe = r;
    }
    void FinishCraft(Agent a)
    {
        var rec = Defs.Recipes[a.CraftRecipe]; a.CraftRecipe = -1;
        GiveOrDrop(a, rec.Out, rec.OutN);
        AddXp(a, 10 + 12 * rec.Level); Rewards[a.Id] += 0.05f;
        Say(a, $"Pronto: {Defs.Name(rec.Out)} x{rec.OutN}");
    }

    // ================================================================== uso do item selecionado (botão de ataque)
    void UseSelected(Agent a, in AgentInput inp)
    {
        if (a.AttackCd > 0 || a.ReloadLeft > 0) return;
        var sel = SelectedItem(a);
        if (sel is Item it)
        {
            if (Defs.Edible(it) != null) { EatItem(a, it); return; }
            if (Defs.ToolOf(it) is ToolDef td) { if (it == Hammer) UseHammer(a, td); else if (it == Sledge) UseSledge(a, td); else Melee(a, td); return; }
            if (Defs.GunOf(it) is Gun g) { Fire(a, g); return; }
            if (it == Spear) { ThrowSpear(a); return; }
            if (it == Flare) { UseFlare(a); return; }
            if (it == OrangeSeed || it == TomatoSeed) { Plant(a, it, inp); return; }
            if (Array.IndexOf(Defs.Placeables, it) >= 0) { Place(a, it, inp); return; }
        }
        Melee(a, Defs.Hand);
    }

    bool EatItem(Agent a, Item it)
    {
        if (Defs.Edible(it) is not (float food, float hp, float st) || a.Count(it) <= 0) return false;
        if (a.AttackCd > 0) return false;
        if (hp >= 0 && a.Hunger > Defs.StatMax - 20 && a.Hp >= Defs.StatMax - 5 && it != Bandage) return false;
        RemoveItem(a, it, 1); a.AttackCd = 0.8f;
        a.Hunger = MathF.Min(Defs.StatMax, a.Hunger + food); a.Stamina = MathF.Min(Defs.StatMax, a.Stamina + st);
        if (hp > 0) a.Hp = MathF.Min(Defs.StatMax, a.Hp + hp); else if (hp < 0) HurtAgent(a, -hp, false);
        Rewards[a.Id] += 0.03f; return true;
    }

    static bool InArc(Vector2 origin, Vector2 dir, Vector2 target, float tr, float reach, float half)
    {
        var d = target - origin; float dist = d.Length();
        if (dist > reach + tr) return false; if (dist <= tr) return true;
        float cos = Vector2.Dot(dir, d / dist);
        return MathF.Acos(Math.Clamp(cos, -1f, 1f)) <= half + MathF.Asin(MathF.Min(1f, tr / dist));
    }

    void Melee(Agent a, ToolDef t)
    {
        if (a.Stamina < 1) return;
        a.AttackCd = t.Cd; a.Stamina = MathF.Max(0, a.Stamina - t.Stamina);
        var dir = new Vector2(MathF.Cos(a.Aim), MathF.Sin(a.Aim)); float reach = a.Radius + 62f, half = 1.1f;
        Emit(2, a.Pos.X, a.Pos.Y, a.Aim);
        bool hitSomething = false;
        foreach (var g in Ghouls) if (g.Alive && InArc(a.Pos, dir, g.Pos, Defs.Ghouls[(int)g.Kind].Radius, reach, half)) { DamageGhoul(g, t.Dmg, a); hitSomething = true; }
        foreach (var an in Animals) if (an.Alive && InArc(a.Pos, dir, an.Pos, 24, reach, half)) { DamageAnimal(an, t.Dmg, a); hitSomething = true; }
        for (int i = Carcasses.Count - 1; i >= 0; i--)
        {
            var c = Carcasses[i]; if (!InArc(a.Pos, dir, c.Pos, 26, reach, half)) continue;
            int mult = t.Item == AxeIron || t.Item == AxeSulfur ? 3 : 1;            // [wiki] machado de aço rende 3x dos animais
            GiveOrDrop(a, Meat, mult); GiveOrDrop(a, Hide, mult); GiveOrDrop(a, Fat, mult); GiveOrDrop(a, Sinew, mult);
            AddXp(a, 4); hitSomething = true; Emit(5, c.Pos.X, c.Pos.Y, 0, 0, 9);
            if (--c.Left <= 0) Carcasses.RemoveAt(i);
        }
        foreach (var n in NodesNear(a.Pos, reach + 80))
        {
            if (!n.Alive) continue; float nr = n.R + (n.Kind == NodeKind.Tree ? 14 : 8);
            if (!InArc(a.Pos, dir, n.Pos, nr, reach, half)) continue;
            if (Harvest(a, n, t)) hitSomething = true;
        }
        if (hitSomething) Rewards[a.Id] += 0.005f;
    }

    bool Harvest(Agent a, Node n, ToolDef t)
    {
        (int lo, int hi) y = default; Item item = Wood; int xp = 0;
        switch (n.Kind)
        {
            case NodeKind.Tree: y = t.Wood; item = Wood; xp = t.XpTree; break;
            case NodeKind.Rock: y = t.Stone; item = Stone; xp = t.XpStone; break;
            case NodeKind.IronOre: y = t.Iron; item = Ore; xp = t.XpIron; break;
            case NodeKind.SulfurOre: y = t.Sulfur; item = Sulfur; xp = t.XpSulfur; break;
            case NodeKind.OrangeBush: case NodeKind.TomatoBush:
                if (n.Fruit <= 0) return false;
                { int take = Math.Min(n.Fruit, rng.Range(2, 3)); n.Fruit -= take; GiveOrDrop(a, n.Kind == NodeKind.OrangeBush ? Orange : Tomato, take); AddXp(a, 3); Emit(5, n.Pos.X, n.Pos.Y, 0, 0, 5); return true; }
            case NodeKind.Crate:
                n.Hp -= t.Dmg; Emit(5, n.Pos.X, n.Pos.Y, 0, 0, 7);
                if (n.Hp <= 0)
                {
                    n.Alive = false; n.RespawnAt = Time + Cfg.Cycle;
                    GiveOrDrop(a, Scrap, rng.Range(1, 3)); if (rng.Chance(0.6f)) GiveOrDrop(a, Sulfur, rng.Range(1, 2)); if (rng.Chance(0.3f)) GiveOrDrop(a, Ore, rng.Range(1, 2));
                    AddXp(a, 10);
                }
                return true;
        }
        if (y.hi <= 0) { if (n.Kind == NodeKind.Rock && t.Item == Wood) { } return false; }       // ferramenta errada: nada acontece
        int amount = rng.Range(y.lo, y.hi);
        // picareta de ferro: 4/8/12 pedras [wiki]
        if (n.Kind == NodeKind.Rock && t.Item == PickIron) amount = 4 * rng.Range(1, 3);
        GiveOrDrop(a, item, amount); AddXp(a, xp);
        Emit(5, n.Pos.X, n.Pos.Y, 0, 0, (int)n.Kind); Gathered += amount; Rewards[a.Id] += 0.01f * amount;
        return true;
    }
    public long Gathered;

    void UseHammer(Agent a, ToolDef t)
    {
        // martelo: conserta a construção à frente gastando o material dela [chute]; também serve como arma
        a.AttackCd = t.Cd; Emit(2, a.Pos.X, a.Pos.Y, a.Aim);
        var tp = a.Pos + new Vector2(MathF.Cos(a.Aim), MathF.Sin(a.Aim)) * (a.Radius + 55); int c = CellOf(tp);
        if (c >= 0 && BldGrid[c] != 0)
        {
            var def = Defs.Blds[BldGrid[c]]; float max = def.Hp;
            if (bldHp[c] < max)
            {
                var mat = def.Name.Contains("madeira") ? Wood : def.Name.Contains("pedra") ? Stone : Iron;
                if (a.Stamina >= t.Stamina && RemoveItem(a, mat, 1)) { bldHp[c] = MathF.Min(max, bldHp[c] + max * 0.1f); a.Stamina -= t.Stamina; AddXp(a, 2); return; }
                Say(a, $"Consertar gasta 1 {Defs.Name(mat)} por golpe."); return;
            }
        }
        Melee(a, t);
    }
    void UseSledge(Agent a, ToolDef t)
    {
        a.AttackCd = t.Cd; Emit(2, a.Pos.X, a.Pos.Y, a.Aim); a.Stamina = MathF.Max(0, a.Stamina - t.Stamina);
        var tp = a.Pos + new Vector2(MathF.Cos(a.Aim), MathF.Sin(a.Aim)) * (a.Radius + 55); int c = CellOf(tp);
        if (c >= 0 && BldGrid[c] != 0) RemoveBuilding(c, a);
    }
    void RemoveBuilding(int c, Agent? giveTo)
    {
        if (giveTo != null && boxes.TryGetValue(c, out var box)) for (int k = 0; k < box.Length; k++) if (box[k] > 0) GiveOrDrop(giveTo, (Item)k, box[k]);
        BldGrid[c] = 0; bldHp[c] = 0; boxes.Remove(c); fuel.Remove(c); charge.Remove(c);
    }

    // ---------------------------------------------------------------- armas
    int ReloadTarget(Agent a, Gun g) => Math.Min(g.Mag - a.Mag[(int)g.Item], a.Count(g.Ammo));
    void ReloadSelected(Agent a)
    {
        if (SelectedItem(a) is not Item it || Defs.GunOf(it) is not Gun g || g.Mag <= 0 || a.ReloadLeft > 0) return;
        if (ReloadTarget(a, g) <= 0) { Say(a, a.Mag[(int)it] >= g.Mag ? "Carregador cheio." : "Sem munição."); return; }
        a.ReloadLeft = g.Reload; Emit(10, a.Pos.X, a.Pos.Y);
    }
    void FinishReload(Agent a)
    {
        if (SelectedItem(a) is not Item it || Defs.GunOf(it) is not Gun g) return;
        int t = ReloadTarget(a, g); if (t <= 0) return; RemoveItem(a, g.Ammo, t); a.Mag[(int)it] += t;
    }
    void Fire(Agent a, Gun g)
    {
        if (g.Mag > 0)
        {
            if (a.Mag[(int)g.Item] <= 0) { ReloadSelected(a); return; }
            a.Mag[(int)g.Item]--;
        }
        else { if (a.Stamina < 1 || !RemoveItem(a, g.Ammo, 1)) return; a.Stamina = MathF.Max(0, a.Stamina - g.Stamina); }
        a.AttackCd = 1f / g.Rate;
        var origin = a.Pos + new Vector2(MathF.Cos(a.Aim), MathF.Sin(a.Aim)) * (a.Radius + 8);
        for (int p = 0; p < g.Pellets; p++)
        {
            float ang = a.Aim + rng.Float(-g.SpreadDeg, g.SpreadDeg) * 0.017453f;
            Projs.Add(new Projectile { Id = nextId++, OwnerId = a.Id, OwnerTeam = a.Team, Kind = g.Item, Pos = origin, Dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang)), Speed = g.Speed, Dmg = g.Dmg, Left = g.Range });
        }
        Emit(1, origin.X, origin.Y, MathF.Cos(a.Aim), MathF.Sin(a.Aim), (int)g.Item);
    }
    void ThrowSpear(Agent a)
    {
        if (!RemoveItem(a, Spear, 1)) return; a.AttackCd = Defs.SpearCd;
        var origin = a.Pos + new Vector2(MathF.Cos(a.Aim), MathF.Sin(a.Aim)) * (a.Radius + 8);
        Projs.Add(new Projectile { Id = nextId++, OwnerId = a.Id, OwnerTeam = a.Team, Kind = Spear, Pos = origin, Dir = new Vector2(MathF.Cos(a.Aim), MathF.Sin(a.Aim)), Speed = Defs.SpearSpeed, Dmg = Defs.SpearDmg, Left = Defs.SpearRange });
        Emit(1, origin.X, origin.Y, MathF.Cos(a.Aim), MathF.Sin(a.Aim), (int)Spear);
    }
    void UseFlare(Agent a)
    {
        if (!RemoveItem(a, Flare, 1)) return; a.AttackCd = 0.5f;
        Flares.Add(new FlareZone { Pos = a.Pos + new Vector2(MathF.Cos(a.Aim), MathF.Sin(a.Aim)) * 80, Left = Cfg.FlareSeconds });
        Emit(6, a.Pos.X, a.Pos.Y); Say(a, "Flare aceso: efeito de sol por 5 s.");
    }

    // ---------------------------------------------------------------- construir / plantar
    Vector2 AimPoint(Agent a, in AgentInput inp)
    {
        float d = inp.AimDist > 0 ? MathF.Min(inp.AimDist, Cfg.ReachBuild) : 120f;
        return a.Pos + new Vector2(MathF.Cos(a.Aim), MathF.Sin(a.Aim)) * d;
    }
    void Plant(Agent a, Item seed, in AgentInput inp)
    {
        var p = AimPoint(a, inp);
        if (!NodeFree(p, 26) || CellOf(p) < 0 || BldGrid[CellOf(p)] != 0) { Say(a, "Sem espaço para plantar aqui."); a.AttackCd = 0.4f; return; }
        RemoveItem(a, seed, 1); a.AttackCd = 0.4f;
        var n = AddNode(seed == OrangeSeed ? NodeKind.OrangeBush : NodeKind.TomatoBush, p); n.Fruit = 0; n.GrowAt = Time + Cfg.PlantGrowSeconds; n.DieAt = Time + Cfg.PlantLifeDays * Cfg.Cycle;
        AddXp(a, 5); Say(a, $"Planta plantada: dá frutos todo dia e morre em {Cfg.PlantLifeDays:0} dias.");
    }
    void UpdatePlants()
    {
        foreach (var n in Nodes)
        {
            if (!n.Alive || n.DieAt < 0) continue;
            if (Time >= n.DieAt) { n.Alive = false; n.RespawnAt = float.MaxValue; continue; }
            if (n.GrowAt > 0 && Time >= n.GrowAt) { n.Fruit = 3; n.GrowAt = 0; }
        }
    }

    bool CellBlockedByBeing(int cell)
    {
        float rx = (cell % GW) * Cfg.Cell, ry = (cell / GW) * Cfg.Cell;
        bool Hit(Vector2 p, float r) { float cx = Math.Clamp(p.X, rx, rx + Cfg.Cell), cy = Math.Clamp(p.Y, ry, ry + Cfg.Cell); return Vector2.DistanceSquared(p, new Vector2(cx, cy)) < r * r; }
        foreach (var o in Agents) if (o.Alive && Hit(o.Pos, o.Radius)) return true;
        foreach (var g in Ghouls) if (g.Alive && Hit(g.Pos, Defs.Ghouls[(int)g.Kind].Radius)) return true;
        foreach (var n in NodesNear(CellCenter(cell), 160)) if (n.Alive && n.Solid && Hit(n.Pos, n.R)) return true;
        return false;
    }

    void Place(Agent a, Item item, in AgentInput inp)
    {
        a.AttackCd = 0.35f; var p = AimPoint(a, inp);
        if (item == HallBot || item == LapadaBot)
        {
            if (!NodeFree(p, 30)) { Say(a, "Sem espaço aqui."); return; }
            RemoveItem(a, item, 1); Robots.Add(new Robot { Id = nextId++, Team = a.Team, Hall = item == HallBot, Pos = p, Hp = 600 });
            Say(a, $"{Defs.Name(item)} ativado."); AddXp(a, 20); return;
        }
        var b = Defs.ItemToBld[(int)item]; int cell = CellOf(p); if (b == Bld.None || cell < 0) return;
        var def = Defs.Blds[(int)b];
        if (def.IsFloor) { if (FloorGrid[cell] != 0) { Say(a, "Já existe piso aqui."); return; } RemoveItem(a, item, 1); FloorGrid[cell] = (byte)b; AddXp(a, 2); return; }
        if (BldGrid[cell] != 0) { Say(a, "Esse lugar já está ocupado."); return; }
        if (def.SolidAgent || def.SolidMob) { if (CellBlockedByBeing(cell)) { Say(a, "Tem algo no caminho."); return; } }
        RemoveItem(a, item, 1); BldGrid[cell] = (byte)b; bldHp[cell] = def.Hp;
        if (b == Bld.Chest || b == Bld.Fridge) boxes[cell] = new int[Defs.ItemCount];
        if (b == Bld.Campfire || b == Bld.Abrasador) fuel[cell] = 40f;
        Rewards[a.Id] += 0.1f; AddXp(a, 8);
    }

    void UpdateDrops(float dt) { for (int i = Drops.Count - 1; i >= 0; i--) if (Time > Drops[i].DieAt) Drops.RemoveAt(i); for (int i = Carcasses.Count - 1; i >= 0; i--) if (Time > Carcasses[i].DecayAt) Carcasses.RemoveAt(i); }
}
