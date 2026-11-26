using System.Diagnostics;
using System.Numerics;
using SimBox;
using static SimBox.Item;

static class T
{
    static int fails, total;
    static void Check(bool ok, string name) { total++; if (!ok) fails++; Console.WriteLine($"{(ok ? "OK   " : "FALHA")} {name}"); }
    static bool Near(float a, float b, float tol) => MathF.Abs(a - b) <= tol;

    static SimConfig Cfg(Action<SimConfig>? f = null) { var c = new SimConfig { CraftTimeScale = 0.05f, Animals = 0, Trees = 0, Rocks = 0, IronNodes = 0, SulfurNodes = 0, Crates = 0, Bushes = 0 }; f?.Invoke(c); return c; }
    static World Mk(Action<SimConfig>? f = null, int agents = 1) { var w = new World(Cfg(f), agents, 1, 0); Place(w.Agents[0], 3000, 3000); return w; }
    static void Place(Agent a, float x, float y) { a.Pos = new Vector2(x, y); }
    static AgentInput I(float mx = 0, float my = 0, float aim = 0, bool atk = false, bool sprint = false, Cmd cmd = Cmd.None, int arg = 0, float dist = 0) => new() { Mx = mx, My = my, Aim = aim, Attack = atk, Sprint = sprint, Cmd = cmd, Arg = arg, AimDist = dist };
    static void Run(World w, float sec, AgentInput inp, bool keep = false)
    {
        int n = (int)(sec * w.Cfg.Tps); var arr = new AgentInput[w.Agents.Length]; arr[0] = inp;
        for (int i = 0; i < n; i++) { if (keep) Keep(w); w.Step(arr); }
    }
    static void One(World w, AgentInput inp) { var arr = new AgentInput[w.Agents.Length]; arr[0] = inp; w.Step(arr); }
    static void Keep(World w) { foreach (var a in w.Agents) { a.Hp = a.Hunger = a.Cold = a.Stamina = 255; a.Rad = 0; } }
    static void Give(Agent a, Item it, int n, World w) => w.AddItem(a, it, n);
    static int SlotOf(Agent a, Item it) { for (int i = 0; i < a.Slots.Length; i++) if (a.Slots[i].N > 0 && a.Slots[i].Item == it) return i; return -1; }
    static void Select(World w, Item it) { int s = SlotOf(w.Agents[0], it); One(w, I(cmd: Cmd.Select, arg: s)); }
    static void Craft(World w, Item o, Bld st = Bld.None) { int r = Array.FindIndex(Defs.Recipes, x => x.Out == o && (st == Bld.None || x.Station == st)); One(w, I(cmd: Cmd.Craft, arg: r)); Run(w, 3, I(), true); }
    static Ghoul G(World w, GhoulKind k, Vector2 p) => w.DebugGhoul(k, p);

