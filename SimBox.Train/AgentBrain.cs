namespace SimBox.Train;

/// <summary>
/// Cérebro do agente: Entrada → Rede → Saída (AgentInput)
/// </summary>
public sealed class AgentBrain
{
    public NeuralNetwork Net { get; }
    public int Team { get; set; }          // 0 ou 1
    public float Fitness { get; set; }
    public bool Survived { get; set; }
    public int Deaths { get; set; }
    public float RawReward { get; set; }   // reward acumulado na geração (World.Rewards)
    public int FinalLevel { get; set; }
    public int FinalKills { get; set; }    // ghouls abatidos
    public int EnemyKills { get; set; }    // agentes do time rival abatidos
    public int FriendKills { get; set; }   // colegas de time abatidos (ruim)
    public float EnemyDmg { get; set; }
    public float FriendDmg { get; set; }
    public int ToolsMade { get; set; }
    public int Donations { get; set; }
    public int StationsMade { get; set; }  // estações de trabalho fabricadas
    public int GoodMeals { get; set; }     // comeu/curou sem desperdiçar
    public int WastedMeals { get; set; }   // comeu desperdiçando (ou comida que fere)
    public int SlotId { get; set; }        // índice do agente no mundo (só para registro)

    /// <summary>Última decisão da rede (para o monitor de foco).</summary>
    public AgentInput LastInput { get; private set; }

    // Saídas da rede (ordem fixa)
    // 0: Mx, 1: My, 2: Aim (ângulo), 3: Sprint, 4: Attack
    // 5-14: logits dos Cmd (10 comandos)
    // 15: Arg (normalizado → int)
    public const int OutputSize = 16;

    static readonly Cmd[] CmdOrder =
    {
        Cmd.None, Cmd.Select, Cmd.Craft, Cmd.Research, Cmd.Interact,
        Cmd.Store, Cmd.Take, Cmd.Drop, Cmd.Reload, Cmd.Eat
    };

    public AgentBrain(NeuralNetwork? net = null, int team = 0, Random? rng = null)
    {
        Net = net ?? new NeuralNetwork(new[]
        {
            FeatureExtractor.FeatureCount,  // entrada
            80,                             // camada 1
            48,                             // camada 2
            32,                             // camada 3
            OutputSize                      // saída
        }, rng);
        Team = team;
    }

    /// <summary>Cópia da rede + todas as estatísticas (antes o Clone perdia nível/kills e o painel mostrava 0).</summary>
    public AgentBrain Clone() => new(Net.Clone(), Team)
    {
        Fitness = Fitness, Survived = Survived, Deaths = Deaths, RawReward = RawReward,
        FinalLevel = FinalLevel, FinalKills = FinalKills, EnemyKills = EnemyKills, FriendKills = FriendKills,
        EnemyDmg = EnemyDmg, FriendDmg = FriendDmg, ToolsMade = ToolsMade, Donations = Donations, StationsMade = StationsMade, GoodMeals = GoodMeals, WastedMeals = WastedMeals, SlotId = SlotId
    };

    public void ResetStats()
    {
        Fitness = RawReward = EnemyDmg = FriendDmg = 0;
        Survived = false; Deaths = FinalLevel = FinalKills = EnemyKills = FriendKills = ToolsMade = Donations = StationsMade = GoodMeals = WastedMeals = 0;
        LastInput = default;
    }

    // ---- arquivo .brain: "SBRN", versão, time, fitness, rede
    public void Save(string path)
    {
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write(new[] { (byte)'S', (byte)'B', (byte)'R', (byte)'N' });
        bw.Write(1); bw.Write(Team); bw.Write(Fitness);
        Net.Write(bw);
    }

