using System.Globalization;
using System.Text;

namespace SimBox.Train;

/// <summary>
/// Registro do treino em pasta:
///   runs/run_AAAAMMDD_HHMMSS/summary.csv
///   runs/run_.../gen_0001/log.txt            (resumo, tabela de todos os agentes e eventos da geração)
///   runs/run_.../gen_0001/top1_a07_T0_f215.brain ... top8_...   (os 8 melhores da geração)
/// </summary>
public sealed class RunLogger
{
    public string RunDir { get; }
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public RunLogger(string baseDir, string? name = null)
    {
        RunDir = Path.Combine(baseDir, name ?? "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(RunDir);
    }

    public static string GenDirName(int genNumber) => $"gen_{genNumber:D4}";

    public string SaveGeneration(int genNumber, string logText, IReadOnlyList<AgentBrain> top)
    {
        string dir = Path.Combine(RunDir, GenDirName(genNumber));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "log.txt"), logText, new UTF8Encoding(false));
        for (int k = 0; k < top.Count; k++)
        {
            var b = top[k];
            b.Save(Path.Combine(dir, string.Format(Inv, "top{0}_a{1:D2}_T{2}_f{3:0}.brain", k + 1, b.SlotId, b.Team, b.Fitness)));
        }
        return dir;
    }

    public void AppendSummary(string header, string row)
    {
        string path = Path.Combine(RunDir, "summary.csv");
        if (!File.Exists(path)) File.WriteAllText(path, header + "\n", new UTF8Encoding(false));
        File.AppendAllText(path, row + "\n");
    }

    /// <summary>Últimos cérebros salvos numa pasta de geração (ordenados top1..top8) — usado por --resume.</summary>
    public static (int gen, List<AgentBrain> brains)? LoadLatest(string runDir, int expectedInputs = 0)
    {
        if (!Directory.Exists(runDir)) return null;
        var genDir = Directory.GetDirectories(runDir, "gen_*").OrderBy(d => d, StringComparer.Ordinal).LastOrDefault();
        if (genDir == null) return null;
        int gen = int.TryParse(Path.GetFileName(genDir)[4..], out var g) ? g : 0;
        var files = Directory.GetFiles(genDir, "top*.brain").OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (files.Count == 0) return null;
        return (gen, files.Select(f => AgentBrain.Load(f, expectedInputs)).ToList());
    }
}
