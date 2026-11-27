using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using static SimBox.Item;

namespace SimBox.Train;

/// <summary>
/// Modelo "coleta": 32 IAs começam do zero no mesmo mundo, sem monstros e sem combate entre elas.
///  · Minuto 1 (janela de coleta): extrair o máximo de recursos. Madeira e pedra valem pouco; minério de ferro vale bem mais, mas exige picareta.
///    A pedra não sai com a mão (o agente nasce com 2 pedras, as do machado). Frutas só se colhem com a mão. Recursos têm quantidade, quebram
///    quando esgotam e reaparecem com o tempo.
///  · Depois, até os 9 minutos no total (mais 8 min): chegar ao ponto de extração, no canto inferior esquerdo. Entrar nele destrói o agente
///    e vale 1000 pontos. No caminho: fome, frio (constante) e zonas de radiação.
///  · Tecnologia: só fogueira, mesa de trabalho, machado básico e picareta (a mesa tem fila de fabricação própria). Nada mais é fabricável.
/// Pontuação = World.Score (só coleta dentro da janela + extração); não usa as recompensas de combate/times.
/// </summary>
public sealed class CollectModel : TrainingModel
{
    public const float GatherWindow = 60f;                 // minuto de coleta
    public const float TravelSeconds = 480f;               // 8 minutos para chegar à extração
    public const float RunSeconds = GatherWindow + TravelSeconds;

    public override string Id => "coleta";
    public override string Title => "Coleta e extração";
    public override int PopulationSize => 32;
    public override int TeamOf(int slot) => 0;             // sem times
    public override int GroupOf(int slot) => slot & 1;     // só para dividir a decisão entre as 2 threads
    public override bool SelectOccupiedSlots => true;      // Select escolhe entre o que está no inventário (+ mão vazia, para colher fruta)

    static readonly HashSet<Item> Allowed = new() { Campfire, Workbench, AxeBasic, PickBasic };

    public override SimConfig MakeConfig() => new()
    {
        Tps = 20,
        WorldW = 4500, WorldH = 4500,
        CraftTimeScale = 1f,                               // tempos reais de fabricação (machado 4 s, fogueira 5 s, mesa 8 s, picareta 15 s na fila da mesa)
        DayLen = 0f, NightLen = 100000f,                   // frio constante: sem dia para se recuperar, só perto de uma fogueira acesa
        Trees = 500, Rocks = 180, IronNodes = 80, SulfurNodes = 0, Crates = 0, Bushes = 60, Animals = 0,
        NoGhouls = true, NoPvp = true,                     // sem monstros nem combate
        FiniteNodes = true, FruitHandOnly = true,
        AllowedCrafts = Allowed, StationQueues = true,
        RadZones = 5, Extraction = true,
        CollectScore = true, GatherWindow = GatherWindow
    };

    public override float PrepareGeneration(SimConfig cfg, int generation) => RunSeconds;
    public override string GenerationTitle(int generation) => $"{GatherWindow / 60f:0} min de coleta + {TravelSeconds / 60f:0} min até a extração";

    // ------------------------------------------------------------------ entradas extras (23)
    public override int ExtraFeatures => 23;

