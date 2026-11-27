using System.Diagnostics;
using System.Globalization;
using SimBox.Train;

// SimBox.Train — Neuroevolução headless com console + monitor duplo no navegador
//   dotnet run --project SimBox.Train -c Release -- [opções]
//     --model M       o que treinar: coleta (padrão: 32 IAs coletam e vão à extração) ou times (2 times de 32, combate)
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
bool open = true, monitorOn = true, quiet = false; bool? parallelTeams = null; string modelName = "coleta";
var inv = CultureInfo.InvariantCulture;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--model": case "--modelo": modelName = args[++i]; break;
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

var model = TrainingModel.ByName(modelName);
if (model == null) { Console.WriteLine($"Modelo desconhecido: {modelName}. Use: {string.Join(", ", TrainingModel.Names)}"); return 1; }
var trainer = new EvolutionaryTrainer(model, seed) { Speed = speed };
if (parallelTeams != null) trainer.ParallelTeams = parallelTeams.Value;
if (resumeDir != null)
{
    var latest = RunLogger.LoadLatest(resumeDir, model.InputSize);
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
    PrintHeader(model);
}
Console.WriteLine($"Modelo: {model.Title} ({model.Id}) · {model.PopulationSize} IAs · {model.InputSize} entradas na rede");
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
    if (quiet) Console.WriteLine(trainer.Model.ConsoleLine(trainer, sw.Elapsed.TotalSeconds));
    if (!quiet) DrawDashboard(trainer, weeks, running: false, elapsed: sw.Elapsed, progress: 1f);
}

try { Console.CursorVisible = true; } catch { }
Console.WriteLine();
Console.WriteLine(trainer.StopRequested ? $"Treino parado por Ctrl+S após {ran} geração(ões)." : "Treino finalizado.");
Console.WriteLine($"Melhor fitness da última geração: {trainer.BestFitness:F1}");
Console.WriteLine($"Tempo total: {swTotal.Elapsed:hh\\:mm\\:ss}   ·   registros em {Path.GetFullPath(trainer.Logger.RunDir)}");
return 0;

static void PrintHeader(TrainingModel model)
{
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
    Console.WriteLine($"║  SimBox — Neuroevolução · {model.Title,-42}  ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
}

static void DrawDashboard(EvolutionaryTrainer t, int weeks, bool running, TimeSpan elapsed, float progress)
{
    if (t.Model is CollectModel) { DrawCollect(t, running, elapsed, progress); return; }
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
    Console.WriteLine($"Sobreviventes  : {t.SurvivorsLastGen,2}/{t.PopulationSize}                                              ");
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

static void DrawCollect(EvolutionaryTrainer t, bool running, TimeSpan elapsed, float progress)
{
    try { Console.SetCursorPosition(0, 4); } catch { /* console estreito */ }

    string status = running ? "▶ RODANDO..." : "✓ concluída";
    string timeStr = elapsed.TotalSeconds < 0.05 && running ? "..." : $"{elapsed.TotalSeconds:F0}s";
    int pct = (int)(progress * 100);
    string bar = new string('█', pct / 5) + new string('░', 20 - pct / 5);
    int shown = running ? t.Generation + 1 : t.CurrentGeneration;

    Console.WriteLine($"Geração: {shown,4}   |   {t.Model.GenerationTitle(t.Generation)}   |   {status,-12}   |   {timeStr,6}     ");
    Console.WriteLine($"Progresso: [{bar}] {pct,3}%                                              ");
    Console.WriteLine(new string('─', 90));
    Console.WriteLine($"Melhor pontuação : {t.BestFitness,8:F1}     Média: {t.AvgFitness,8:F1}      (última geração concluída)   ");
    Console.WriteLine($"Extraídos        : {t.SurvivorsLastGen,2}/{t.PopulationSize}                                              ");
    var all = t.LastTop8.Length > 0 ? t.Population : t.Population;
    Console.WriteLine($"Coleta média     : madeira {all.Average(b => b.GotWood),6:F0}   pedra {all.Average(b => b.GotStone),6:F0}   minério {all.Average(b => b.GotOre),6:F0}   frutas {all.Average(b => b.GotFruit),5:F1}      ");
    Console.WriteLine();
    Console.WriteLine("Top 8 da última geração:");
    Console.WriteLine(" #  Pontos  Extraído  t(s)  Madeira  Pedra  Minério  Frutas  Ferr  Est  Dist.min");
    Console.WriteLine(new string('─', 90));
    var top = t.LastTop8;
    for (int i = 0; i < 8; i++)
    {
        if (i < top.Length)
        {
            var b = top[i];
            Console.WriteLine($"{i + 1,2}  {b.Fitness,6:F0}  {(b.Extracted ? "SIM" : "não"),8}  {(b.Extracted ? b.ExtractedAt.ToString("F0") : "-"),4}  {b.GotWood,7}  {b.GotStone,5}  {b.GotOre,7}  {b.GotFruit,6}  {b.ToolsMade,4}  {b.StationsMade,3}  {(b.MinExtractDist == float.MaxValue ? "-" : b.MinExtractDist.ToString("F0")),8}      ");
        }
        else Console.WriteLine(new string(' ', 90));
    }
    Console.WriteLine();
    Console.WriteLine($"dia 8 min: coleta pontua (madeira {SimBox.Rw.CollectWood:0} · pedra {SimBox.Rw.CollectStone:0} · minério {SimBox.Rw.CollectOre:0}); noite 8 min: extração abre (+{SimBox.Rw.Extract:0}, canto inferior esquerdo), só minério pontua ({SimBox.Rw.CollectOreNight:0}); quem não extrai fica com 0");
    Console.WriteLine("sem monstros · sem combate · fome, frio constante e zonas de radiação · só fogueira, mesa, machado e picareta");
    Console.WriteLine(t.EndNow ? "⏹ encerrando a geração em curso agora...                                      " : t.StopRequested ? "⏹ parada solicitada: termina ao fim desta geração (Ctrl+S de novo = encerrar já)       " : "Ctrl+S: parar ao fim da geração (2× = encerrar a geração em curso já)                    ");
    Console.WriteLine(new string('─', 90));
}
