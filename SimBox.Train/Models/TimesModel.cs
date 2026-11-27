using System.Globalization;
using System.Text;

namespace SimBox.Train;

/// <summary>Modelo "times": 2 times de 32 no mesmo mundo, separados por um muro até os 2 últimos dias; combate, ferramentas, estações, comer.</summary>
public sealed class TimesModel : TrainingModel
{
    public const int TeamSize = 32;                       // 32 por time (time = índice % 2)
    public override string Id => "times";
    public override string Title => "2 Times · combate";
    public override int PopulationSize => TeamSize * 2;
    public override int TeamOf(int slot) => slot & 1;     // índice par → time 0, ímpar → time 1; as elites ficam intercaladas
    public override int GroupOf(int slot) => slot & 1;    // 1 núcleo por time
    public override int ExtraFeatures => 0;
    public override void FillExtraFeatures(World w, Agent a, float[] f, ref int i) { }

    public override SimConfig MakeConfig() => new()
    {
        // Tempo COMPRIMIDO no treino: dia/noite curtos, mas ainda 7 ciclos = 1 "semana"
        // Padrão wiki = 480+480=960s/ciclo. Aqui usamos 90+90=180s/ciclo (~5x mais rápido).
        DayLen = 90f,
        NightLen = 90f,
        CraftTimeScale = 0.35f,
        Tps = 20,                       // 20 TPS no treino (mais rápido que 30)
        WorldW = 4500,                  // mapa um pouco menor
        WorldH = 4500,
        Trees = 500,
        Rocks = 180,
        IronNodes = 80,
        SulfurNodes = 40,
        Crates = 30,
        Bushes = 60,
        Animals = 25,
        TeamSpawns = true,              // time 0 nasce à esquerda, time 1 à direita
        TeamWall = true                 // muro vertical no meio separa os times; some nos 2 últimos dias (WallOpenTime, definido em PrepareGeneration)
    };

    // 1 semana nas primeiras 28 gerações, depois 2 semanas
    public override int WeeksFor(int generation) => generation < 28 ? 1 : 2;
    public override string GenerationTitle(int generation) => $"{WeeksFor(generation)} semana(s)";

    public override float PrepareGeneration(SimConfig cfg, int generation)
    {
        float simSeconds = WeeksFor(generation) * 7 * cfg.Cycle;
        cfg.WallOpenTime = simSeconds - 2 * cfg.Cycle;      // muro cai no começo dos 2 últimos dias
        return simSeconds;
    }

    public override string DoneNote => "todos morreram, geração encerrada antes";

    public override GenerationStats Evaluate(World world, AgentBrain[] pop)
    {
        // fitness = reward acumulado na geração (combate/times/ferramentas/coleta/...) + sobreviver(+100)/morrer(-50) + 2·nível
        float team0 = 0, team1 = 0; int survivors = 0;
        for (int i = 0; i < pop.Length; i++)
        {
            var brain = pop[i]; var agent = world.Agents[i];
            brain.RawReward = world.Rewards[i];
            brain.FinalLevel = agent.Level;
            brain.FinalKills = agent.Kills;
            brain.EnemyKills = agent.EnemyKills; brain.FriendKills = agent.FriendKills;
            brain.EnemyDmg = agent.EnemyDmg; brain.FriendDmg = agent.FriendDmg;
            brain.ToolsMade = agent.ToolsMade; brain.Donations = agent.Donations;
            brain.StationsMade = agent.StationsMade; brain.GoodMeals = agent.GoodMeals; brain.WastedMeals = agent.WastedMeals;

            bool survived = agent.Alive;
            brain.Survived = survived;
            if (survived) survivors++;

            brain.Fitness = brain.RawReward + (survived ? 100f : -50f) + agent.Level * 2f;
            if (brain.Team == 0) team0 += brain.Fitness; else team1 += brain.Fitness;
        }
        return new GenerationStats(survivors, team0, team1);
    }

    public override void WriteLogBody(StringBuilder sb, GenerationInfo g)
    {
        var inv = CultureInfo.InvariantCulture;
        sb.AppendLine(string.Format(inv, "Melhor fitness {0:0.0} · média {1:0.0} · sobreviventes {2}/{3}", g.Best, g.Avg, g.Survivors, PopulationSize));
        sb.AppendLine(string.Format(inv, "Time 0 (azul): {0:0.0}   Time 1 (vermelho): {1:0.0}", g.Team0, g.Team1));
        sb.AppendLine($"Ghouls abatidos: {g.World.KillsByKind.Sum()} (por tipo: {string.Join("/", g.World.KillsByKind)}) · mortes: {g.World.Deaths}");
        sb.AppendLine("Regras: fitness = reward + 100 (vivo no fim) / -50 (morto) + 2·nível · mortos só renascem na próxima geração");
        sb.AppendLine($"Muro entre os times: até {g.Cfg.WallOpenTime:0}s (cai nos 2 últimos dias) · refBoa = comeu sem desperdício (bônus), refDesp = desperdiçou/comida que fere");
        sb.AppendLine();
        sb.AppendLine("  # slot time  fitness  reward vivo  nív ghouls  killsInim killsAmigo  danoInim danoAmigo ferram doações estaç refBoa refDesp");
        for (int k = 0; k < g.Ranked.Length; k++)
        {
            var b = g.Ranked[k];
            sb.AppendLine(string.Format(inv, "{0,3} {1,4}  T{2} {3,9:0.0} {4,8:0.0} {5,4} {6,4} {7,6} {8,10} {9,10} {10,9:0} {11,8:0} {12,6} {13,6} {14,5} {15,6} {16,7}",
                k + 1, b.SlotId, b.Team, b.Fitness, b.RawReward, b.Survived ? "sim" : "não", b.FinalLevel, b.FinalKills, b.EnemyKills, b.FriendKills, b.EnemyDmg, b.FriendDmg, b.ToolsMade, b.Donations, b.StationsMade, b.GoodMeals, b.WastedMeals));
        }
    }

    public override string SummaryHeader => "geracao,semanas,segundos_reais,melhor,media,sobreviventes,time0,time1,kills_inimigo,kills_amigo";
    public override string SummaryRow(GenerationInfo g) => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:0.0},{3:0.0},{4:0.0},{5},{6:0.0},{7:0.0},{8},{9}",
        g.Generation, g.Weeks, g.WallSeconds, g.Best, g.Avg, g.Survivors, g.Team0, g.Team1, g.Ranked.Sum(b => b.EnemyKills), g.Ranked.Sum(b => b.FriendKills));

    public override string ConsoleLine(EvolutionaryTrainer t, double seconds) => string.Format(CultureInfo.InvariantCulture,
        "ger {0,3} | {1}s | melhor {2,8:F1} | média {3,8:F1} | vivos {4,2}/{5} | T0 {6,8:F1} T1 {7,8:F1} | inimigos {8} amigos {9} | {10}",
        t.CurrentGeneration, seconds.ToString("F1", CultureInfo.InvariantCulture), t.BestFitness, t.AvgFitness, t.SurvivorsLastGen, PopulationSize, t.Team0Score, t.Team1Score,
        t.LastTop8.Sum(b => b.EnemyKills), t.LastTop8.Sum(b => b.FriendKills), Path.GetFileName(t.LastGenDir));
}
