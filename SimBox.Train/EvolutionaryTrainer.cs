using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SimBox.Train;

public sealed class EvolutionaryTrainer
{
    /// <summary>O que está sendo treinado (mundo, regras, pontuação, registro). Ver <see cref="TrainingModel"/>.</summary>
    public readonly TrainingModel Model;
    public int PopulationSize => Model.PopulationSize;
    public int EliteCount => Model.EliteCount;          // os melhores passam (e são salvos em disco)

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
    const int MaxEventLines = 4000;

    public int WeeksForGeneration(int gen) => Model.WeeksFor(gen);

    public EvolutionaryTrainer(TrainingModel? model = null, int? seed = null)
    {
        Model = model ?? new CollectModel();
        Seed = seed ?? Random.Shared.Next();
        rng = new Random(Seed);
        Population = new AgentBrain[PopulationSize];
        for (int i = 0; i < PopulationSize; i++)
            Population[i] = new AgentBrain(team: Model.TeamOf(i), rng: rng, inputs: Model.InputSize);
    }

    /// <summary>Retoma de cérebros salvos: eles viram a elite; o resto nasce de cruzamento + mutação deles.</summary>
    public void Resume(IReadOnlyList<AgentBrain> saved, int lastGenNumber)
    {
        for (int i = 0; i < PopulationSize; i++)
        {
            if (i < saved.Count) Population[i] = new AgentBrain(saved[i].Net.Clone(), Model.TeamOf(i));
            else
            {
                var a = saved[rng.Next(saved.Count)]; var b = saved[rng.Next(saved.Count)];
                var net = NeuralNetwork.Crossover(a.Net, b.Net, rng); net.Mutate(0.08f, 0.2f, rng);
                Population[i] = new AgentBrain(net, Model.TeamOf(i));
            }
        }
        Generation = lastGenNumber; CurrentGeneration = lastGenNumber;
    }

    /// <summary>
    /// Roda uma geração completa: simula → avalia → registra → seleciona → reproduz.
    /// onProgress é chamado periodicamente com valor 0..1 (pode atualizar a UI).
    /// </summary>
    public void RunGeneration(Action<float>? onProgress = null)
    {
        var wall = Stopwatch.StartNew();
        CurrentGeneration = Generation + 1;
        int weeks = CurrentWeeks = Model.WeeksFor(Generation);
        var cfg = Model.MakeConfig();
        float simSeconds = SimSeconds = Model.PrepareGeneration(cfg, Generation);
        int worldSeed = rng.Next();

        var world = new World(cfg, PopulationSize, worldSeed);
        for (int i = 0; i < PopulationSize; i++)
        {
            world.Agents[i].Team = Model.TeamOf(i); Population[i].Team = Model.TeamOf(i);
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
            var byGroup = new[] { new List<int>(), new List<int>() };
            for (int i = 0; i < PopulationSize; i++) byGroup[Model.GroupOf(i) & 1].Add(i);
            pool = new DecisionPool(byGroup.Select(l => l.ToArray()).ToArray(), i => inputs[i] = Population[i].Decide(world, world.Agents[i], Model));
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
                        inputs[i] = Population[i].Decide(world, world.Agents[i], Model);
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

        // --- Avaliação (o modelo define o fitness) ---
        var stats = Model.Evaluate(world, Population);
        int survivors = stats.Survivors; float team0 = stats.Team0, team1 = stats.Team1;

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
                var info = new GenerationInfo
                {
                    Generation = CurrentGeneration, WorldSeed = worldSeed, TrainSeed = Seed, Weeks = weeks, Survivors = survivors, WallSeconds = wall.Elapsed.TotalSeconds,
                    SimSeconds = simSeconds, Best = BestFitness, Avg = AvgFitness, Team0 = team0, Team1 = team1, World = world, Cfg = cfg, Ranked = ranked
                };
                LastGenDir = Logger.SaveGeneration(CurrentGeneration, BuildLog(info, events), LastTop8);
                Logger.AppendSummary(Model.SummaryHeader, Model.SummaryRow(info));
            }
            catch (Exception ex) { OnEvent?.Invoke($"[aviso] não consegui gravar o registro: {ex.Message}"); }
        }

        OnFrame?.Invoke(world);
        Evolve();
        Generation++;
    }

    string BuildLog(GenerationInfo g, List<string> events)
    {
        var w = g.World; var sb = new StringBuilder();
        sb.AppendLine($"=== Geração {CurrentGeneration} === modelo {Model.Id} · {Model.GenerationTitle(Generation)} · seed do mundo {g.WorldSeed} · seed do treino {Seed}");
        sb.AppendLine($"Simulado: {w.Time:0}s de {SimSeconds:0}s (dia {w.DayNumber}){(w.Done ? " — " + Model.DoneNote : EndNow ? " — geração encerrada antes (Ctrl+S)" : "")} · tempo real: {g.WallSeconds:0.0}s");
        Model.WriteLogBody(sb, g);
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

            next[i] = new AgentBrain(childNet, Model.TeamOf(i));
        }

        // Times SEMPRE equilibrados (no modelo de times: índice par → time 0, ímpar → time 1; as elites ficam intercaladas entre os dois).
        for (int i = 0; i < PopulationSize; i++)
        {
            next[i].Team = Model.TeamOf(i);
            next[i].ResetStats();
            Population[i] = next[i];
        }
    }

    public AgentBrain[] GetTop(int n = 8)
    {
        return Population.OrderByDescending(b => b.Fitness).Take(n).ToArray();
    }
}
