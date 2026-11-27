using System.Text;
using System.Text.Json;

namespace SimBox.Train;

/// <summary>Resumo de uma geração já simulada: o treinador preenche e o modelo escreve o registro (log, CSV, linha de console) a partir dele.</summary>
public sealed class GenerationInfo
{
    public int Generation, WorldSeed, TrainSeed, Weeks, Survivors;
    public double WallSeconds;
    public float SimSeconds, Best, Avg, Team0, Team1;
    public World World = null!;
    public SimConfig Cfg = null!;
    public AgentBrain[] Ranked = Array.Empty<AgentBrain>();
}

/// <summary>Soma por time e nº de "bem-sucedidos" (vivos no modelo de times; extraídos no de coleta) de uma geração.</summary>
public readonly record struct GenerationStats(int Survivors, float Team0, float Team1);

/// <summary>
/// Um "modelo de treinamento" define O QUE é treinado: mundo (regras, duração), quem joga, o que a rede enxerga a mais, como se pontua
/// e como a geração é registrada. O <see cref="EvolutionaryTrainer"/> só cuida da evolução e do laço de simulação.
/// Modelos: <see cref="CollectModel"/> (coleta e extração, 32 IAs) e <see cref="TimesModel"/> (2 times de 32, combate).
/// </summary>
public abstract class TrainingModel
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract int PopulationSize { get; }
    public virtual int EliteCount => 8;

    /// <summary>Time do agente no mundo (0 ou 1). Sem times: todos 0.</summary>
    public virtual int TeamOf(int slot) => 0;
    /// <summary>Grupo de decisão do agente (0 ou 1): cada grupo roda na sua thread dedicada (<see cref="DecisionPool"/>).</summary>
    public virtual int GroupOf(int slot) => slot & 1;
    /// <summary>true = o comando Select escolhe entre os slots ocupados (+ a mão vazia) em vez de um índice cru de slot.</summary>
    public virtual bool SelectOccupiedSlots => false;

    // ---- entradas da rede: as de FeatureExtractor + as extras do modelo (sempre no FIM do vetor, para dar para reaproveitar cérebros menores)
    public abstract int ExtraFeatures { get; }
    public int InputSize => FeatureExtractor.FeatureCount + ExtraFeatures;
    public abstract void FillExtraFeatures(World w, Agent a, float[] f, ref int i);

    // ---- mundo
    public abstract SimConfig MakeConfig();
    /// <summary>Ajusta o config da geração e devolve a duração (segundos simulados).</summary>
    public abstract float PrepareGeneration(SimConfig cfg, int generation);
    public virtual int WeeksFor(int generation) => 0;
    public abstract string GenerationTitle(int generation);

    // ---- avaliação e registro
    /// <summary>Copia as estatísticas do mundo para os cérebros e define o Fitness de cada um.</summary>
    public abstract GenerationStats Evaluate(World w, AgentBrain[] pop);
    public abstract string DoneNote { get; }
    public abstract void WriteLogBody(StringBuilder sb, GenerationInfo g);
    public abstract string SummaryHeader { get; }
    public abstract string SummaryRow(GenerationInfo g);
    public abstract string ConsoleLine(EvolutionaryTrainer t, double seconds);

    // ---- monitor
    public virtual float LiveScore(World w, int i) => w.Rewards[i] + (w.Agents[i].Alive ? 0f : -50f) + w.Agents[i].Level * 2f;
    public virtual void WriteMonitorExtras(Utf8JsonWriter j, World w) { }

    public static TrainingModel? ByName(string name) => name.ToLowerInvariant() switch
    {
        "coleta" or "collect" => new CollectModel(),
        "times" or "teams" or "combate" => new TimesModel(),
        _ => null
    };
    public static readonly string[] Names = { "coleta", "times" };
}
