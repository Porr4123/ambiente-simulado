using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SimBox.Train;

public sealed class EvolutionaryTrainer
{
    public const int PopulationSize = 64;
    public const int EliteCount = 8;          // os 8 melhores passam (e são salvos em disco)
    public const int TeamSize = 32;           // 32 por time (time = índice % 2)

    public readonly AgentBrain[] Population;
    public int Generation { get; private set; }          // 0-based: próxima geração a rodar
    public int CurrentGeneration { get; private set; }   // 1-based: a que está rodando / acabou de rodar
    public float BestFitness { get; private set; }
    public float AvgFitness { get; private set; }
    public float Team0Score { get; private set; }
    public float Team1Score { get; private set; }
    public int SurvivorsLastGen { get; private set; }
    public float Progress { get; private set; }  // 0..1 durante a simulação
    public int CurrentWeeks { get; private set; }
    public float SimSeconds { get; private set; }
    public World? CurrentWorld { get; private set; }
    public string LastGenDir { get; private set; } = "";
    public readonly List<(int gen, float best, float avg)> History = new();

    /// <summary>Snapshot dos top 8 da última geração concluída (UI não apaga enquanto roda).</summary>
    public AgentBrain[] LastTop8 { get; private set; } = Array.Empty<AgentBrain>();

    /// <summary>Chamado a cada tick da simulação (o monitor decide quando desenhar).</summary>
    public Action<World>? OnFrame;
    /// <summary>Chamado a cada evento importante (morte, kill, ferramenta, doação) — alimenta o painel do monitor.</summary>
    public Action<string>? OnEvent;
    /// <summary>0 = o mais rápido possível; N = N× o tempo real (para acompanhar no monitor).</summary>
    public volatile float Speed;
    /// <summary>Ctrl+S no console: o treino para ao fim da geração em curso.</summary>
    public volatile bool StopRequested;
    /// <summary>true = 1 núcleo para o mundo + 1 núcleo para cada time decidindo em paralelo (<see cref="DecisionPool"/>); false = tudo serial numa thread só.
    /// Por padrão liga com 3+ núcleos lógicos (mundo + 2 times).</summary>
    public bool ParallelTeams = Environment.ProcessorCount >= 3;
    /// <summary>2º Ctrl+S: encerra a geração em curso agora (ela ainda é avaliada e gravada).</summary>
    public volatile bool EndNow;
    public RunLogger? Logger;
    public int Seed { get; }

    readonly Random rng;
    readonly SimConfig? baseCfg;
    const int MaxEventLines = 4000;

    // 1 semana nas primeiras 28 gerações, depois 2 semanas
    public int WeeksForGeneration(int gen) => gen < 28 ? 1 : 2;

    public EvolutionaryTrainer(SimConfig? cfg = null, int? seed = null)
    {
        Seed = seed ?? Random.Shared.Next();
        rng = new Random(Seed);
        baseCfg = cfg;
        Population = new AgentBrain[PopulationSize];
        for (int i = 0; i < PopulationSize; i++)
            Population[i] = new AgentBrain(team: i % 2, rng: rng);
    }

    /// <summary>Retoma de cérebros salvos: eles viram a elite; o resto nasce de cruzamento + mutação deles.</summary>
    public void Resume(IReadOnlyList<AgentBrain> saved, int lastGenNumber)
    {
        for (int i = 0; i < PopulationSize; i++)
        {
            if (i < saved.Count) Population[i] = new AgentBrain(saved[i].Net.Clone(), i % 2);
            else
            {
                var a = saved[rng.Next(saved.Count)]; var b = saved[rng.Next(saved.Count)];
                var net = NeuralNetwork.Crossover(a.Net, b.Net, rng); net.Mutate(0.08f, 0.2f, rng);
                Population[i] = new AgentBrain(net, i % 2);
            }
        }
        Generation = lastGenNumber; CurrentGeneration = lastGenNumber;
    }

