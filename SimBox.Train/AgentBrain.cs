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
    public float RawReward { get; set; }   // reward vindo do World.Rewards
    public int FinalLevel { get; set; }
    public int FinalKills { get; set; }

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

    public AgentBrain(NeuralNetwork? net = null, int team = 0)
    {
        Net = net ?? new NeuralNetwork(new[]
        {
            FeatureExtractor.FeatureCount,  // entrada
            80,                             // camada 1
            48,                             // camada 2
            32,                             // camada 3
            OutputSize                      // saída
        });
        Team = team;
    }

    public AgentBrain Clone()
    {
        return new AgentBrain(Net.Clone(), Team)
        {
            Fitness = Fitness,
            Survived = Survived,
            Deaths = Deaths,
            RawReward = RawReward
        };
    }

    public AgentInput Decide(World w, Agent a)
    {
        if (!a.Alive)
            return new AgentInput { Cmd = Cmd.Respawn };

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

        // Arg: depende do comando
        int arg = 0;
        float argRaw = outp[15];
        switch (cmd)
        {
            case Cmd.Select:
                arg = Math.Clamp((int)((argRaw + 1f) * 0.5f * a.Slots.Length), 0, a.Slots.Length - 1);
                break;
            case Cmd.Craft:
                // escolhe entre receitas possíveis
                arg = PickCraftRecipe(w, a, argRaw);
                break;
            case Cmd.Research:
                arg = PickResearch(w, a, argRaw);
                break;
            case Cmd.Eat:
                arg = PickFood(a, argRaw);
                break;
            default:
                arg = 0;
                break;
        }

        return new AgentInput
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
        if (candidates.Count == 0) return 0;
        int idx = Math.Clamp((int)((raw + 1f) * 0.5f * candidates.Count), 0, candidates.Count - 1);
        return candidates[idx];
    }

    static int PickResearch(World w, Agent a, float raw)
    {
        var candidates = new List<int>();
        for (int k = 0; k < Defs.ItemCount; k++)
            if (w.ResearchCheck(a, (Item)k) == null)
                candidates.Add(k);
        if (candidates.Count == 0) return 0;
        int idx = Math.Clamp((int)((raw + 1f) * 0.5f * candidates.Count), 0, candidates.Count - 1);
        return candidates[idx];
    }

    static int PickFood(Agent a, float raw)
    {
        var foods = new[] { Item.CookedMeat, Item.Meat, Item.Orange, Item.Tomato, Item.Bandage };
        var have = foods.Where(a.Has).ToList();
        if (have.Count == 0) return 0;
        int idx = Math.Clamp((int)((raw + 1f) * 0.5f * have.Count), 0, have.Count - 1);
        return (int)have[idx];
    }
}