    public static AgentBrain Load(string path)
    {
        using var br = new BinaryReader(File.OpenRead(path));
        var magic = br.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != 'S' || magic[1] != 'B' || magic[2] != 'R' || magic[3] != 'N') throw new InvalidDataException($"{path}: não é um arquivo .brain");
        if (br.ReadInt32() != 1) throw new InvalidDataException($"{path}: versão desconhecida");
        int team = br.ReadInt32(); float fit = br.ReadSingle();
        var net = NeuralNetwork.Read(br);
        if (net.Layers[0] == FeatureExtractor.LegacyFeatureCount && FeatureExtractor.FeatureCount > net.Layers[0]) net = net.WithInputSize(FeatureExtractor.FeatureCount);   // cérebro da versão sem obstáculos: migra
        if (net.Layers[0] != FeatureExtractor.FeatureCount || net.Layers[^1] != OutputSize)
            throw new InvalidDataException($"{path}: rede com {net.Layers[0]} entradas, mas as features atuais são {FeatureExtractor.FeatureCount} (cérebro de versão antiga)");
        return new AgentBrain(net, team) { Fitness = fit };
    }

    public AgentInput Decide(World w, Agent a)
    {
        // Morto fica morto até a próxima geração: nada de Respawn.
        if (!a.Alive) return LastInput = new AgentInput { Cmd = Cmd.None };

        var features = FeatureExtractor.Extract(w, a);
        var outp = Net.Forward(features);

        // Movimento contínuo
        float mx = Math.Clamp(outp[0], -1f, 1f);
        float my = Math.Clamp(outp[1], -1f, 1f);

        // Aim: saída linear → ângulo
        float aim = outp[2] * MathF.PI; // -π .. π aproximadamente

        bool sprint = outp[3] > 0.3f;
        bool attack = outp[4] > 0.4f;

        // Escolhe comando pelo maior logit
        int bestCmdIdx = 0;
        float bestVal = outp[5];
        for (int i = 1; i < CmdOrder.Length; i++)
        {
            if (outp[5 + i] > bestVal)
            {
                bestVal = outp[5 + i];
                bestCmdIdx = i;
            }
        }

        // Só emite comando se a ativação for razoável (evita spam)
        Cmd cmd = bestVal > 0.25f ? CmdOrder[bestCmdIdx] : Cmd.None;

        // Arg: depende do comando (-1 = não há nada válido para escolher → sem comando)
        int arg = 0;
        float argRaw = outp[15];
        switch (cmd)
        {
            case Cmd.Select:
                arg = Math.Clamp((int)((argRaw + 1f) * 0.5f * a.Slots.Length), 0, a.Slots.Length - 1);
                break;
            case Cmd.Craft:
                arg = PickCraftRecipe(w, a, argRaw);
                break;
            case Cmd.Research:
                arg = PickResearch(w, a, argRaw);
                break;
            case Cmd.Eat:
                arg = PickFood(a, argRaw);
                break;
        }
        if (arg < 0) { cmd = Cmd.None; arg = 0; }

        return LastInput = new AgentInput
        {
            Mx = mx,
            My = my,
            Aim = aim,
            AimDist = 200f,
            Sprint = sprint,
            Attack = attack,
            Cmd = cmd,
            Arg = arg
        };
    }

    static int PickCraftRecipe(World w, Agent a, float raw)
    {
        var candidates = new List<int>();
        for (int r = 0; r < Defs.Recipes.Length; r++)
        {
            if (Defs.Recipes[r].BotOk && w.CraftCheck(a, r, out _) == null)
                candidates.Add(r);
        }
        if (candidates.Count == 0) return -1;
        int idx = Math.Clamp((int)((raw + 1f) * 0.5f * candidates.Count), 0, candidates.Count - 1);
        return candidates[idx];
    }

    static int PickResearch(World w, Agent a, float raw)
    {
        var candidates = new List<int>();
        for (int k = 0; k < Defs.ItemCount; k++)
            if (w.ResearchCheck(a, (Item)k) == null)
                candidates.Add(k);
        if (candidates.Count == 0) return -1;
        int idx = Math.Clamp((int)((raw + 1f) * 0.5f * candidates.Count), 0, candidates.Count - 1);
        return candidates[idx];
    }

    static int PickFood(Agent a, float raw)
    {
        var foods = new[] { Item.CookedMeat, Item.Meat, Item.Orange, Item.Tomato, Item.Bandage };
        var have = foods.Where(a.Has).ToList();
        if (have.Count == 0) return -1;
        int idx = Math.Clamp((int)((raw + 1f) * 0.5f * have.Count), 0, have.Count - 1);
        return (int)have[idx];
    }
}
