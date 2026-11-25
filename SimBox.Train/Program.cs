using System.Diagnostics;
using SimBox.Train;

// SimBox.Train — Neuroevolução headless com interface de acompanhamento no console
//   dotnet run --project SimBox.Train -c Release

Console.Title = "SimBox NeuroEvolution";
Console.CursorVisible = false;

var trainer = new EvolutionaryTrainer();

Console.Clear();
PrintHeader();

int targetGens = 100; // pode mudar depois
var swTotal = Stopwatch.StartNew();

for (int g = 0; g < targetGens; g++)
{
    var sw = Stopwatch.StartNew();
    int weeks = trainer.WeeksForGeneration(trainer.Generation);

    // Atualiza UI antes de rodar (mostra "rodando...")
    DrawDashboard(trainer, weeks, running: true, elapsed: TimeSpan.Zero);

    trainer.RunGeneration();
    sw.Stop();

    DrawDashboard(trainer, weeks, running: false, elapsed: sw.Elapsed);

    // Pequena pausa só para conseguir ler (opcional)
    // Thread.Sleep(80);
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

static void DrawDashboard(EvolutionaryTrainer t, int weeks, bool running, TimeSpan elapsed)
{
    // Volta pro topo
    Console.SetCursorPosition(0, 4);

    string status = running ? "▶ RODANDO..." : "✓ concluída";
    string timeStr = running ? "..." : $"{elapsed.TotalSeconds:F1}s";

    Console.WriteLine($"Geração: {t.Generation,4}   |   Duração: {weeks} semana(s)   |   {status,-12}   |   {timeStr,8}     ");
    Console.WriteLine(new string('─', 72));

    // Fitness
    Console.WriteLine($"Melhor fitness : {t.BestFitness,8:F1}     Média: {t.AvgFitness,8:F1}                         ");
    Console.WriteLine($"Sobreviventes  : {t.SurvivorsLastGen,2}/32                                              ");
    Console.WriteLine();

    // Times
    float total = t.Team0Score + t.Team1Score + 0.001f;
    int bar0 = (int)(30 * t.Team0Score / total);
    int bar1 = 30 - bar0;
    Console.WriteLine($"Time 0 (azul)  : {t.Team0Score,9:F1}  [{new string('█', bar0)}{new string('░', 30 - bar0)}]");
    Console.WriteLine($"Time 1 (vermelho): {t.Team1Score,9:F1}  [{new string('█', bar1)}{new string('░', 30 - bar1)}]");
    Console.WriteLine();

    // Top 8
    Console.WriteLine("Top 8 agentes da última geração:");
    Console.WriteLine(" #  Time  Fitness   Surv   Reward   Lvl  Kills");
    Console.WriteLine("────────────────────────────────────────────────");

    if (!running && t.Generation > 0)
    {
        var top = t.GetTop(8);
        for (int i = 0; i < top.Length; i++)
        {
            var b = top[i];
            string team = b.Team == 0 ? "T0" : "T1";
            string surv = b.Survived ? "SIM" : "não";
            Console.WriteLine($"{i + 1,2}   {team}  {b.Fitness,8:F1}   {surv,3}  {b.RawReward,7:F1}  {b.FinalLevel,3}   {b.FinalKills,3}  ");
        }
    }
    else
    {
        for (int i = 0; i < 8; i++)
            Console.WriteLine(new string(' ', 55));
    }

    Console.WriteLine();
    Console.WriteLine("Regras: +100 sobreviver a semana  |  -50 morrer  |  Elitismo: top 8 passam");
    Console.WriteLine("Primeiras 28 gerações = 1 semana   |   Depois = 2 semanas");
    Console.WriteLine(new string('─', 72));
}
