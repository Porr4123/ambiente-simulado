using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SimBox.Train;

/// <summary>
/// Monitor duplo em duas janelas do navegador (uma por monitor físico):
///   http://localhost:PORT/        → MAPA: agentes (azul/vermelho), recursos (amarelo), construções (quadrados cinza), ghouls/inimigos (verde)
///   http://localhost:PORT/focus   → FOCO: câmera seguindo o top 1 atual, com status, inventário e a decisão da rede
/// A simulação roda no thread do treino e publica um snapshot JSON ~10x por segundo; o navegador só lê.
/// </summary>
public sealed class MonitorServer
{
    readonly EvolutionaryTrainer trainer;
    readonly HttpListener listener = new();
    readonly int port;
    readonly byte[] html;
    readonly Stopwatch sw = Stopwatch.StartNew();
    long lastMs = -1000;

    volatile string state = "{}";
    sealed record NodesSnap(int V, string Json);
    volatile NodesSnap nodes = new(0, "[]");
    World? nodesWorld; int nodesAlive = -1;

    int focusId = -1;
    readonly Queue<string> recent = new();     // só mexe nele o thread da simulação

    public string MapUrl => $"http://localhost:{port}/";
    public string FocusUrl => $"http://localhost:{port}/focus";

    public MonitorServer(EvolutionaryTrainer trainer, int port)
    {
        this.trainer = trainer; this.port = port;
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("monitor.html") ?? throw new InvalidOperationException("monitor.html não embutido");
        using var ms = new MemoryStream(); s.CopyTo(ms); html = ms.ToArray();
        trainer.OnFrame += w => Update(w);
        trainer.OnEvent += AddEvent;
    }

    public bool Start(out string error)
    {
        error = "";
        listener.Prefixes.Add($"http://localhost:{port}/");
        try { listener.Start(); }
        catch (Exception ex) { error = ex.Message; return false; }
        new Thread(Serve) { IsBackground = true, Name = "monitor-http" }.Start();
        return true;
    }

    void AddEvent(string line)
    {
        recent.Enqueue(line); while (recent.Count > 14) recent.Dequeue();
    }