    static SimConfig MakeConfig() => new()
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
        TeamWall = true                 // muro vertical no meio separa os times; some nos 2 últimos dias (WallOpenTime, definido em RunGeneration)
    };

    /// <summary>
    /// Roda uma geração completa: simula → avalia → registra → seleciona → reproduz.
    /// onProgress é chamado periodicamente com valor 0..1 (pode atualizar a UI).
    /// </summary>
    public void RunGeneration(Action<float>? onProgress = null)
    {
        var wall = Stopwatch.StartNew();
        CurrentGeneration = Generation + 1;
        int weeks = CurrentWeeks = WeeksForGeneration(Generation);
        var cfg = MakeConfig();
        float simSeconds = SimSeconds = weeks * 7 * cfg.Cycle;
        cfg.WallOpenTime = simSeconds - 2 * cfg.Cycle;      // muro cai no começo dos 2 últimos dias
        int worldSeed = rng.Next();

        var world = new World(cfg, PopulationSize, worldSeed);
        for (int i = 0; i < PopulationSize; i++)
        {
            world.Agents[i].Team = Population[i].Team;
            Population[i].ResetStats(); Population[i].SlotId = i;
        }
        world.Reset(worldSeed);             // de novo, agora com os times definidos (spawns por time)
        world.EmitEvents = false;
        world.AccumulateRewards = true;     // reward só zera na próxima geração
        CurrentWorld = world;

        var events = new List<string>();
        world.Trace = line =>
        {
            if (events.Count < MaxEventLines) events.Add(line);
            else if (events.Count == MaxEventLines) events.Add("... (log de eventos truncado)");
            OnEvent?.Invoke(line);
        };

        var inputs = new AgentInput[PopulationSize];
        DecisionPool? pool = null;
        if (ParallelTeams)
        {
            var byTeam = new[] { new List<int>(), new List<int>() };
            for (int i = 0; i < PopulationSize; i++) byTeam[world.Agents[i].Team & 1].Add(i);
            pool = new DecisionPool(byTeam.Select(l => l.ToArray()).ToArray(), i => inputs[i] = Population[i].Decide(world, world.Agents[i]));
        }
        try {
        int totalTicks = (int)(simSeconds * cfg.Tps);
        int decideEvery = 2;                // rede só pensa a cada 2 ticks
        int progressEvery = Math.Max(1, totalTicks / 50);
        Progress = 0f;
        var pace = Stopwatch.StartNew(); float lastSpeed = Speed; int paceTick = 0;

        for (int tick = 0; tick < totalTicks; tick++)
        {
            if (tick % decideEvery == 0)
            {
                if (pool != null) pool.Run();        // time 0 e time 1 decidem em paralelo (mundo parado, só leitura)
                else
                    for (int i = 0; i < PopulationSize; i++)
                        inputs[i] = Population[i].Decide(world, world.Agents[i]);
            }

            world.Step(inputs);
            // comandos (fabricar, soltar, comer...) valem para UM tick; senão a pausa entre decisões os repetia
            for (int i = 0; i < PopulationSize; i++) inputs[i].Cmd = Cmd.None;

            OnFrame?.Invoke(world);

            if (tick % progressEvery == 0)
            {
                Progress = (float)tick / totalTicks;
                onProgress?.Invoke(Progress);
            }

            float speed = Speed;
            if (speed != lastSpeed) { lastSpeed = speed; paceTick = tick; pace.Restart(); }     // mudou a velocidade (botão slow): refaz a referência, senão tentaria "compensar" o tempo passado
            if (speed > 0 && (speed <= 2f || tick % 4 == 0))                                      // em câmera lenta dorme a cada tick (movimento fluido no monitor)
            {
                double ahead = (tick + 1 - paceTick) / (cfg.Tps * (double)speed) - pace.Elapsed.TotalSeconds;
                if (ahead > 0.002) Thread.Sleep((int)(ahead * 1000));
            }

            if (world.Done || EndNow) break;
        }
        } finally { pool?.Dispose(); }

        Progress = 1f;
        onProgress?.Invoke(1f);

        // --- Avaliação ---
        // fitness = reward acumulado na geração (combate/times/ferramentas/coleta/...) + sobreviver(+100)/morrer(-50) + 2·nível
        float team0 = 0, team1 = 0;
        int survivors = 0;

        for (int i = 0; i < PopulationSize; i++)
        {
            var brain = Population[i];
            var agent = world.Agents[i];
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

            float fitness = brain.RawReward + (survived ? 100f : -50f) + agent.Level * 2f;
            brain.Fitness = fitness;

            if (brain.Team == 0) team0 += fitness;
            else team1 += fitness;
        }

        SurvivorsLastGen = survivors;
        Team0Score = team0;
        Team1Score = team1;
        BestFitness = Population.Max(b => b.Fitness);
        AvgFitness = Population.Average(b => b.Fitness);
        History.Add((CurrentGeneration, BestFitness, AvgFitness));

        var ranked = Population.OrderByDescending(b => b.Fitness).ToArray();
        LastTop8 = ranked.Take(EliteCount).Select(b => b.Clone()).ToArray();

        // --- Registro em pasta (log.txt + top 8 salvos) ---
        if (Logger != null)
        {
            try
            {
                LastGenDir = Logger.SaveGeneration(CurrentGeneration, BuildLog(world, cfg, weeks, worldSeed, wall.Elapsed.TotalSeconds, ranked, events), LastTop8);
                Logger.AppendSummary(CurrentGeneration, weeks, wall.Elapsed.TotalSeconds, BestFitness, AvgFitness, survivors, team0, team1,
                    Population.Sum(b => b.EnemyKills), Population.Sum(b => b.FriendKills));
            }
            catch (Exception ex) { OnEvent?.Invoke($"[aviso] não consegui gravar o registro: {ex.Message}"); }
        }

        OnFrame?.Invoke(world);
        Evolve();
        Generation++;
    }

    string BuildLog(World w, SimConfig cfg, int weeks, int worldSeed, double wallSec, AgentBrain[] ranked, List<string> events)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"=== Geração {CurrentGeneration} === {weeks} semana(s) · seed do mundo {worldSeed} · seed do treino {Seed}");
        sb.AppendLine($"Simulado: {w.Time:0}s de {SimSeconds:0}s (dia {w.DayNumber}){(w.Done ? " — todos morreram, geração encerrada antes" : EndNow ? " — geração encerrada antes (Ctrl+S)" : "")} · tempo real: {wallSec:0.0}s");
        sb.AppendLine(string.Format(inv, "Melhor fitness {0:0.0} · média {1:0.0} · sobreviventes {2}/{3}", BestFitness, AvgFitness, SurvivorsLastGen, PopulationSize));
        sb.AppendLine(string.Format(inv, "Time 0 (azul): {0:0.0}   Time 1 (vermelho): {1:0.0}", Team0Score, Team1Score));
        sb.AppendLine($"Ghouls abatidos: {w.KillsByKind.Sum()} (por tipo: {string.Join("/", w.KillsByKind)}) · mortes: {w.Deaths}");
        sb.AppendLine("Regras: fitness = reward + 100 (vivo no fim) / -50 (morto) + 2·nível · mortos só renascem na próxima geração");
        sb.AppendLine($"Muro entre os times: até {cfg.WallOpenTime:0}s (cai nos 2 últimos dias) · refBoa = comeu sem desperdício (bônus), refDesp = desperdiçou/comida que fere");
        sb.AppendLine();
        sb.AppendLine("  # slot time  fitness  reward vivo  nív ghouls  killsInim killsAmigo  danoInim danoAmigo ferram doações estaç refBoa refDesp");
        for (int k = 0; k < ranked.Length; k++)
        {
            var b = ranked[k];
            sb.AppendLine(string.Format(inv, "{0,3} {1,4}  T{2} {3,9:0.0} {4,8:0.0} {5,4} {6,4} {7,6} {8,10} {9,10} {10,9:0} {11,8:0} {12,6} {13,6} {14,5} {15,6} {16,7}",
                k + 1, b.SlotId, b.Team, b.Fitness, b.RawReward, b.Survived ? "sim" : "não", b.FinalLevel, b.FinalKills, b.EnemyKills, b.FriendKills, b.EnemyDmg, b.FriendDmg, b.ToolsMade, b.Donations, b.StationsMade, b.GoodMeals, b.WastedMeals));
        }
        sb.AppendLine();
        sb.AppendLine($"--- Eventos ({events.Count}) ---");
        foreach (var e in events) sb.AppendLine(e);
        return sb.ToString();
    }

    void Evolve()
    {
        var ranked = Population.OrderByDescending(b => b.Fitness).ToArray();
        var next = new AgentBrain[PopulationSize];

        for (int i = 0; i < EliteCount; i++)
            next[i] = ranked[i].Clone();

        for (int i = EliteCount; i < PopulationSize; i++)
        {
            var parentA = ranked[rng.Next(EliteCount)];
            var parentB = ranked[rng.Next(EliteCount)];
            var childNet = NeuralNetwork.Crossover(parentA.Net, parentB.Net, rng);

            float mutRate = Generation < 15 ? 0.14f : Generation < 40 ? 0.10f : 0.06f;
            float mutStr = Generation < 15 ? 0.35f : 0.22f;
            childNet.Mutate(mutRate, mutStr, rng);

            next[i] = new AgentBrain(childNet, 0);
        }

        // Times SEMPRE equilibrados 32×32 (antes, as elites mantinham o time antigo e os times saíam desiguais).
        // Índice par → time 0, ímpar → time 1; as elites ficam intercaladas entre os dois.
        for (int i = 0; i < PopulationSize; i++)
        {
            next[i].Team = i % 2;
            next[i].ResetStats();
            Population[i] = next[i];
        }
    }

    public AgentBrain[] GetTop(int n = 8)
    {
        return Population.OrderByDescending(b => b.Fitness).Take(n).ToArray();
    }
}
