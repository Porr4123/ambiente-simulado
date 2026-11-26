using System.Diagnostics;

namespace SimBox.Train;

/// <summary>
/// Divisão de núcleos do treino: a thread chamadora é o "núcleo do mundo" (World.Step, monitor, progresso) e este pool
/// mantém uma thread dedicada por time, que só decide (features + rede) os agentes do seu time.
/// Cada tick de decisão é uma fase: o mundo fica parado (só leitura) enquanto os dois times pensam em paralelo,
/// depois o mundo avança sozinho. O resultado é idêntico ao modo serial: cada agente tem a sua rede e escreve só no seu slot.
/// </summary>
public sealed class DecisionPool : IDisposable
{
    readonly int[][] teamIdx;               // índices (no vetor de agentes) de cada time
    readonly Action<int> decideOne;
    readonly Thread[] threads;
    int phase, done;                        // phase: nº da rodada pedida; done: quantos times terminaram nela
    volatile bool stop;
    Exception? error;

    public int Teams => teamIdx.Length;

    public DecisionPool(int[][] teamIndices, Action<int> decideOne)
    {
        teamIdx = teamIndices; this.decideOne = decideOne;
        threads = new Thread[teamIdx.Length];
        for (int t = 0; t < threads.Length; t++)
        {
            int team = t;
            threads[t] = new Thread(() => Worker(team)) { IsBackground = true, Name = $"time-{team}", Priority = ThreadPriority.AboveNormal };
            threads[t].Start();
        }
    }

    /// <summary>Faz todos os times decidirem e só volta quando os dois terminaram.</summary>
    public void Run()
    {
        Volatile.Write(ref done, 0);
        Interlocked.Increment(ref phase);                        // libera os workers (barreira de escrita: os inputs/estado do mundo ficam visíveis)
        int spins = 0; var sw = new SpinWait();
        while (Volatile.Read(ref done) < teamIdx.Length)
        {
            if (++spins < 20000) sw.SpinOnce(-1); else Thread.Sleep(1);
        }
        if (error != null) { var e = error; error = null; throw new InvalidOperationException("falha numa thread de time", e); }
    }

    void Worker(int team)
    {
        int seen = 0; var mine = teamIdx[team];
        while (true)
        {
            int spins = 0; var sw = new SpinWait();
            while (Volatile.Read(ref phase) == seen)
            {
                if (stop) return;
                // gira (spin) enquanto o próximo tick chega em microssegundos; se o mundo está pausado (--speed, Ctrl+S), dorme para não gastar CPU
                if (++spins < 20000) sw.SpinOnce(-1); else Thread.Sleep(1);
            }
            seen = Volatile.Read(ref phase);
            try { foreach (int i in mine) decideOne(i); }
            catch (Exception ex) { error ??= ex; }
            Interlocked.Increment(ref done);
        }
    }

    public void Dispose()
    {
        stop = true;
        foreach (var t in threads) t.Join(2000);
    }
}
