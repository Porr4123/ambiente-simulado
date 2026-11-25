using System.Diagnostics;
using SimBox.Train;

// SimBox.Train — Neuroevolução headless com interface de acompanhamento no console
//   dotnet run --project SimBox.Train -c Release

Console.Title = "SimBox NeuroEvolution";
Console.CursorVisible = false;

var trainer = new EvolutionaryTrainer();

Console.Clear();
PrintHeader();

int targetGens = 100;
var swTotal = Stopwatch.StartNew();

for (int g = 0; g < targetGens; g++)
{
    var sw = Stopwatch.StartNew();
    int weeks = trainer.WeeksForGeneration(trainer.Generation);

    DrawDashboard(trainer, weeks, running: true, elapsed: TimeSpan.Zero, progress: 0f);

    trainer.RunGeneration(progress =>
    {
        DrawDashboard(trainer, weeks, running: true, elapsed: sw.Elapsed, progress: progress);
    });

    sw.Stop();
    DrawDashboard(trainer, weeks, running: false, elapsed: sw.Elapsed, progress: 1f);
}

Console.CursorVisible = true;
Console.WriteLine();
Console.WriteLine("Treino finalizado.");
Console.WriteLine($"Melhor fitness alcançado: {trainer.BestFitness:F1}");
Console.WriteLine($"Tempo total: {swTotal.Elapsed:hh\\:mm\\:ss}");

static void PrintHeader()
{
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║           SimBox — Neuroevolução (2 Times · CPU)                     ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
}

static void DrawDashboard(EvolutionaryTrainer t, int weeks, bool running, TimeSpan elapsed, float progress)
{
    try { Console.SetCursorPosition(0, 4); } catch { /* console estreito */ }

    string status = running ? "▶ RODANDO..." : "✓ concluída";
    string timeStr = elapsed.TotalSeconds < 0.05 && running ? "..." : $"{elapsed.TotalSeconds:F0}s";
    int pct = (int)(progress * 100);
    string bar = new string('█', pct / 5) + new string('░', 20 - pct / 5);

    Console.WriteLine($"Geração: {t.Generation,4}   |   {weeks} semana(s)   |   {status,-12}   |   {timeStr,6}     ");
    Console.WriteLine($"Progresso: [{bar}] {pct,3}%                                              ");
    Console.WriteLine(new string('─', 72));

    Console.WriteLine($"Melhor fitness : {t.BestFitness,8:F1}     Média: {t.AvgFitness,8:F1}                         ");
    Console.WriteLine($"Sobreviventes  : {t.SurvivorsLastGen,2}/32                                              ");
    Console.WriteLine();

    float total = Math.Abs(t.Team0Score) + Math.Abs(t.Team1Score) + 0.001f;
    // barras pelo valor absoluto relativo (scores negativos no início)
    int bar0 = (int)(30 * Math.Abs(t.Team0Score) / total);
    bar0 = Math.Clamp(bar0, 0, 30);
    int bar1 = 30 - bar0;
    Console.WriteLine($"Time 0 (azul)  : {t.Team0Score,9:F1}  [{new string('█', bar0)}{new string('░', 30 - bar0)}]");
    Console.WriteLine($"Time 1 (vermelho): {t.Team1Score,9:F1}  [{new string('█', bar1)}{new string('░', 30 - bar1)}]");
    Console.WriteLine();

    Console.WriteLine("Top 8 agentes da última geração:");
    Console.WriteLine(" #  Time  Fitness   Surv   Reward   Lvl  Kills");
    Console.WriteLine("────────────────────────────────────────────────");

    var top = t.LastTop8;
    if (top.Length > 0)
    {
        for (int i = 0; i < top.Length; i++)
        {
            var b = top[i];
            string team = b.Team == 0 ? "T0" : "T1";
            string surv = b.Survived ? "SIM" : "não";
            Console.WriteLine($"{i + 1,2}   {team}  {b.Fitness,8:F1}   {surv,3}  {b.RawReward,7:F1}  {b.FinalLevel,3}   {b.FinalKills,3}  ");
        }
        for (int i = top.Length; i < 8; i++)
            Console.WriteLine(new string(' ', 55));
    }
    else
    {
        for (int i = 0; i < 8; i++)
            Console.WriteLine(new string(' ', 55));
    }

    Console.WriteLine();
    Console.WriteLine("Regras: +100 sobreviver  |  -50 morrer  |  Elitismo top 8  |  tempo comprimido no treino");
    Console.WriteLine("Primeiras 28 gerações = 1 semana   |   Depois = 2 semanas");
    Console.WriteLine(new string('─', 72));
}