    public override void FillExtraFeatures(World w, Agent a, float[] f, ref int i)
    {
        float diag = MathF.Sqrt(w.W * w.W + w.H * w.H);
        // posição absoluta (2): sem isto a rede não sabe onde está em relação ao canto da extração
        f[i++] = a.Pos.X / w.W; f[i++] = a.Pos.Y / w.H;
        // ponto de extração (3): direção e distância
        if (w.ExtractionPos is Vector2 ep)
        {
            var d = ep - a.Pos; float dist = d.Length(); if (dist > 0.01f) d /= dist;
            f[i++] = d.X; f[i++] = d.Y; f[i++] = Math.Clamp(dist / diag, 0f, 1f);
        }
        else { f[i++] = 0f; f[i++] = 0f; f[i++] = 1f; }
        // tempo (4): quanto da simulação passou, se ainda estamos na janela de coleta, quanto resta dela e do total
        f[i++] = Math.Clamp(w.Time / RunSeconds, 0f, 1f);
        f[i++] = w.Time < GatherWindow ? 1f : 0f;
        f[i++] = Math.Clamp((GatherWindow - w.Time) / GatherWindow, 0f, 1f);
        f[i++] = Math.Clamp((RunSeconds - w.Time) / RunSeconds, 0f, 1f);
        // zona de radiação mais próxima (4): direção até o centro, distância até a borda (negativa = dentro) e se está dentro
        float bestEdge = float.MaxValue; Vector2 bestDir = Vector2.Zero; bool inside = false;
        foreach (var z in w.RadZones)
        {
            var d = z.Pos - a.Pos; float dist = d.Length(), edge = dist - z.R;
            if (edge < bestEdge) { bestEdge = edge; bestDir = dist > 0.01f ? d / dist : Vector2.Zero; inside = edge < 0; }
        }
        if (bestEdge == float.MaxValue) { f[i++] = 0f; f[i++] = 0f; f[i++] = 1f; f[i++] = 0f; }
        else { f[i++] = bestDir.X; f[i++] = bestDir.Y; f[i++] = Math.Clamp(bestEdge / 1500f, -1f, 1f); f[i++] = inside ? 1f : 0f; }
        // fila da mesa (1): tenho algo sendo fabricado na mesa?
        f[i++] = w.QueuedJobs(a) > 0 ? 1f : 0f;
        // arbusto com fruta mais próximo (3): as frutas não estão entre os recursos da base
        float bd = float.MaxValue; var bp = Vector2.Zero;
        foreach (var n in w.Nodes)
        {
            if (!n.Alive || n.Fruit <= 0 || (n.Kind != NodeKind.OrangeBush && n.Kind != NodeKind.TomatoBush)) continue;
            float d2 = Vector2.DistanceSquared(n.Pos, a.Pos); if (d2 < bd) { bd = d2; bp = n.Pos; }
        }
        if (bd == float.MaxValue) { f[i++] = 0f; f[i++] = 0f; f[i++] = 1f; }
        else { var d = bp - a.Pos; float dist = MathF.Sqrt(bd); if (dist > 0.01f) d /= dist; f[i++] = d.X; f[i++] = d.Y; f[i++] = Math.Clamp(dist / 1500f, 0f, 1f); }
        // o que está na mão (6): vazia, machado, picareta, mesa, fogueira, comida
        var sel = w.SelectedItem(a);
        f[i++] = sel == null ? 1f : 0f; f[i++] = sel == AxeBasic ? 1f : 0f; f[i++] = sel == PickBasic ? 1f : 0f;
        f[i++] = sel == Workbench ? 1f : 0f; f[i++] = sel == Campfire ? 1f : 0f; f[i++] = sel is Item si && Defs.Edible(si) != null ? 1f : 0f;
    }

    // ------------------------------------------------------------------ avaliação
    public override GenerationStats Evaluate(World w, AgentBrain[] pop)
    {
        int extracted = 0; float total = 0;
        for (int i = 0; i < pop.Length; i++)
        {
            var b = pop[i]; var a = w.Agents[i];
            b.RawReward = w.Score[i]; b.Fitness = w.Score[i];            // fitness = pontos (coleta na janela + 1000 da extração)
            b.Extracted = a.Extracted; b.ExtractedAt = a.ExtractedAt; b.Survived = a.Alive; b.MinExtractDist = a.MinExtractDist;
            b.GotWood = a.GotWood; b.GotStone = a.GotStone; b.GotOre = a.GotOre; b.GotFruit = a.GotFruit;
            b.ToolsMade = a.ToolsMade; b.StationsMade = a.StationsMade; b.FinalLevel = a.Level;
            if (a.Extracted) extracted++;
            total += w.Score[i];
        }
        return new GenerationStats(extracted, total, 0f);
    }

    public override string DoneNote => "todos foram extraídos ou morreram, geração encerrada antes";

