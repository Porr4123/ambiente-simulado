using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SimBox;
using static SimBox.Item;

// SimBox.Web — servidor local: o motor C# (física livre, 30 ticks/s) roda aqui e o navegador desenha.
//   dotnet run --project SimBox.Web -c Release [-- --port 5050 --seed 1 --day 480 --night 480 --craftscale 1 --noopen]
static class Program
{
    static void Main(string[] args)
    {
        int port = 5050, seed = 1; float day = 480, night = 480, craft = 1f; bool open = true;
        for (int i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--port": port = int.Parse(args[++i]); break;
                case "--seed": seed = int.Parse(args[++i]); break;
                case "--day": day = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--night": night = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--craftscale": craft = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--noopen": open = false; break;
            }
        var game = new GameServer(seed, day, night, craft);
        var listener = new HttpListener(); string url = $"http://localhost:{port}/"; listener.Prefixes.Add(url);
        try { listener.Start(); }
        catch (Exception ex) { Console.WriteLine($"Não consegui abrir {url}: {ex.Message}\nTente outra porta: --port 5051"); return; }
        Console.WriteLine($"SimBox 2D (física livre) rodando em {url}  (Ctrl+C para parar)");
        new Thread(game.Loop) { IsBackground = true }.Start();
        if (open) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
        while (true)
        {
            var ctx = listener.GetContext();
            Task.Run(() => { try { game.Handle(ctx); } catch { try { ctx.Response.Abort(); } catch { } } });
        }
    }
}

