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

    readonly Random rng = new();
    readonly SimConfig baseCfg;

    // Configuração de tempo
    public int WeeksForGeneration(int gen) => gen < 28 ? 1 : 2;

    public EvolutionaryTrainer(SimConfig? cfg = null)
    {
        baseCfg = cfg ?? new SimConfig();
        Population = new AgentBrain[PopulationSize];

        for (int i = 0; i < PopulationSize; i++)
        {
            Population[i] = new AgentBrain(team: i < TeamSize ? 0 : 1);
        }
    }

    /// <summary>
    /// Roda uma geração completa: simula → avalia → seleciona → reproduz
    /// </summary>
    public void RunGeneration()
    {
        int weeks = WeeksForGeneration(Generation);
        float simSeconds = weeks * 7 * 960f; // 1 ciclo = 960s

        // --- Simulação ---
        // Usa TPS alto para acelerar o treino (não precisa ser em tempo real)
        var cfg = new SimConfig
        {
            DayLen = baseCfg.DayLen,
            NightLen = baseCfg.NightLen,
            CraftTimeScale = 0.4f,          // crafts mais rápidos no treino
            Tps = 30,
            WorldW = baseCfg.WorldW,
            WorldH = baseCfg.WorldH
        };

        var world = new World(cfg, PopulationSize, rng.Next());
        world.EmitEvents = false;          // menos overhead

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

        for (int tick = 0; tick < totalTicks; tick++)
        {
            for (int i = 0; i < PopulationSize; i++)
                inputs[i] = Population[i].Decide(world, world.Agents[i]);

            world.Step(inputs);

            if (world.Done) break;
        }

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

            // Fitness final
            float fitness = reward;
            if (survived) fitness += 100f;   // bônus de sobrevivência
            else fitness -= 50f;             // ônus de morte

            // Pequeno bônus por nível / kills (incentiva progressão)
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

        // --- Seleção + Reprodução ---
        Evolve();

        Generation++;
    }

    void Evolve()
    {
        // Ordena por fitness (melhor primeiro)
        var ranked = Population.OrderByDescending(b => b.Fitness).ToArray();

        // Elites (top 8) passam quase intactos
        var next = new AgentBrain[PopulationSize];
        for (int i = 0; i < EliteCount; i++)
        {
            next[i] = ranked[i].Clone();
            // mantém o time original do elite
        }

        // Resto: crossover + mutação a partir dos elites
        for (int i = EliteCount; i < PopulationSize; i++)
        {
            var parentA = ranked[rng.Next(EliteCount)];
            var parentB = ranked[rng.Next(EliteCount)];

            var childNet = NeuralNetwork.Crossover(parentA.Net, parentB.Net, rng);

            // Mutação adaptativa
            float mutRate = Generation < 15 ? 0.14f : Generation < 40 ? 0.10f : 0.06f;
            float mutStr = Generation < 15 ? 0.35f : 0.22f;
            childNet.Mutate(mutRate, mutStr, rng);

            // Distribui times equilibrados
            int team = i < TeamSize ? 0 : 1;
            next[i] = new AgentBrain(childNet, team);
        }

        // Copia de volta
        for (int i = 0; i < PopulationSize; i++)
            Population[i] = next[i];
    }

    public AgentBrain[] GetTop(int n = 8)
    {
        return Population.OrderByDescending(b => b.Fitness).Take(n).ToArray();
    }
}
