namespace SimBox.Train;

public sealed class EvolutionaryTrainer
{
    public const int PopulationSize = 32;
    public const int EliteCount = 8;          // os 8 melhores passam
    public const int TeamSize = 16;           // 16 por time

    public readonly AgentBrain[] Population;
    public int Generation { get; private set; }
    public float BestFitness { get; private set; }
    public float AvgFitness { get; private set; }
    public float Team0Score { get; private set; }
    public float Team1Score { get; private set; }
    public int SurvivorsLastGen { get; private set; }
    public float Progress { get; private set; }  // 0..1 durante a simulação

    /// <summary>Snapshot dos top 8 da última geração concluída (UI não apaga enquanto roda).</summary>
    public AgentBrain[] LastTop8 { get; private set; } = Array.Empty<AgentBrain>();

    readonly Random rng = new();
    readonly SimConfig baseCfg;

    // 1 semana nas primeiras 28 gerações, depois 2 semanas
    public int WeeksForGeneration(int gen) => gen < 28 ? 1 : 2;

    public EvolutionaryTrainer(SimConfig? cfg = null)
    {
        baseCfg = cfg ?? new SimConfig();
        Population = new AgentBrain[PopulationSize];

        for (int i = 0; i < PopulationSize; i++)
            Population[i] = new AgentBrain(team: i < TeamSize ? 0 : 1);
    }

    /// <summary>
    /// Roda uma geração completa: simula → avalia → seleciona → reproduz.
    /// onProgress é chamado periodicamente com valor 0..1 (pode atualizar a UI).
    /// </summary>
    public void RunGeneration(Action<float>? onProgress = null)
    {
        int weeks = WeeksForGeneration(Generation);

        // Tempo COMPRIMIDO no treino: dia/noite curtos, mas ainda 7 ciclos = 1 "semana"
        // Padrão wiki = 480+480=960s/ciclo. Aqui usamos 90+90=180s/ciclo (~5x mais rápido).
        const float trainDay = 90f;
        const float trainNight = 90f;
        float cycle = trainDay + trainNight;
        float simSeconds = weeks * 7 * cycle;

        var cfg = new SimConfig
        {
            DayLen = trainDay,
            NightLen = trainNight,
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
            Animals = 25
        };

        var world = new World(cfg, PopulationSize, rng.Next());
        world.EmitEvents = false;

        for (int i = 0; i < PopulationSize; i++)
        {
            world.Agents[i].Team = Population[i].Team;
            Population[i].Fitness = 0;
            Population[i].Survived = false;
            Population[i].Deaths = 0;
            Population[i].RawReward = 0;
        }

        var inputs = new AgentInput[PopulationSize];
        int totalTicks = (int)(simSeconds * cfg.Tps);
        int decideEvery = 2;                // rede só pensa a cada 2 ticks
        int progressEvery = Math.Max(1, totalTicks / 50);

        Progress = 0f;

        for (int tick = 0; tick < totalTicks; tick++)
        {
            if (tick % decideEvery == 0)
            {
                for (int i = 0; i < PopulationSize; i++)
                    inputs[i] = Population[i].Decide(world, world.Agents[i]);
            }

            world.Step(inputs);

            if (tick % progressEvery == 0)
            {
                Progress = (float)tick / totalTicks;
                onProgress?.Invoke(Progress);
            }

            if (world.Done) break;
        }

        Progress = 1f;
        onProgress?.Invoke(1f);

        // --- Avaliação ---
        float team0 = 0, team1 = 0;
        int survivors = 0;

        for (int i = 0; i < PopulationSize; i++)
        {
            var brain = Population[i];
            var agent = world.Agents[i];
            float reward = world.Rewards[i];
            brain.RawReward = reward;
            brain.FinalLevel = agent.Level;
            brain.FinalKills = agent.Kills;

            bool survived = agent.Alive;
            brain.Survived = survived;
            if (survived) survivors++;

            float fitness = reward;
            if (survived) fitness += 100f;
            else fitness -= 50f;

            fitness += agent.Level * 2f;
            fitness += agent.Kills * 3f;

            brain.Fitness = fitness;

            if (brain.Team == 0) team0 += fitness;
            else team1 += fitness;
        }

        SurvivorsLastGen = survivors;
        Team0Score = team0;
        Team1Score = team1;
        BestFitness = Population.Max(b => b.Fitness);
        AvgFitness = Population.Average(b => b.Fitness);

        LastTop8 = Population.OrderByDescending(b => b.Fitness).Take(8)
            .Select(b => b.Clone()).ToArray();

        Evolve();
        Generation++;
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

            int team = i < TeamSize ? 0 : 1;
            next[i] = new AgentBrain(childNet, team);
        }

        for (int i = 0; i < PopulationSize; i++)
            Population[i] = next[i];
    }

    public AgentBrain[] GetTop(int n = 8)
    {
        return Population.OrderByDescending(b => b.Fitness).Take(n).ToArray();
    }
}
