using System.Diagnostics;
using System.Globalization;
using SimBox.Train;

// SimBox.Train — Neuroevolução headless com console + monitor duplo no navegador
//   dotnet run --project SimBox.Train -c Release -- [opções]
//     --gens N        limite de gerações (padrão: ilimitado — pare com Ctrl+S no console; 2× Ctrl+S encerra a geração em curso já)
//     --seed N        semente do treino (reproduzível)
//     --out DIR       pasta dos registros (padrão ./runs)
//     --port N        porta do monitor (padrão 5080)
//     --speed X       X× tempo real para acompanhar nos monitores (0 = o mais rápido possível, padrão)
//     --resume DIR    retoma de uma pasta run_... (usa os top 8 da última geração gravada como elite)
//     --noopen        não abrir o navegador automaticamente
//     --nomonitor     sem servidor de monitor
//     --quiet         sem painel no console (uma linha por geração)
//     --serial        tudo numa thread só (padrão com menos de 3 núcleos)
//     --parallel      força 1 núcleo p/ o mundo + 1 p/ cada time (padrão com 3+ núcleos)

int gens = 0, port = 5080; int? seed = null; float speed = 0; string outDir = "runs"; string? resumeDir = null;   // gens 0 = ilimitado
bool open = true, monitorOn = true, quiet = false; bool? parallelTeams = null;
var inv = CultureInfo.InvariantCulture;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--gens": gens = int.Parse(args[++i]); break;
        case "--seed": seed = int.Parse(args[++i]); break;
        case "--out": outDir = args[++i]; break;
        case "--port": port = int.Parse(args[++i]); break;
        case "--speed": speed = float.Parse(args[++i], inv); break;
        case "--resume": resumeDir = args[++i]; break;
        case "--noopen": open = false; break;
        case "--nomonitor": monitorOn = false; break;
        case "--quiet": quiet = true; break;
        case "--serial": parallelTeams = false; break;
        case "--parallel": parallelTeams = true; break;
        default: Console.WriteLine($"Opção desconhecida: {args[i]}"); return 1;
    }
}

var trainer = new EvolutionaryTrainer(seed: seed) { Speed = speed };
if (parallelTeams != null) trainer.ParallelTeams = parallelTeams.Value;
if (resumeDir != null)
{
    var latest = RunLogger.LoadLatest(resumeDir);
    if (latest == null) { Console.WriteLine($"Nada para retomar em {resumeDir} (esperava gen_XXXX/top*.brain)."); return 1; }
    trainer.Resume(latest.Value.brains, latest.Value.gen);
    Console.WriteLine($"Retomando da geração {latest.Value.gen} ({latest.Value.brains.Count} cérebros carregados).");
}
trainer.Logger = new RunLogger(outDir);

string monitorInfo = "monitor desligado";
if (monitorOn)
{
    var mon = new MonitorServer(trainer, port);
    if (mon.Start(out var err))
    {
        monitorInfo = $"Monitor 1 (mapa): {mon.MapUrl}   Monitor 2 (foco no top 1): {mon.FocusUrl}";
        if (open)
        {
            foreach (var u in new[] { mon.MapUrl, mon.FocusUrl })
            {
                try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch { /* sem navegador padrão: abra as URLs à mão */ }
                Thread.Sleep(400);
            }
        }
    }
    else monitorInfo = $"monitor NÃO iniciou ({err}) — tente --port {port + 1}";
}

if (!quiet)
{
    try { Console.Title = "SimBox NeuroEvolution"; Console.CursorVisible = false; Console.Clear(); } catch { }
    PrintHeader();
}
Console.WriteLine($"Registro: {Path.GetFullPath(trainer.Logger.RunDir)}   seed do treino: {trainer.Seed}");
Console.WriteLine(monitorInfo);

// Ctrl+S no console: 1ª vez = para ao fim da geração em curso (ela é gravada); 2ª vez = encerra a geração em curso já.
if (!Console.IsInputRedirected)
{
    new Thread(() =>
    {
        try
        {
            while (true)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.KeyChar == '\u0013' || (k.Key == ConsoleKey.S && (k.Modifiers & ConsoleModifiers.Control) != 0))
                {
                    if (trainer.StopRequested) trainer.EndNow = true; else trainer.StopRequested = true;
                }
            }
        }
        catch { /* sem console interativo: só pelo Ctrl+C */ }
    }) { IsBackground = true, Name = "teclado" }.Start();
}
Console.WriteLine(trainer.ParallelTeams ? $"Núcleos: 1 do mundo + 1 por time (decisão dos 2 times em paralelo) · {Environment.ProcessorCount} lógicos nesta máquina" : $"Núcleos: modo serial (uma thread) · {Environment.ProcessorCount} lógicos nesta máquina — use --parallel para 1 núcleo do mundo + 1 por time");
Console.WriteLine(gens > 0 ? $"Limite: {gens} gerações · Ctrl+S para parar antes." : "Gerações ilimitadas · Ctrl+S para parar ao fim da geração (2× = encerrar a geração em curso já).");