sealed class GameServer
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const float BoxX = 1700, BoxY = 1100;         // área (em unidades) enviada ao navegador ao redor do jogador
    readonly SimConfig cfg; readonly World w; readonly Agent p; int seed;
    readonly object inLock = new();
    float mx, my, aim, dist; bool sprint, attack; readonly Queue<(int cmd, int arg)> cmds = new();
    readonly object stLock = new(); long version; string latest = "{}"; readonly List<(long v, string json)> ring = new();
    readonly List<string> logQ = new(); string lastLog = ""; float lastLogT = -99;
    readonly string defsJson; string worldJson = "{}"; int worldNodes = -1;

    public GameServer(int seed, float day, float night, float craft)
    {
        this.seed = seed;
        cfg = new SimConfig { DayLen = day, NightLen = night, CraftTimeScale = craft };
        w = new World(cfg, 1, seed, 0); p = w.Agents[0];
        w.Log = (a, m) => { lock (logQ) { if (m == lastLog && w.Time - lastLogT < 1.5f) return; lastLog = m; lastLogT = w.Time; logQ.Add(m); } };
        w.Log(p, "Bem-vindo! Bata em árvores com o mouse (clique esquerdo). Com 2 pedras e 10 madeiras faça um machado (tecla C).");
        defsJson = BuildDefs(); Publish(BuildState());
    }

    // ------------------------------------------------------------------ loop
    public void Loop()
    {
        var sw = Stopwatch.StartNew(); double tick = 1000.0 / cfg.Tps, next = tick;
        while (true)
        {
            double now = sw.Elapsed.TotalMilliseconds;
            if (now < next) { Thread.Sleep(1); continue; }
            if (now - next > 10 * tick) next = now;
            next += tick;
            try
            {
                var inp = new AgentInput[1];
                lock (inLock)
                {
                    inp[0] = new AgentInput { Mx = mx, My = my, Aim = aim, AimDist = dist, Sprint = sprint, Attack = attack };
                    if (cmds.Count > 0) { var c = cmds.Dequeue(); inp[0].Cmd = (Cmd)c.cmd; inp[0].Arg = c.arg; }
                }
                w.Step(inp);
                Publish(BuildState());
            }
            catch (Exception ex) { Console.WriteLine("Erro no tick: " + ex); }
        }
    }

    void Publish(string json) { lock (stLock) { latest = json; version++; ring.Add((version, json)); if (ring.Count > 90) ring.RemoveAt(0); Monitor.PulseAll(stLock); } }

    // ------------------------------------------------------------------ HTTP
    public void Handle(HttpListenerContext c)
    {
        var path = c.Request.Url!.AbsolutePath; var resp = c.Response; resp.Headers["Cache-Control"] = "no-cache";
        if (path == "/") Send(resp, "text/html; charset=utf-8", LoadHtml());
        else if (path == "/api/defs") Send(resp, "application/json; charset=utf-8", defsJson);
        else if (path == "/api/world") { string s; lock (stLock) { if (worldNodes != w.Nodes.Count) { worldJson = BuildWorld(); worldNodes = w.Nodes.Count; } s = worldJson; } Send(resp, "application/json; charset=utf-8", s); }
        else if (path == "/api/state") { string s; lock (stLock) s = latest; Send(resp, "application/json; charset=utf-8", s); }
        else if (path == "/api/input" && c.Request.HttpMethod == "POST") { ReadInput(c.Request); resp.StatusCode = 204; resp.Close(); }
        else if (path == "/api/stream") Stream(resp);
        else { resp.StatusCode = 404; resp.Close(); }
    }
    static string LoadHtml() { using var s = typeof(GameServer).Assembly.GetManifestResourceStream("index.html")!; using var r = new StreamReader(s, Encoding.UTF8); return r.ReadToEnd(); }
    static void Send(HttpListenerResponse r, string type, string body) { var b = Encoding.UTF8.GetBytes(body); r.ContentType = type; r.ContentLength64 = b.Length; r.OutputStream.Write(b, 0, b.Length); r.Close(); }

    void Stream(HttpListenerResponse r)
    {
        r.ContentType = "text/event-stream"; r.SendChunked = true; long last = -1;
        try
        {
            var o = r.OutputStream;
            while (true)
            {
                List<string> batch;
                lock (stLock)
                {
                    while (version == last) Monitor.Wait(stLock, 2000);
                    batch = last < 0 ? new List<string> { latest } : ring.Where(e => e.v > last).Select(e => e.json).ToList(); last = version;
                }
                foreach (var s in batch) { var b = Encoding.UTF8.GetBytes("data: " + s + "\n\n"); o.Write(b, 0, b.Length); }
                o.Flush();
            }
        }
        catch { try { r.Abort(); } catch { } }
    }

    void ReadInput(HttpListenerRequest req)
    {
        using var rd = new StreamReader(req.InputStream, Encoding.UTF8); using var doc = JsonDocument.Parse(rd.ReadToEnd()); var j = doc.RootElement;
        lock (inLock)
        {
            if (j.TryGetProperty("mx", out var a)) mx = Math.Clamp(a.GetSingle(), -1, 1);
            if (j.TryGetProperty("my", out a)) my = Math.Clamp(a.GetSingle(), -1, 1);
            if (j.TryGetProperty("aim", out a)) aim = a.GetSingle();
            if (j.TryGetProperty("dist", out a)) dist = a.GetSingle();
            if (j.TryGetProperty("sprint", out a)) sprint = a.GetBoolean();
            if (j.TryGetProperty("attack", out a)) attack = a.GetBoolean();
            if (j.TryGetProperty("cmds", out a) && a.ValueKind == JsonValueKind.Array)
                foreach (var e in a.EnumerateArray()) if (cmds.Count < 30) cmds.Enqueue((e[0].GetInt32(), e[1].GetInt32()));
        }
    }

    // ------------------------------------------------------------------ JSON
    static string F(float v) => v.ToString("0.#", Inv);
    static string Q(string s) => JsonSerializer.Serialize(s);

    string BuildDefs()
    {
        var sb = new StringBuilder("{\"items\":[");
        for (int i = 0; i < Defs.ItemCount; i++) sb.Append(i > 0 ? "," : "").Append($"[{Q(Defs.ItemNames[i])},{Defs.Category((Item)i)}]");
        sb.Append("],\"recipes\":[");
        for (int i = 0; i < Defs.Recipes.Length; i++)
        {
            var r = Defs.Recipes[i];
            sb.Append(i > 0 ? "," : "").Append($"{{\"o\":{(int)r.Out},\"n\":{r.OutN},\"s\":{(int)r.Station},\"a\":{(int)r.Alt},\"t\":{F(r.Secs * cfg.CraftTimeScale)},\"b\":{(r.BotOk ? 1 : 0)},\"l\":{r.Level},\"sp\":{r.SkillPts},\"pre\":{(r.Prereq is Item pr ? (int)pr : -1)},\"i\":[");
            for (int k = 0; k < r.In.Length; k++) sb.Append(k > 0 ? "," : "").Append($"[{(int)r.In[k].item},{r.In[k].n}]");
            sb.Append("]}");
        }
        sb.Append("],\"blds\":[");
        for (int i = 0; i < Defs.Blds.Length; i++) sb.Append(i > 0 ? "," : "").Append($"[{Q(Defs.Blds[i].Name)},{F(Defs.Blds[i].Hp)},{(Defs.Blds[i].IsFloor ? 1 : 0)}]");
        sb.Append("],\"guns\":[");
        for (int i = 0; i < Defs.Guns.Length; i++) { var g = Defs.Guns[i]; sb.Append(i > 0 ? "," : "").Append($"[{(int)g.Item},{(int)g.Ammo},{g.Mag},{F(g.Range)}]"); }
        sb.Append("],\"placeables\":[").Append(string.Join(",", Defs.Placeables.Select(x => (int)x)));
        sb.Append("],\"ghouls\":[");
        for (int i = 0; i < Defs.Ghouls.Length; i++) { var g = Defs.Ghouls[i]; sb.Append(i > 0 ? "," : "").Append($"[{Q(g.Name)},{F(g.Radius)}]"); }
        sb.Append("],\"xp\":[").Append(string.Join(",", Enumerable.Range(0, 60).Select(l => Defs.XpFor(l))));
        sb.Append($"],\"cmd\":{{\"select\":{(int)Cmd.Select},\"craft\":{(int)Cmd.Craft},\"research\":{(int)Cmd.Research},\"interact\":{(int)Cmd.Interact},\"store\":{(int)Cmd.Store},\"take\":{(int)Cmd.Take},\"drop\":{(int)Cmd.Drop},\"reload\":{(int)Cmd.Reload},\"eat\":{(int)Cmd.Eat},\"respawn\":{(int)Cmd.Respawn}}}");
        sb.Append($",\"cfg\":{{\"tps\":{cfg.Tps},\"dayLen\":{F(cfg.DayLen)},\"nightLen\":{F(cfg.NightLen)},\"W\":{F(cfg.WorldW)},\"H\":{F(cfg.WorldH)},\"cell\":{F(cfg.Cell)},\"slots\":{cfg.InventorySlots},\"fireR\":{F(cfg.FireRadius)},\"flareR\":{F(cfg.FlareRadius)},\"reach\":{F(cfg.ReachBuild)},\"boxX\":{BoxX},\"boxY\":{BoxY}}}}}");
        return sb.ToString();
    }

    string BuildWorld()
    {
        var sb = new StringBuilder($"{{\"w\":{F(w.W)},\"h\":{F(w.H)},\"nodes\":[");
        for (int i = 0; i < w.Nodes.Count; i++) { var n = w.Nodes[i]; sb.Append(i > 0 ? "," : "").Append($"[{n.Id},{(int)n.Kind},{F(n.Pos.X)},{F(n.Pos.Y)},{F(n.R)}]"); }
        return sb.Append("]}").ToString();
    }

    string BuildState()
    {
        var sb = new StringBuilder(24000); var C = w.Cfg;
        sb.Append($"{{\"t\":{F(w.Time)},\"tin\":{F(w.TimeInCycle)},\"day\":{w.DayNumber},\"alive\":{(p.Alive ? 1 : 0)},\"nc\":{w.Nodes.Count}");
        int lvl = p.Level; var wc = World.ArmorLevel(p);
        int mag = -1, magMax = 0; if (w.SelectedItem(p) is Item si && Defs.GunOf(si) is Gun gun && gun.Mag > 0) { mag = p.Mag[(int)si]; magMax = gun.Mag; }
        int craftOut = p.CraftRecipe >= 0 ? (int)Defs.Recipes[p.CraftRecipe].Out : -1;
        sb.Append($",\"me\":{{\"x\":{F(p.Pos.X)},\"y\":{F(p.Pos.Y)},\"hp\":{F(p.Hp)},\"hunger\":{F(p.Hunger)},\"cold\":{F(p.Cold)},\"stam\":{F(p.Stamina)},\"rad\":{F(p.Rad)},\"lvl\":{lvl},\"xp\":{F(p.Xp)},\"sp\":{p.SkillPts},\"sel\":{p.Selected},\"kills\":{p.Kills},\"armor\":{wc},\"mag\":{mag},\"magMax\":{magMax},\"reload\":{F(p.ReloadLeft)},\"craftLeft\":{F(p.CraftLeft)},\"craftTotal\":{F(p.CraftTotal)},\"craftOut\":{craftOut},\"cd\":{F(p.AttackCd)},\"slots\":[");
        for (int i = 0; i < p.Slots.Length; i++) sb.Append(i > 0 ? "," : "").Append($"[{(p.Slots[i].N > 0 ? (int)p.Slots[i].Item : -1)},{p.Slots[i].N}]");
        sb.Append("]}");
        sb.Append(",\"unl\":[").Append(string.Join(",", Enumerable.Range(0, Defs.ItemCount).Where(i => p.Unlocked[i])));
        sb.Append("],\"rsch\":[").Append(string.Join(",", Enumerable.Range(0, Defs.ItemCount).Where(i => w.ResearchCheck(p, (Item)i) == null)));
        var here = new List<int>(); var can = new List<int>();
        for (int r = 0; r < Defs.Recipes.Length; r++)
        {
            var rec = Defs.Recipes[r];
            if (rec.Station == Bld.None || w.FindStation(p.Pos, rec.Station) >= 0 || (rec.Alt != Bld.None && w.FindStation(p.Pos, rec.Alt) >= 0)) here.Add(r);
            if (p.Alive && w.CraftCheck(p, r, out _) == null) can.Add(r);
        }
        sb.Append("],\"here\":[").Append(string.Join(",", here)).Append("],\"can\":[").Append(string.Join(",", can)).Append(']');

        // construções e pisos na área
        float x0 = p.Pos.X - BoxX, x1 = p.Pos.X + BoxX, y0 = p.Pos.Y - BoxY, y1 = p.Pos.Y + BoxY;
        int gx0 = Math.Max(0, (int)(x0 / C.Cell)), gx1 = Math.Min(w.GW - 1, (int)(x1 / C.Cell)), gy0 = Math.Max(0, (int)(y0 / C.Cell)), gy1 = Math.Min(w.GH - 1, (int)(y1 / C.Cell));
        var blds = new StringBuilder(); var flo = new StringBuilder();
        for (int gy = gy0; gy <= gy1; gy++)
            for (int gx = gx0; gx <= gx1; gx++)
            {
                int c = gy * w.GW + gx; byte b = w.BldGrid[c], f = w.FloorGrid[c];
                if (f != 0) flo.Append(flo.Length > 0 ? "," : "").Append($"[{gx},{gy},{f}]");
                if (b != 0) blds.Append(blds.Length > 0 ? "," : "").Append($"[{gx},{gy},{b},{F(w.BuildingHp(c) / Defs.Blds[b].Hp)},{(w.IsLit(c) ? 1 : 0)}]");
            }
        sb.Append(",\"bld\":[").Append(blds).Append("],\"flo\":[").Append(flo).Append(']');

        bool In(Vector2 q) => q.X >= x0 && q.X <= x1 && q.Y >= y0 && q.Y <= y1;
        // nós mortos e frutos dentro da área
        var dead = new StringBuilder(); var fruit = new StringBuilder();
        foreach (var n in w.Nodes)
        {
            if (!In(n.Pos)) continue;
            if (!n.Alive) dead.Append(dead.Length > 0 ? "," : "").Append(n.Id);
            else if (n.Kind == NodeKind.OrangeBush || n.Kind == NodeKind.TomatoBush) fruit.Append(fruit.Length > 0 ? "," : "").Append($"[{n.Id},{n.Fruit}]");
        }
        sb.Append(",\"dead\":[").Append(dead).Append("],\"fruit\":[").Append(fruit).Append(']');
        void Arr(string name, IEnumerable<string> items) { sb.Append(",\"").Append(name).Append("\":[").Append(string.Join(",", items)).Append(']'); }
        Arr("ghouls", w.Ghouls.Where(g => g.Alive && In(g.Pos)).Select(g => $"[{g.Id},{(int)g.Kind},{F(g.Pos.X)},{F(g.Pos.Y)},{F(g.Hp / g.MaxHp)},{F(g.Dir.X)},{F(g.Dir.Y)}]"));
        Arr("animals", w.Animals.Where(a => a.Alive && In(a.Pos)).Select(a => $"[{a.Id},{F(a.Pos.X)},{F(a.Pos.Y)}]"));
        Arr("carcasses", w.Carcasses.Where(c => In(c.Pos)).Select(c => $"[{c.Id},{F(c.Pos.X)},{F(c.Pos.Y)}]"));
        Arr("drops", w.Drops.Where(d => In(d.Pos)).Select(d => $"[{d.Id},{(int)d.Item},{d.N},{F(d.Pos.X)},{F(d.Pos.Y)}]"));
        Arr("projs", w.Projs.Where(q => In(q.Pos)).Select(q => $"[{q.Id},{(int)q.Kind},{F(q.Pos.X)},{F(q.Pos.Y)},{F(q.Dir.X)},{F(q.Dir.Y)}]"));
        Arr("robots", w.Robots.Where(r => r.Alive && In(r.Pos)).Select(r => $"[{r.Id},{(r.Hall ? 1 : 0)},{F(r.Pos.X)},{F(r.Pos.Y)},{F(r.Hp / 600f)}]"));
        Arr("flares", w.Flares.Select(f => $"[{F(f.Pos.X)},{F(f.Pos.Y)},{F(f.Left)}]"));
        Arr("agents", w.Agents.Where(a => a.Alive && a != p).Select(a => $"[{a.Id},{F(a.Pos.X)},{F(a.Pos.Y)},{F(a.Aim)},{F(a.Hp / 255f)}]"));
        Arr("ev", w.Events.Select(e => $"[{e.T},{F(e.X)},{F(e.Y)},{F(e.X2)},{F(e.Y2)},{e.A}]"));
        lock (logQ) { Arr("log", logQ.Select(Q)); logQ.Clear(); }
        sb.Append('}'); return sb.ToString();
    }
}