    void Serve()
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = listener.GetContext(); } catch { return; }
            try { Handle(ctx); } catch { try { ctx.Response.Abort(); } catch { } }
        }
    }

    void Handle(HttpListenerContext ctx)
    {
        string path = ctx.Request.Url?.AbsolutePath ?? "/";
        byte[] body; string type;
        if (path == "/api/state")
        {
            string s = state; var ns = nodes;
            int.TryParse(ctx.Request.QueryString["nv"], out int client);
            string extra = client != ns.V ? $",\"nv\":{ns.V},\"nodes\":{ns.Json}" : $",\"nv\":{ns.V}";
            if (s.Length > 1) s = s[..^1] + extra + "}"; else s = "{" + extra[1..] + "}";
            body = Encoding.UTF8.GetBytes(s); type = "application/json; charset=utf-8";
        }
        else if (path == "/" || path == "/focus" || path == "/index.html") { body = html; type = "text/html; charset=utf-8"; }
        else { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
        ctx.Response.ContentType = type; ctx.Response.ContentLength64 = body.Length;
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.OutputStream.Write(body, 0, body.Length); ctx.Response.OutputStream.Close();
    }

    static float LiveScore(World w, int i) => w.Rewards[i] + (w.Agents[i].Alive ? 0f : -50f) + w.Agents[i].Level * 2f;

    /// <summary>Chamado a cada tick no thread da simulação; só monta o snapshot a cada ~100 ms.</summary>
    public void Update(World w)
    {
        long now = sw.ElapsedMilliseconds;
        if (now - lastMs < 100) return;
        lastMs = now;
        try { state = Build(w); } catch { /* um snapshot ruim não pode derrubar o treino */ }
    }

    string Build(World w)
    {
        // nós (recursos) só vão para o navegador quando mudam
        int alive = 0; foreach (var n in w.Nodes) if (n.Alive) alive++;
        if (w != nodesWorld || alive != nodesAlive)
        {
            nodesWorld = w; nodesAlive = alive;
            var sbn = new StringBuilder("["); bool first = true;
            foreach (var n in w.Nodes)
            {
                if (!n.Alive) continue;
                if (!first) sbn.Append(','); first = false;
                sbn.Append('[').Append((int)n.Pos.X).Append(',').Append((int)n.Pos.Y).Append(',').Append((int)n.Kind).Append(']');
            }
            nodes = new NodesSnap(nodes.V + 1, sbn.Append(']').ToString());
        }

        int count = w.Agents.Length;
        var score = new float[count]; int best = 0;
        for (int i = 0; i < count; i++) { score[i] = LiveScore(w, i); if (score[i] > score[best]) best = i; }
        // histerese: só troca o foco se o líder novo for claramente melhor (ou o foco morreu)
        if (focusId < 0 || focusId >= count || (best != focusId && (!w.Agents[focusId].Alive || score[best] > score[focusId] + 3f))) focusId = best;

        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms))
        {
            j.WriteStartObject();
            j.WriteNumber("gen", trainer.CurrentGeneration); j.WriteNumber("weeks", trainer.CurrentWeeks);
            j.WriteNumber("t", MathF.Round(w.Time, 1)); j.WriteNumber("tot", trainer.SimSeconds);
            j.WriteBoolean("day", w.IsDay); j.WriteNumber("dayn", w.DayNumber); j.WriteBoolean("done", w.Done);
            j.WriteNumber("W", w.W); j.WriteNumber("H", w.H); j.WriteNumber("cell", w.Cfg.Cell);
            j.WriteNumber("best", MathF.Round(trainer.BestFitness, 1)); j.WriteNumber("avg", MathF.Round(trainer.AvgFitness, 1));
            j.WriteNumber("sc0", MathF.Round(trainer.Team0Score, 1)); j.WriteNumber("sc1", MathF.Round(trainer.Team1Score, 1));
            j.WriteNumber("speed", trainer.Speed);

            j.WriteStartArray("ag");
            for (int i = 0; i < count; i++)
            {
                var a = w.Agents[i];
                j.WriteStartArray(); j.WriteNumberValue(i); j.WriteNumberValue(a.Team & 1);
                j.WriteNumberValue((int)a.Pos.X); j.WriteNumberValue((int)a.Pos.Y); j.WriteNumberValue(a.Alive ? 1 : 0);
                j.WriteNumberValue(MathF.Round(score[i], 1)); j.WriteNumberValue(a.Level);
                j.WriteEndArray();
            }
            j.WriteEndArray();

            j.WriteStartArray("gh");
            foreach (var g in w.Ghouls) if (g.Alive) { j.WriteStartArray(); j.WriteNumberValue((int)g.Pos.X); j.WriteNumberValue((int)g.Pos.Y); j.WriteNumberValue((int)g.Kind); j.WriteEndArray(); }
            j.WriteEndArray();

            j.WriteStartArray("bl");
            for (int c = 0; c < w.BldGrid.Length; c++) if (w.BldGrid[c] != 0) { j.WriteStartArray(); j.WriteNumberValue(c % w.GW); j.WriteNumberValue(c / w.GW); j.WriteNumberValue(w.BldGrid[c]); j.WriteEndArray(); }
            j.WriteEndArray();

            j.WriteStartArray("pr");
            foreach (var p in w.Projs) { j.WriteStartArray(); j.WriteNumberValue((int)p.Pos.X); j.WriteNumberValue((int)p.Pos.Y); j.WriteEndArray(); }
            j.WriteEndArray();

            j.WriteStartArray("dr");
            int nd = 0; foreach (var d in w.Drops) { if (nd++ >= 300) break; j.WriteStartArray(); j.WriteNumberValue((int)d.Pos.X); j.WriteNumberValue((int)d.Pos.Y); j.WriteEndArray(); }
            j.WriteEndArray();

            j.WriteStartArray("log"); foreach (var e in recent) j.WriteStringValue(e); j.WriteEndArray();

            j.WriteStartArray("hist");
            foreach (var h in trainer.History.Skip(Math.Max(0, trainer.History.Count - 80))) { j.WriteStartArray(); j.WriteNumberValue(h.gen); j.WriteNumberValue(MathF.Round(h.best, 1)); j.WriteNumberValue(MathF.Round(h.avg, 1)); j.WriteEndArray(); }
            j.WriteEndArray();

            // ---- foco: top 1 atual
            var f = w.Agents[focusId]; var brain = trainer.Population[focusId]; var inp = brain.LastInput;
            j.WriteStartObject("foc");
            j.WriteNumber("id", focusId); j.WriteNumber("team", f.Team & 1); j.WriteBoolean("alive", f.Alive);
            j.WriteNumber("x", (int)f.Pos.X); j.WriteNumber("y", (int)f.Pos.Y); j.WriteNumber("aim", MathF.Round(f.Aim, 2));
            j.WriteNumber("hp", MathF.Round(f.Hp, 0)); j.WriteNumber("hu", MathF.Round(f.Hunger, 0)); j.WriteNumber("co", MathF.Round(f.Cold, 0));
            j.WriteNumber("st", MathF.Round(f.Stamina, 0)); j.WriteNumber("rad", MathF.Round(f.Rad, 0)); j.WriteNumber("max", Defs.StatMax);
            j.WriteNumber("lvl", f.Level); j.WriteNumber("kills", f.Kills); j.WriteNumber("ek", f.EnemyKills); j.WriteNumber("fk", f.FriendKills);
            j.WriteNumber("edmg", MathF.Round(f.EnemyDmg, 0)); j.WriteNumber("fdmg", MathF.Round(f.FriendDmg, 0));
            j.WriteNumber("tools", f.ToolsMade); j.WriteNumber("don", f.Donations);
            j.WriteNumber("score", MathF.Round(score[focusId], 1)); j.WriteNumber("rew", MathF.Round(w.Rewards[focusId], 1));
            j.WriteNumber("sel", f.Selected);
            var sel = w.SelectedItem(f); j.WriteString("wpn", sel is Item si ? Defs.Name(si) : "mão"); j.WriteNumber("tier", sel is Item si2 ? Defs.Tier(si2) : 1);
            j.WriteStartArray("slots");
            foreach (var s in f.Slots) { j.WriteStartArray(); if (s.N > 0) { j.WriteStringValue(Defs.Name(s.Item)); j.WriteNumberValue(s.N); } j.WriteEndArray(); }
            j.WriteEndArray();
            if (f.CraftLeft > 0 && f.CraftRecipe >= 0) { j.WriteStartArray("craft"); j.WriteStringValue(Defs.Name(Defs.Recipes[f.CraftRecipe].Out)); j.WriteNumberValue(MathF.Round(f.CraftLeft, 1)); j.WriteNumberValue(MathF.Round(f.CraftTotal, 1)); j.WriteEndArray(); }
            j.WriteStartObject("in");
            j.WriteNumber("mx", MathF.Round(inp.Mx, 2)); j.WriteNumber("my", MathF.Round(inp.My, 2));
            j.WriteBoolean("atk", inp.Attack); j.WriteBoolean("spr", inp.Sprint); j.WriteString("cmd", inp.Cmd.ToString());
            j.WriteEndObject();
            j.WriteEndObject();

            j.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