var swTotal = Stopwatch.StartNew(); int ran = 0;
for (int g = 0; !trainer.StopRequested && (gens <= 0 || g < gens); g++)
{
    var sw = Stopwatch.StartNew();
    int weeks = trainer.WeeksForGeneration(trainer.Generation);

    if (!quiet) DrawDashboard(trainer, weeks, running: true, elapsed: TimeSpan.Zero, progress: 0f);

    trainer.RunGeneration(progress =>
    {
        if (!quiet) DrawDashboard(trainer, weeks, running: true, elapsed: sw.Elapsed, progress: progress);
    });

    sw.Stop(); ran++;
    if (quiet)
        Console.WriteLine(string.Format(inv, "ger {0,3} | {1}s | melhor {2,8:F1} | média {3,8:F1} | vivos {4,2}/64 | T0 {5,8:F1} T1 {6,8:F1} | inimigos {7} amigos {8} | {9}",
            trainer.CurrentGeneration, sw.Elapsed.TotalSeconds.ToString("F1", inv), trainer.BestFitness, trainer.AvgFitness, trainer.SurvivorsLastGen, trainer.Team0Score, trainer.Team1Score,
            trainer.LastTop8.Sum(b => b.EnemyKills), trainer.LastTop8.Sum(b => b.FriendKills), Path.GetFileName(trainer.LastGenDir)));
    else DrawDashboard(trainer, weeks, running: false, elapsed: sw.Elapsed, progress: 1f);
}

try { Console.CursorVisible = true; } catch { }
Console.WriteLine();
Console.WriteLine(trainer.StopRequested ? $"Treino parado por Ctrl+S após {ran} geração(ões)." : "Treino finalizado.");
Console.WriteLine($"Melhor fitness da última geração: {trainer.BestFitness:F1}");
Console.WriteLine($"Tempo total: {swTotal.Elapsed:hh\\:mm\\:ss}   ·   registros em {Path.GetFullPath(trainer.Logger.RunDir)}");
return 0;

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
    int shown = running ? t.Generation + 1 : t.CurrentGeneration;

    Console.WriteLine($"Geração: {shown,4}   |   {weeks} semana(s)   |   {status,-12}   |   {timeStr,6}     ");
    Console.WriteLine($"Progresso: [{bar}] {pct,3}%                                              ");
    Console.WriteLine(new string('─', 78));

    Console.WriteLine($"Melhor fitness : {t.BestFitness,8:F1}     Média: {t.AvgFitness,8:F1}      (última geração concluída)   ");
    Console.WriteLine($"Sobreviventes  : {t.SurvivorsLastGen,2}/{EvolutionaryTrainer.PopulationSize}                                              ");
    Console.WriteLine();

    float total = Math.Abs(t.Team0Score) + Math.Abs(t.Team1Score) + 0.001f;
    int bar0 = Math.Clamp((int)(30 * Math.Abs(t.Team0Score) / total), 0, 30);
    int bar1 = 30 - bar0;
    Console.WriteLine($"Time 0 (azul)    : {t.Team0Score,9:F1}  [{new string('█', bar0)}{new string('░', 30 - bar0)}]");
    Console.WriteLine($"Time 1 (vermelho): {t.Team1Score,9:F1}  [{new string('█', bar1)}{new string('░', 30 - bar1)}]");
    Console.WriteLine();

    Console.WriteLine("Top 8 agentes da última geração:");
    Console.WriteLine(" #  Time  Fitness   Surv   Reward  Nív  Ghouls  Inim  Amigo  Ferr  Doa  Est  Ref(b/d)");
    Console.WriteLine(new string('─', 78));

    var top = t.LastTop8;
    for (int i = 0; i < 8; i++)
    {
        if (i < top.Length)
        {
            var b = top[i];
            string team = b.Team == 0 ? "T0" : "T1";
            string surv = b.Survived ? "SIM" : "não";
            Console.WriteLine($"{i + 1,2}   {team}  {b.Fitness,8:F1}   {surv,3}  {b.RawReward,7:F1}  {b.FinalLevel,3}  {b.FinalKills,6}  {b.EnemyKills,4}  {b.FriendKills,5}  {b.ToolsMade,4}  {b.Donations,3}  {b.StationsMade,3}  {b.GoodMeals,3}/{b.WastedMeals,-3}");
        }
        else Console.WriteLine(new string(' ', 78));
    }

    Console.WriteLine();
    Console.WriteLine("fitness = reward da geração + 100 (vivo) / -50 (morto) + 2·nível · mortos só renascem na próxima geração");
    Console.WriteLine("times: fogo amigo e matar amigo penalizam · acertar/matar rival e ghouls (mais com armas melhores) bonificam");
    Console.WriteLine("ferramenta/estação fabricada: bônus 1x/geração · comer sem desperdiçar bonifica · muro no meio até os 2 últimos dias");
    Console.WriteLine("Primeiras 28 gerações = 1 semana   |   Depois = 2 semanas   |   Top 8 salvo em runs/run_.../gen_XXXX/");
    Console.WriteLine(t.EndNow ? "⏹ encerrando a geração em curso agora...                                      " : t.StopRequested ? "⏹ parada solicitada: termina ao fim desta geração (Ctrl+S de novo = encerrar já)       " : "Ctrl+S: parar ao fim da geração (2× = encerrar a geração em curso já)                    ");
    Console.WriteLine(new string('─', 78));
}