    public override void WriteLogBody(StringBuilder sb, GenerationInfo g)
    {
        var inv = CultureInfo.InvariantCulture; var pop = g.Ranked;
        sb.AppendLine(string.Format(inv, "Melhor {0:0.0} pontos · média {1:0.0} · extraídos {2}/{3} · mortos {4}", g.Best, g.Avg, g.Survivors, PopulationSize, g.World.Deaths));
        sb.AppendLine(string.Format(inv, "Coletado (média por IA): madeira {0:0.0} · pedra {1:0.0} · minério {2:0.0} · frutas {3:0.0}",
            pop.Average(b => b.GotWood), pop.Average(b => b.GotStone), pop.Average(b => b.GotOre), pop.Average(b => b.GotFruit)));
        sb.AppendLine($"Regras: janela de coleta {GatherWindow:0}s (madeira {Rw.CollectWood:0} · pedra {Rw.CollectStone:0} · minério {Rw.CollectOre:0} ponto/unidade; só pontua dentro da janela) · extração {Rw.Extract:0} pontos · total {RunSeconds:0}s");
        sb.AppendLine($"Mundo: sem monstros · sem combate · frio constante · {g.Cfg.RadZones} zonas de radiação · recursos finitos que reaparecem após {g.Cfg.NodeRespawnSeconds:0}s · só fogueira, mesa, machado e picareta");
        sb.AppendLine();
        sb.AppendLine("  # slot   pontos extraído t_extr vivo  madeira pedra minério frutas ferram estaç  dist_min");
        for (int k = 0; k < pop.Length; k++)
        {
            var b = pop[k];
            sb.AppendLine(string.Format(inv, "{0,3} {1,4} {2,8:0.0} {3,8} {4,6} {5,4} {6,8} {7,5} {8,7} {9,6} {10,6} {11,5} {12,9}",
                k + 1, b.SlotId, b.Fitness, b.Extracted ? "sim" : "não", b.Extracted ? b.ExtractedAt.ToString("0", inv) + "s" : "-", b.Survived ? "sim" : "não",
                b.GotWood, b.GotStone, b.GotOre, b.GotFruit, b.ToolsMade, b.StationsMade, b.MinExtractDist == float.MaxValue ? "-" : b.MinExtractDist.ToString("0", inv)));
        }
    }

    public override string SummaryHeader => "geracao,segundos_reais,melhor,media,extraidos,madeira_media,pedra_media,minerio_media,frutas_media";
    public override string SummaryRow(GenerationInfo g) => string.Format(CultureInfo.InvariantCulture, "{0},{1:0.0},{2:0.0},{3:0.0},{4},{5:0.0},{6:0.0},{7:0.0},{8:0.0}",
        g.Generation, g.WallSeconds, g.Best, g.Avg, g.Survivors,
        g.Ranked.Average(b => b.GotWood), g.Ranked.Average(b => b.GotStone), g.Ranked.Average(b => b.GotOre), g.Ranked.Average(b => b.GotFruit));

    public override string ConsoleLine(EvolutionaryTrainer t, double seconds)
    {
        var all = t.Population;
        return string.Format(CultureInfo.InvariantCulture, "ger {0,3} | {1}s | melhor {2,8:F1} | média {3,8:F1} | extraídos {4,2}/{5} | madeira {6,5:F0} pedra {7,5:F0} minério {8,5:F0} | {9}",
            t.CurrentGeneration, seconds.ToString("F1", CultureInfo.InvariantCulture), t.BestFitness, t.AvgFitness, t.SurvivorsLastGen, PopulationSize,
            all.Average(b => b.GotWood), all.Average(b => b.GotStone), all.Average(b => b.GotOre), Path.GetFileName(t.LastGenDir));
    }

    // ------------------------------------------------------------------ monitor
    public override float LiveScore(World w, int i) => w.Score[i];

    public override void WriteMonitorExtras(Utf8JsonWriter j, World w)
    {
        j.WriteNumber("win", GatherWindow);
        if (w.ExtractionPos is Vector2 ep) { j.WriteStartObject("ext"); j.WriteNumber("x", (int)ep.X); j.WriteNumber("y", (int)ep.Y); j.WriteNumber("r", w.Cfg.ExtractionRadius); j.WriteEndObject(); }
        j.WriteStartArray("rz");
        foreach (var z in w.RadZones) { j.WriteStartArray(); j.WriteNumberValue((int)z.Pos.X); j.WriteNumberValue((int)z.Pos.Y); j.WriteNumberValue((int)z.R); j.WriteEndArray(); }
        j.WriteEndArray();
    }
}