    static int Main(string[] args)
    {
        if (args.Contains("--bench")) return Bench();
        // ---------------- catálogo
        Check(Defs.ItemNames.Length == Defs.ItemCount, $"nomes PT == ItemCount ({Defs.ItemCount})");
        Check(Defs.Blds.Length == Enum.GetValues<Bld>().Length, "tabela de construções == enum Bld");
        Check(Defs.Placeables.All(p => Defs.Recipes.Any(r => r.Out == p)), "todo colocável tem receita");
        Check(Defs.Recipes.All(r => r.In.Length > 0 && r.OutN > 0 && r.Secs > 0), $"{Defs.Recipes.Length} receitas bem formadas");
        Check(Defs.Recipes.All(r => r.Prereq is null || Defs.Recipes.Any(x => x.Out == r.Prereq)), "pré-requisitos de pesquisa existem");
        Check(Defs.Guns.All(g => Defs.Recipes.Any(r => r.Out == g.Item)), "toda arma tem receita");
        // [wiki] tabela dos ghouls
        var gd = Defs.Ghouls;
        Check(gd[0].Hp == 160 && gd[1].Hp == 120 && gd[2].Hp == 80 && gd[3].Hp == 160 && gd[4].Hp == 800, "ghouls: vida 160/120/80/160/800 [wiki]");
        Check(gd[0].DmgDay == 8 && gd[0].DmgNight == 20 && gd[1].DmgNight == 7 && gd[2].DmgNight == 20 && gd[3].DmgNight == 15 && gd[4].DmgNight == 50, "ghouls: dano dia/noite [wiki]");
        Check(gd[1].SpeedNight == 280 / 10 * 10 && gd[0].SpeedDay == 120 && gd[4].SpeedNight == 230, "ghouls: velocidade (tabela ×10 u/s)");
        Check(Defs.XpFor(1) == 450 && Defs.XpFor(2) == 950 && Defs.XpFor(3) == 1450 && Defs.LevelFor(1449) == 2 && Defs.LevelFor(1450) == 3, "XP por nível 450/950/1450 [wiki]");
        Check(Defs.Guns.First(g => g.Item == AK47).Rate == 5.1f && Defs.Guns.First(g => g.Item == Sniper).Dmg == 90 && Defs.Guns.First(g => g.Item == MP5).Mag == 30, "armas: AK 5.1/s, sniper 90, MP5 carregador 30 [wiki]");

        // ---------------- movimento e colisão
        var w = Mk(); var a = w.Agents[0]; float x0 = a.Pos.X;
        Run(w, 1f, I(mx: 1), true);
        Check(Near(a.Pos.X - x0, 260, 12), $"andar: ~260 u/s (andou {a.Pos.X - x0:0})");
        x0 = a.Pos.X; a.Stamina = 255; Run(w, 1f, I(mx: 1, sprint: true));
        Check(Near(a.Pos.X - x0, 400, 14) && Near(a.Stamina, 255 - 28, 3), $"correr: ~400 u/s e stamina -28/s (andou {a.Pos.X - x0:0}, stamina {a.Stamina:0})");
        float st = a.Stamina; Run(w, 1f, I());
        Check(Near(a.Stamina, MathF.Min(255, st + 14), 2), "stamina regenera 14/s parado");
        w = Mk(); a = w.Agents[0]; w.DebugNode(NodeKind.Tree, new Vector2(3150, 3000)); Run(w, 2f, I(mx: 1), true);
        Check(a.Pos.X < 3150 - 30 - 30 + 1 && a.Pos.X > 3150 - 30 - 30 - 20, $"árvore bloqueia (parou em x={a.Pos.X:0})");
        w = Mk(); a = w.Agents[0]; w.DebugNode(NodeKind.Tree, new Vector2(3150, 3000)); Run(w, 2f, I(mx: 1, my: 0.6f), true);
        Check(a.Pos.Y > 3000 + 20 && a.Pos.X > 3100, "desliza ao redor do obstáculo");
        w = Mk(); a = w.Agents[0]; w.DebugPlace(Bld.WallWood, 31, 30); Run(w, 2f, I(mx: 1), true);
        Check(Near(a.Pos.X, 3100 - 30, 2), $"muro bloqueia o agente (x={a.Pos.X:0})");
        w = Mk(); a = w.Agents[0]; w.DebugPlace(Bld.GateWood, 31, 30); Run(w, 2f, I(mx: 1), true);
        Check(a.Pos.X > 3200, "portão deixa o agente passar");
        w = Mk(); w.Time = 100; var gh = G(w, GhoulKind.Normal, new Vector2(3300, 3050)); w.DebugPlace(Bld.GateWood, 32, 30); w.DebugPlace(Bld.GateWood, 32, 29); w.DebugPlace(Bld.GateWood, 32, 31);
        Run(w, 3f, I(), true);
        Check(gh.Pos.X > 3195, "portão bloqueia o ghoul (ele para do outro lado)");

        // ---------------- coleta, XP, pesquisa
        w = Mk(); a = w.Agents[0]; w.DebugNode(NodeKind.Tree, new Vector2(3085, 3000)); One(w, I(cmd: Cmd.Select, arg: 5));
        Run(w, 1.01f, I(atk: true, aim: 0), true);
        Check(a.Count(Wood) >= 2 && a.Count(Wood) <= 4, $"mão: 1-2 madeira por golpe, 2 golpes/s (madeira={a.Count(Wood)})");
        float xp0 = a.Xp; Check(a.Xp >= 3, "XP de coleta (árvore com a mão: 3)");
        w = Mk(); a = w.Agents[0]; Give(a, Wood, 10, w); Craft(w, AxeBasic);
        Check(a.Has(AxeBasic) && a.Count(Wood) == 0 && a.Count(Stone) == 0, "machado básico: 10 madeira + 2 pedra [wiki]");
        w = Mk(); a = w.Agents[0]; Give(a, AxeBasic, 1, w); w.DebugNode(NodeKind.Tree, new Vector2(3085, 3000)); Select(w, AxeBasic);
        int before = a.Count(Wood); Run(w, 0.55f, I(atk: true), true); int d = a.Count(Wood) - before;
        Check(d >= 2 && d <= 8, $"machado básico: 2-4 madeira por golpe ({d} em 2 golpes)");
        w = Mk(); a = w.Agents[0]; w.AddXp(a, 450);
        Check(a.Level == 1 && a.SkillPts == 1, "450 XP = nível 1 com 1 ponto de habilidade [wiki]");
        Check(w.ResearchCheck(a, PickIron) != null, "picareta de ferro exige nível 6 e a picareta de pedra");
        One(w, I(cmd: Cmd.Research, arg: (int)PickBasic)); Check(a.Unlocked[(int)PickBasic] && a.SkillPts == 0, "pesquisar a picareta gasta 1 ponto");
        Give(a, Wood, 150, w); Give(a, Stone, 30, w); w.DebugPlace(Bld.Workbench, 30, 30);
        a.Pos = new Vector2(3000, 3000);
        Craft(w, PickBasic, Bld.Workbench); Check(a.Has(PickBasic), "picareta fabricada na bancada depois de pesquisada");
        w = Mk(); a = w.Agents[0]; Give(a, Wood, 150, w); Give(a, Stone, 30, w); w.DebugPlace(Bld.Workbench, 30, 30);
        Craft(w, PickBasic, Bld.Workbench); Check(!a.Has(PickBasic), "sem pesquisa não fabrica picareta");
        w = Mk(); a = w.Agents[0]; Give(a, Wood, 150, w); Give(a, Stone, 30, w);
        Craft(w, PickBasic, Bld.Workbench); Check(!a.Has(PickBasic), "sem estar perto da bancada, não fabrica");

        // ---------------- ferramentas e minérios
        w = Mk(); a = w.Agents[0]; w.DebugNode(NodeKind.IronOre, new Vector2(3085, 3000)); Select(w, Stone);
        Run(w, 0.6f, I(atk: true), true); Check(a.Count(Ore) == 0, "minério não sai com a mão");
        Give(a, PickBasic, 1, w); Select(w, PickBasic); Run(w, 0.7f, I(atk: true), true); Check(a.Count(Ore) >= 1, "picareta básica extrai minério de ferro");
        w = Mk(); a = w.Agents[0]; w.DebugNode(NodeKind.SulfurOre, new Vector2(3085, 3000)); Give(a, PickBasic, 1, w); Select(w, PickBasic);
        Run(w, 0.7f, I(atk: true), true); Check(a.Count(Sulfur) == 0, "enxofre não sai com a picareta básica");
        Give(a, PickIron, 1, w); Select(w, PickIron); Run(w, 0.7f, I(atk: true), true); Check(a.Count(Sulfur) >= 1, "picareta de ferro extrai enxofre");

        // ---------------- sobrevivência [wiki]
        w = Mk(); a = w.Agents[0]; Run(w, 10f, I()); Check(Near(a.Hunger, 255 - 12, 1), $"fome: -1.2/s (={a.Hunger:0.0})");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); Run(w, 10f, I());
        Check(Near(a.Cold, 255 - 35, 2), $"frio: -3.5/s à noite sem fogueira (={a.Cold:0.0})");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); a.Cold = 100; w.DebugPlace(Bld.Campfire, 30, 30); Run(w, 10f, I());
        Check(Near(a.Cold, 150, 2), $"fogueira acesa: +5/s (={a.Cold:0.0})");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); w.DebugPlace(Bld.Campfire, 30, 30); Give(a, Wood, 50, w);
        Run(w, 1f, I()); var cell = 30 * w.GW + 30; w.DebugPlace(Bld.Campfire, 30, 30);
        Check(w.IsLit(cell), "fogueira nasce acesa");
        w = Mk(); a = w.Agents[0]; Give(a, Wood, 30, w); Give(a, Stone, 5, w); Craft(w, Campfire); Select(w, Campfire);
        One(w, I(atk: true, aim: 0, dist: 150)); int fc = w.FindStation(a.Pos, Bld.Campfire, 400);
        Check(fc >= 0 && w.IsLit(fc), "fogueira colocada na mira (mouse)");
        Run(w, 41f, I(), true); Check(!w.IsLit(fc), "fogueira apaga quando acaba a madeira");
        Give(a, Wood, 3, w); One(w, I(cmd: Cmd.Interact)); Check(w.IsLit(fc), "interagir com madeira reacende o fogo");
        w = Mk(); a = w.Agents[0]; a.Hunger = 0; Run(w, 5f, I()); Check(a.Hp < 255 - 15, "fome 0 causa dano de vida");
        w = Mk(); a = w.Agents[0]; Give(a, Headscarf, 1, w); w.DebugSetTime(w.Cfg.DayLen + 5); Run(w, 10f, I());
        Check(Near(a.Cold, 255 - 30, 2), "lenço: frio cai 3/s [wiki]");

        // ---------------- ciclo e ondas de ghouls
        w = Mk(c => { c.DayLen = 20; c.NightLen = 20; }, 2); var kinds = new List<HashSet<GhoulKind>>();
        for (int n = 1; n <= 6; n++)
        {
            while (w.TimeInCycle < w.Cfg.DayLen + 1 || (int)(w.Time / w.Cfg.Cycle) + 1 != n) { Keep(w); w.Step(new AgentInput[2]); }
            kinds.Add(w.Ghouls.Select(g => g.Kind).ToHashSet());
        }
        Check(kinds[0].Count == 0, "noite 1: nenhum ghoul");
        Check(kinds[1].SetEquals(new[] { GhoulKind.Normal }), "noite 2: só ghouls normais");
        Check(kinds[2].Contains(GhoulKind.Fast), "noite 3: + veloz");
        Check(kinds[3].Contains(GhoulKind.Explosive), "noite 4: + explosivo");
        Check(kinds[4].Contains(GhoulKind.Radioactive), "noite 5: + radioativo");
        Check(kinds[5].Contains(GhoulKind.Armored), "noite 6: + blindado");

        // ---------------- combate corpo a corpo
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); gh = G(w, GhoulKind.Radioactive, new Vector2(3070, 3000)); gh.Hostile = false;
        Give(a, AxeBasic, 1, w); Select(w, AxeBasic); One(w, I(atk: true, aim: 0)); Check(Near(gh.Hp, 160 - 7, 0.01f), "machado básico dá 7 de dano [wiki]");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); gh = G(w, GhoulKind.Radioactive, new Vector2(3070, 3000)); gh.Hostile = false;
        One(w, I(atk: true, aim: MathF.PI)); Check(gh.Hp == 160, "golpe para o lado oposto não acerta (arco)");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); gh = G(w, GhoulKind.Normal, new Vector2(3068, 3000));
        Run(w, 1.0f, I()); Check(a.Hp < 255 && a.Hp >= 255 - 20 * 2 - 1, $"ghoul normal bate 20 à noite (hp={a.Hp:0})");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(10f); gh = G(w, GhoulKind.Normal, new Vector2(3068, 3000)); Run(w, 0.3f, I());
        Check(a.Hp >= 255 - 8 - 0.5f && a.Hp < 255, $"de dia o dano é 8 (hp={a.Hp:0})");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); Give(a, ArmorBear, 1, w); gh = G(w, GhoulKind.Normal, new Vector2(3068, 3000)); Run(w, 0.3f, I());
        Check(Near(a.Hp, 255 - 20 * 0.75f, 0.5f), "armadura de urso reduz 25% do dano corpo a corpo");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); Give(a, Meat, 1, w); a.Hunger = 100; Select(w, Meat);
        a.Hp = 100; One(w, I(atk: true)); Check(Near(a.Hunger, 130, 1) && a.Hp < 100, "carne crua: +30 fome, -10 vida [wiki]");
        w = Mk(); a = w.Agents[0]; Give(a, CookedMeat, 1, w); a.Hunger = 100; Select(w, CookedMeat); One(w, I(atk: true)); Check(Near(a.Hunger, 227, 1), "carne assada: +127 fome [wiki]");

        // ---------------- armas à distância
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); Give(a, AK47, 1, w); Give(a, BulletHigh, 60, w); Select(w, AK47);
        One(w, I(atk: true)); Check(w.Projs.Count == 0, "AK sem munição carregada não atira");
        One(w, I(cmd: Cmd.Reload)); Run(w, 2.5f, I(), true); Check(a.Mag[(int)AK47] == 30 && a.Count(BulletHigh) == 30, "recarregar leva 2.4 s e enche 30 [wiki: carregador 30]");
        gh = G(w, GhoulKind.Armored, new Vector2(3500, 3000)); gh.Hostile = false; int shots0 = a.Mag[(int)AK47];
        Run(w, 1f, I(atk: true, aim: 0)); int fired = shots0 - a.Mag[(int)AK47];
        Check(fired >= 4 && fired <= 6, $"AK: ~5.1 tiros/s ({fired} em 1 s)");
        Check(gh.Hp < 800 - 30, $"balas acertam o alvo a 500 u (blindado hp={gh.Hp:0})");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); Give(a, Sniper, 1, w); Give(a, BulletHigh, 20, w); Select(w, Sniper);
        One(w, I(cmd: Cmd.Reload)); Run(w, 2.6f, I(), true); gh = G(w, GhoulKind.Normal, new Vector2(4600, 3000)); gh.Hostile = false;
        Run(w, 1.2f, I(atk: true), true); Check(Near(gh.Hp, 160 - 90, 1), $"sniper acerta a 1600 u com 90 de dano (hp={gh.Hp:0})");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); Give(a, Shotgun, 1, w); Give(a, Shell, 20, w); Select(w, Shotgun);
        One(w, I(cmd: Cmd.Reload)); Run(w, 2.6f, I(), true); gh = G(w, GhoulKind.Armored, new Vector2(3180, 3000)); gh.Hostile = false;
        One(w, I(atk: true)); Run(w, 0.5f, I(), true);
        Check(gh.Hp <= 800 - 21 * 2, $"escopeta: vários chumbos acertam de perto (dano {800 - gh.Hp:0})");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); Give(a, Pistol9, 1, w); Give(a, BulletLow, 20, w); Select(w, Pistol9);
        One(w, I(cmd: Cmd.Reload)); Run(w, 1.8f, I(), true); w.DebugPlace(Bld.WallWood, 33, 30); gh = G(w, GhoulKind.Armored, new Vector2(3500, 3000)); gh.Hostile = false;
        Run(w, 1f, I(atk: true), true); Check(gh.Hp == 800, "muro de madeira bloqueia balas");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); Give(a, Pistol9, 1, w); Give(a, BulletLow, 20, w); Select(w, Pistol9);
        One(w, I(cmd: Cmd.Reload)); Run(w, 1.8f, I(), true); w.DebugPlace(Bld.ThinWallWood, 33, 30); gh = G(w, GhoulKind.Armored, new Vector2(3500, 3000)); gh.Hostile = false;
        Run(w, 1f, I(atk: true), true); Check(gh.Hp < 800, "muro fino deixa atirar por cima [wiki: half walls]");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); Give(a, Spear, 1, w); Select(w, Spear); gh = G(w, GhoulKind.Normal, new Vector2(3400, 3000)); gh.Hostile = false;
        One(w, I(atk: true)); Run(w, 0.5f, I(), true); Check(Near(gh.Hp, 160 - 80, 1) && w.Drops.Any(x => x.Item == Spear), "lança arremessada: 80 de dano e cai no chão");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); Give(a, Bow, 1, w); Give(a, Arrow, 10, w); Select(w, Bow); gh = G(w, GhoulKind.Normal, new Vector2(3500, 3000)); gh.Hostile = false;
        One(w, I(atk: true)); Run(w, 0.8f, I(), true); Check(Near(gh.Hp, 160 - 40, 1) && a.Count(Arrow) == 9, "arco: 40 de dano, gasta 1 flecha");

        // ---------------- ghouls vs construções, explosão, sol, flare
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); int wc = w.DebugPlace(Bld.WallWood, 31, 30); w.DebugPlace(Bld.WallWood, 31, 29); w.DebugPlace(Bld.WallWood, 31, 31);
        G(w, GhoulKind.Normal, new Vector2(3300, 3050)); Run(w, 12f, I(), true);
        Check(w.BuildingHp(wc) < 3000 - 300 || w.BldGrid[wc] == 0, $"ghoul quebra o muro no caminho (hp={w.BuildingHp(wc):0})");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); int wb = w.DebugPlace(Bld.Workbench, 32, 30); Place(a, 3000, 3000);
        var eg = G(w, GhoulKind.Explosive, new Vector2(3100, 3000)); eg.Hostile = false; Give(a, AxeIron, 1, w);
        w.DamageGhoul(eg, 9999, a); Check(a.Hp <= 255 - 120 && a.Hp >= 255 - 135, $"ghoul explosivo explode: ~127 de dano (hp={a.Hp:0})");
        Check(w.BldGrid[wb] == 0, "explosão destrói a bancada (250 hp)");
        w = Mk(); w.DebugSetTime(w.Cfg.DayLen + 5); gh = G(w, GhoulKind.Normal, new Vector2(3500, 3500)); gh.Hostile = false; w.DebugSetTime(w.Cfg.Cycle + 1); Run(w, 45f, I(), true);
        Check(!gh.Alive, "ghoul morre depois que amanhece (~40 s)");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); gh = G(w, GhoulKind.Normal, new Vector2(3500, 3500)); gh.Hostile = false; Give(a, Flare, 1, w); a.Pos = new Vector2(3500, 3450);
        Select(w, Flare); One(w, I(atk: true)); Run(w, 3f, I(), true); Check(gh.Hp < 160 - 10, $"flare dá efeito de sol (hp={gh.Hp:0})");
        Run(w, 3f, I(), true); Check(w.Flares.Count == 0, "flare dura só 5 s");
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); eg = G(w, GhoulKind.Radioactive, new Vector2(3100, 3000)); eg.Hostile = false; Run(w, 3f, I());
        Check(a.Rad > 60, $"ghoul radioativo aumenta a radiação (rad={a.Rad:0})");

        // ---------------- fogo, fornalha, estações
        w = Mk(c => c.CraftTimeScale = 0.02f); a = w.Agents[0]; w.DebugPlace(Bld.Smelter, 31, 30); Give(a, Scrap, 9, w); Give(a, Ore, 20, w); Give(a, Sulfur, 9, w); Give(a, Gasoline, 1, w);
        int ssm = Defs.Recipes.ToList().FindIndex(r => r.Out == Alloy);
        for (int i = 0; i < 4; i++) { One(w, I(cmd: Cmd.Craft, arg: ssm)); Run(w, 1f, I(), true); }
        Check(a.Count(Alloy) == 4 && a.Count(Gasoline) == 0, "1 gasolina = 4 alloys [user]");
        One(w, I(cmd: Cmd.Craft, arg: ssm)); Run(w, 1f, I(), true); Check(a.Count(Alloy) == 4, "5º alloy sem combustível: recusado");
        Give(a, Diesel, 1, w); One(w, I(cmd: Cmd.Craft, arg: ssm)); Run(w, 1f, I(), true); Check(a.Count(Alloy) == 5 && w.SmelterCharge(31 + 30 * w.GW) == 11, "1 diesel = 12 operações [user]");
        w = Mk(c => c.CraftTimeScale = 1f); a = w.Agents[0]; w.DebugPlace(Bld.Abrasador, 31, 30); w.DebugPlace(Bld.Smelter, 29, 30); Give(a, Ore, 4, w); Give(a, Iron, 1, w); Give(a, Gasoline, 1, w);
        One(w, I(cmd: Cmd.Craft, arg: Defs.Recipes.ToList().FindIndex(r => r.Out == Iron && r.Station == Bld.Abrasador))); Run(w, 20f, I(), true); Check(a.Count(Iron) == 1, "abrasador ainda não terminou em 20 s (30 s) — lento");
        Run(w, 11f, I(), true); Check(a.Count(Iron) == 2, "abrasador: 2 minério → 1 ferro em 30 s");
        One(w, I(cmd: Cmd.Craft, arg: Defs.Recipes.ToList().FindIndex(r => r.Out == Iron && r.Station == Bld.Smelter))); Run(w, 11f, I(), true);
        Check(a.Count(Iron) == 3 && a.Count(Ore) == 0, "smelter: 2 minério + 1 ferro → 2 ferro em 10 s (3x mais rápido)");
        w = Mk(c => c.CraftTimeScale = 0.02f); a = w.Agents[0]; Give(a, Meat, 1, w); w.DebugPlace(Bld.Campfire, 31, 30); w.DebugSetTime(10);
        One(w, I(cmd: Cmd.Craft, arg: Defs.Recipes.ToList().FindIndex(r => r.Out == CookedMeat))); Run(w, 1f, I(), true); Check(a.Has(CookedMeat), "carne assada na fogueira");
        w = Mk(c => c.CraftTimeScale = 0.02f); a = w.Agents[0]; Give(a, Wood, 5, w); Give(a, Nails, 1, w); w.AddXp(a, 50000);
        Give(a, Iron, 1, w); w.DebugPlace(Bld.Workbench, 31, 30); One(w, I(cmd: Cmd.Craft, arg: Defs.Recipes.ToList().FindIndex(r => r.Out == Nails))); Run(w, 1f, I(), true);
        Check(a.Count(Nails) == 86, "1 ferro → 85 pregos [user]");
        w = Mk(); a = w.Agents[0]; Give(a, Fat, 1, w); Give(a, Wood, 5, w); int fi = Defs.Recipes.ToList().FindIndex(r => r.Out == Flare);
        var wb2 = Mk(); wb2.Agents[0].IsHuman = false; Give(wb2.Agents[0], Fat, 3, wb2); Give(wb2.Agents[0], Wood, 10, wb2); One(wb2, I(cmd: Cmd.Craft, arg: fi)); Check(wb2.Agents[0].CraftLeft == 0, "bot NÃO fabrica flare [user]");
        Give(a, Fat, 2, w); Give(a, Wood, 10, w); One(w, I(cmd: Cmd.Craft, arg: fi)); Check(a.CraftLeft > 0, "humano fabrica flare");

        // ---------------- baú / geladeira / comida
        w = Mk(); a = w.Agents[0]; int ch = w.DebugPlace(Bld.Chest, 31, 30); Give(a, Wood, 5, w); Give(a, Meat, 3, w); Give(a, Orange, 2, w);
        One(w, I(cmd: Cmd.Store)); Check(w.BoxCount(ch, Wood) == 5 && w.BoxCount(ch, Meat) == 0 && a.Count(Meat) == 3, "baú guarda madeira e recusa comida [user]");
        w = Mk(); a = w.Agents[0]; int fr = w.DebugPlace(Bld.Fridge, 31, 30); Give(a, Wood, 5, w); Give(a, Meat, 3, w); Give(a, Orange, 40, w);
        One(w, I(cmd: Cmd.Store)); Check(w.BoxCount(fr, Meat) == 3 && w.BoxCount(fr, Wood) == 0, "geladeira guarda só comida");
        w.DebugSetTime(w.Cfg.DayLen * 0.5f); Give(a, Orange, 10, w); Run(w, 62f, I(), true);
        Check(a.Count(RottenOrange) > 0 && w.BoxCount(fr, Orange) == 40 && w.BoxCount(fr, Meat) == 3, "comida fora da geladeira apodrece; dentro não");
        One(w, I(cmd: Cmd.Take)); Check(a.Count(Meat) == 3, "pegar de volta da geladeira");
        w = Mk(); a = w.Agents[0]; int left = w.AddItem(a, Wood, 300);
        Check(left == 0 && a.Slots.Count(s => s.N > 0 && s.Item == Wood) == 2 && a.Slots.Any(s => s.N == 255), "pilha máxima 255 [wiki]");
        w = Mk(); a = w.Agents[0]; for (int i = 0; i < 12; i++) w.AddItem(a, (Item)i, 1); Check(a.Slots.Count(s => s.N > 0) == 8, "inventário limitado a 8 slots");

        // ---------------- plantas
        w = Mk(c => { c.DayLen = 20; c.NightLen = 20; c.PlantGrowSeconds = 5; }); w.Cfg.RotChance = 0; a = w.Agents[0]; Give(a, OrangeSeed, 1, w); Select(w, OrangeSeed);
        One(w, I(atk: true, aim: 0, dist: 100)); var bush = w.Nodes.FirstOrDefault(n => n.Kind == NodeKind.OrangeBush && n.DieAt > 0);
        Check(bush != null && bush.Fruit == 0, "semente plantada vira arbusto sem frutos");
        Run(w, 6f, I(), true); Check(bush!.Fruit == 3, "arbusto dá frutos depois de crescer");
        Run(w, 5 * 40 - 6 + 2, I(), true); Check(!bush.Alive, "planta morre 5 dias depois de plantada [user]");

        // ---------------- robôs
        w = Mk(); a = w.Agents[0]; w.DebugSetTime(w.Cfg.DayLen + 5); Give(a, HallBot, 1, w); Select(w, HallBot); One(w, I(atk: true, aim: MathF.PI, dist: 120));
        gh = G(w, GhoulKind.Armored, new Vector2(2600, 3000)); gh.Hostile = false; Run(w, 6f, I(), true);
        Check(w.Robots.Count == 1 && gh.Hp < 800 - 100 && a.Hp == 255, $"Hall 9000 atira nos ghouls e não fere o dono (ghoul hp={gh.Hp:0})");
        w = Mk(); a = w.Agents[0]; wc = w.DebugPlace(Bld.WallWood, 33, 30); w.DamageBuilding(wc, 2000); float h0 = w.BuildingHp(wc); Give(a, LapadaBot, 1, w); Select(w, LapadaBot);
        One(w, I(atk: true, aim: 0, dist: 150)); Run(w, 10f, I(), true); Check(w.BuildingHp(wc) > h0 + 100, $"Lapada bot conserta construções ({h0:0} → {w.BuildingHp(wc):0})");

        // ---------------- morte e renascimento
        w = Mk(); a = w.Agents[0]; w.AddXp(a, Defs.XpFor(10)); Give(a, Wood, 30, w); int lv = a.Level; a.Hp = 1; w.HurtAgent(a, 50, false);
        Check(!a.Alive && w.Drops.Any(d2 => d2.Item == Wood), "morrer solta o inventário no chão");
        One(w, I(cmd: Cmd.Respawn)); Check(a.Alive && a.Level == lv / 2 && a.Count(Stone) == 2, $"renascer: metade do nível ({lv}→{a.Level}) [wiki]");

        // ---------------- times, recompensas acumuladas e ferramentas (regras do treino)
        w = Mk(null, 2); w.AccumulateRewards = true; a = w.Agents[0];
        Run(w, 2f, I(), true); float racc = w.Rewards[0];
        Check(racc > 0.02f, $"AccumulateRewards: reward acumula entre ticks ({racc:0.000})");
        w.Reset(1); Check(w.Rewards[0] == 0 && w.Rewards[1] == 0, "Reset() zera o reward (nova geração)");
        var wp = new World(Cfg(c => c.TeamSpawns = true), 4, 1, -1); for (int i = 0; i < 4; i++) wp.Agents[i].Team = i % 2; wp.Reset(1);
        Check(wp.Agents[0].Pos.X < wp.W / 2 && wp.Agents[1].Pos.X > wp.W / 2 && Near(wp.Agents[0].Pos.X, wp.W * 0.25f, 400) && Near(wp.Agents[1].Pos.X, wp.W * 0.75f, 400), "TeamSpawns: time 0 à esquerda, time 1 à direita");

        AgentInput Hit() => I(atk: true, aim: 0);
        World Duel(int teamB, float hpB) { var d = Mk(null, 2); d.AccumulateRewards = true; d.Agents[0].Team = 0; d.Agents[1].Team = teamB; Place(d.Agents[1], 3050, 3000); d.Agents[1].Hp = hpB; return d; }
        w = Duel(0, 255); var vb = w.Agents[1]; One(w, Hit());
        Check(vb.Hp < 255 && w.Rewards[0] < 0 && w.Agents[0].FriendDmg > 0, $"fogo amigo: penalidade (reward {w.Rewards[0]:0.00}, dano {w.Agents[0].FriendDmg:0})");
        w = Duel(0, 1); One(w, Hit());
        Check(!w.Agents[1].Alive && w.Agents[0].FriendKills == 1 && w.Rewards[0] <= Rw.KillFriend + 1, $"matar amigo: penalidade severa (reward {w.Rewards[0]:0.0})");
        w = Duel(1, 255); One(w, Hit());
        Check(w.Agents[1].Hp < 255 && w.Rewards[0] > 0 && w.Agents[0].EnemyDmg > 0, $"fogo inimigo: bônus (reward {w.Rewards[0]:0.00})");
        w = Duel(1, 1); One(w, Hit());
        Check(!w.Agents[1].Alive && w.Agents[0].EnemyKills == 1 && w.Rewards[0] >= Rw.KillEnemy - 1, $"matar inimigo: bônus severo (reward {w.Rewards[0]:0.0})");
        Check(Defs.TechMult(Wood) == 1f && Defs.TechMult(AxeBasic) < Defs.TechMult(AxeIron) && Defs.TechMult(AxeIron) < Defs.TechMult(Pistol9) && Defs.TechMult(Pistol9) < Defs.TechMult(AK47), "bônus de combate cresce com a tecnologia da arma");
        w = Duel(1, 255); Give(w.Agents[0], Spear, 1, w); Select(w, Spear); One(w, I(atk: true, aim: 0, dist: 300)); Run(w, 1f, I());
        Check(w.Agents[1].Hp < 255 && w.Rewards[0] > 0, $"projétil (lança) acerta agente rival ({w.Agents[1].Hp:0} hp)");
        w = Duel(0, 255); Give(w.Agents[0], Spear, 1, w); Select(w, Spear); One(w, I(atk: true, aim: 0, dist: 300)); Run(w, 1f, I());
        Check(w.Agents[1].Hp < 255 && w.Rewards[0] < 0, "projétil em colega: fogo amigo penalizado");

        w = Duel(0, 255); a = w.Agents[0]; var mate = w.Agents[1];
        Give(a, AxeBasic, 1, w); w.ToolObtained(a, AxeBasic, true);
        Check(Near(w.Rewards[0], Defs.ToolBonus(AxeBasic), 0.01f) && a.ToolsMade == 1, "fabricar ferramenta: bônus grande");
        w.ToolObtained(a, AxeBasic, true); Check(Near(w.Rewards[0], Defs.ToolBonus(AxeBasic), 0.01f), "mesma ferramenta: bônus só 1x por geração");
        Give(a, AxeIron, 1, w); w.ToolObtained(a, AxeIron, true);
        Check(!a.Has(AxeBasic) && mate.Has(AxeBasic) && a.Donations == 1, "ferramenta melhor: a anterior é doada ao colega pior equipado");
        Check(Near(w.Rewards[0], Defs.ToolBonus(AxeBasic) + Defs.ToolBonus(AxeIron) + Defs.ToolBonus(AxeBasic) * Rw.DonateFactor, 0.01f), $"doador ganha bônus (reward {w.Rewards[0]:0.0})");
        w = Duel(1, 255); a = w.Agents[0]; Give(a, AxeBasic, 1, w); Give(a, AxeIron, 1, w); w.ToolObtained(a, AxeIron, true);
        Check(!a.Has(AxeBasic) && !w.Agents[1].Has(AxeBasic) && w.Drops.Any(d2 => d2.Item == AxeBasic), "sem colega por perto (outro time): joga a anterior fora");
        w = Duel(0, 255); a = w.Agents[0]; Give(w.Agents[1], AxeIron, 1, w); Give(a, AxeBasic, 1, w); Give(a, AxeIron, 1, w); w.ToolObtained(a, AxeIron, true);
        Check(!a.Has(AxeBasic) && !w.Agents[1].Has(AxeBasic) && Near(w.Rewards[0], Defs.ToolBonus(AxeIron), 0.01f), "colega já tem igual/melhor: não doa (joga fora) e não há bônus de doação");

        // ---------------- muro entre os times (cai nos 2 últimos dias)
        w = Mk(c => { c.TeamWall = true; c.WallOpenTime = 6f; }); a = w.Agents[0]; Place(a, 2700, 3000);
        Run(w, 3f, I(mx: 1), true);
        Check(w.WallActive && Near(a.Pos.X, 3000 - 50 - a.Radius, 2), $"muro: agente não atravessa (parou em x={a.Pos.X:0})");
        Run(w, 4f, I(), true); Check(!w.WallActive, "muro: some depois de WallOpenTime");
        Run(w, 2f, I(mx: 1), true); Check(a.Pos.X > 3100, $"muro: depois que cai, dá para passar (x={a.Pos.X:0})");
        w = Mk(c => { c.TeamWall = true; c.WallOpenTime = 600f; }, 2); Place(w.Agents[0], 2900, 3000); Place(w.Agents[1], 3110, 3000);
        Give(w.Agents[0], Spear, 1, w); Select(w, Spear); One(w, I(atk: true, aim: 0, dist: 300)); Run(w, 1f, I());
        Check(w.Agents[1].Hp == 255, "muro: para a lança (ninguém é atingido do outro lado)");
        w = Mk(c => { c.TeamWall = false; }, 2); Place(w.Agents[0], 2900, 3000); Place(w.Agents[1], 3110, 3000);
        Give(w.Agents[0], Spear, 1, w); Select(w, Spear); One(w, I(atk: true, aim: 0, dist: 300)); Run(w, 1f, I());
        Check(w.Agents[1].Hp < 255, "sem muro a mesma lança acerta (controle do teste)");
        Check(!new World(Cfg(), 1, 1, -1).WallActive, "WallActive é falso por padrão (jogo normal não tem muro)");

        // ---------------- bônus por fabricar estações de trabalho e por comer sem desperdício
        w = Mk(); w.AccumulateRewards = true; a = w.Agents[0];
        Give(a, Wood, 40, w); Give(a, Stone, 20, w); float r0 = w.Rewards[0]; Craft(w, Workbench);
        Check(a.Has(Workbench) && w.Rewards[0] - r0 >= Defs.StationBonus(Workbench) && a.StationsMade == 1, $"fabricar estação: bônus grande (+{w.Rewards[0] - r0:0.0})");
        Check(Defs.StationBonus(Campfire) < Defs.StationBonus(Workbench) && Defs.StationBonus(Workbench) < Defs.StationBonus(ResearchTable) && Defs.StationBonus(ResearchTable) < Defs.StationBonus(Smelter), "estações melhores valem mais");
        Give(a, Wood, 40, w); Give(a, Stone, 20, w); r0 = w.Rewards[0]; Craft(w, Workbench);
        Check(w.Rewards[0] - r0 < 1f && a.StationsMade == 1, $"mesma estação: bônus só 1x por geração (+{w.Rewards[0] - r0:0.00})");
        w = Mk(); w.AccumulateRewards = true; a = w.Agents[0]; a.Hunger = 100; Give(a, Orange, 3, w); r0 = w.Rewards[0];
        One(w, I(cmd: Cmd.Eat, arg: (int)Orange));
        Check(a.GoodMeals == 1 && w.Rewards[0] - r0 > Rw.EatPerPoint * 40 * 0.9f, $"comer sem desperdício: bônus (+{w.Rewards[0] - r0:0.00})");
        Run(w, 1f, I()); a.Hunger = 220; r0 = w.Rewards[0]; One(w, I(cmd: Cmd.Eat, arg: (int)Orange));
        Check(a.WastedMeals == 1 && w.Rewards[0] - r0 < 0.5f, $"comer desperdiçando: sem bônus (+{w.Rewards[0] - r0:0.00})");
        w = Mk(); w.AccumulateRewards = true; a = w.Agents[0]; a.Hunger = 100; Give(a, Meat, 1, w); r0 = w.Rewards[0]; One(w, I(cmd: Cmd.Eat, arg: (int)Meat));
        Check(a.GoodMeals == 0 && w.Rewards[0] - r0 < 0.5f, "carne crua (fere) não paga o bônus");
        w = Mk(); w.AccumulateRewards = true; a = w.Agents[0]; a.Hp = 100; Give(a, Bandage, 1, w); r0 = w.Rewards[0]; One(w, I(cmd: Cmd.Eat, arg: (int)Bandage));
        Check(a.GoodMeals == 1 && w.Rewards[0] - r0 > Rw.EatPerPoint * 60 * 0.9f, "curar sem passar do máximo: bônus");

        // ---------------- estresse
        var cfgS = Cfg(c => { c.DayLen = 30; c.NightLen = 30; c.Trees = 300; c.Rocks = 100; c.IronNodes = 40; c.SulfurNodes = 20; c.Crates = 20; c.Bushes = 40; c.Animals = 20; });
        var ws = new World(cfgS, 6, 5, -1); var rnd = new Random(2); var inp = new AgentInput[6];
        var sw = Stopwatch.StartNew(); int steps = 30 * 60 * 5; bool ok = true;
        try
        {
            for (int t = 0; t < steps; t++)
            {
                for (int i = 0; i < 6; i++)
                {
                    inp[i] = new AgentInput { Mx = rnd.Next(-1, 2), My = rnd.Next(-1, 2), Aim = (float)(rnd.NextDouble() * 6.28), AimDist = rnd.Next(300), Attack = rnd.Next(3) == 0, Sprint = rnd.Next(4) == 0, Cmd = rnd.Next(12) == 0 ? (Cmd)rnd.Next(1, 11) : Cmd.None, Arg = rnd.Next(0, 80) };
                }
                ws.Step(inp); if (t % 200 == 0) foreach (var ag in ws.Agents) { ag.Hunger = ag.Cold = 255; if (!ag.Alive) inp[0].Cmd = Cmd.Respawn; }
                foreach (var ag in ws.Agents) if (!ag.Alive) { inp[ag.Id].Cmd = Cmd.Respawn; }
            }
        }
        catch (Exception ex) { ok = false; Console.WriteLine(ex); }
        Check(ok, $"estresse: 5 min simulados, 6 agentes aleatórios, sem exceção ({steps / sw.Elapsed.TotalSeconds:N0} ticks/s)");

        Console.WriteLine($"\n{total - fails}/{total} testes passaram");
        return fails == 0 ? 0 : 1;
    }

    static int Bench()
    {
        var cfg = new SimConfig(); var w = new World(cfg, 16, 1, -1); var inp = new AgentInput[16]; var rnd = new Random(1);
        var sw = Stopwatch.StartNew(); int n = 30 * 120;
        for (int t = 0; t < n; t++) { for (int i = 0; i < 16; i++) inp[i] = new AgentInput { Mx = rnd.Next(-1, 2), My = rnd.Next(-1, 2), Aim = (float)(rnd.NextDouble() * 6.28), Attack = rnd.Next(3) == 0 }; w.Step(inp); }
        Console.WriteLine($"{n} ticks, 16 agentes, mundo {cfg.WorldW}x{cfg.WorldH}: {n / sw.Elapsed.TotalSeconds:N0} ticks/s (tempo real = 30 ticks/s)");
        return 0;
    }
}
