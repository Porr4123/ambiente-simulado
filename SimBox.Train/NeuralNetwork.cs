namespace SimBox.Train;

/// <summary>
/// Rede neural feedforward simples e rápida (CPU).
/// Arquitetura padrão: Input → 80 → 48 → 32 → Output
/// </summary>
public sealed class NeuralNetwork
{
    public readonly int[] Layers;          // ex: [input, 80, 48, 32, output]
    public readonly float[][] Weights;     // Weights[l][i*prev + j]
    public readonly float[][] Biases;
    public readonly float[][] Activations; // buffer reutilizável

    public int TotalWeights
    {
        get
        {
            int n = 0;
            for (int l = 1; l < Layers.Length; l++)
                n += Layers[l] * Layers[l - 1] + Layers[l];
            return n;
        }
    }

    public NeuralNetwork(int[] layers)
    {
        Layers = (int[])layers.Clone();
        Weights = new float[layers.Length][];
        Biases = new float[layers.Length][];
        Activations = new float[layers.Length][];

        var rng = new Random();
        for (int l = 0; l < layers.Length; l++)
        {
            Activations[l] = new float[layers[l]];
            if (l == 0) continue;

            int prev = layers[l - 1];
            int cur = layers[l];
            Weights[l] = new float[cur * prev];
            Biases[l] = new float[cur];

            // Xavier-ish init
            float scale = MathF.Sqrt(2f / (prev + cur));
            for (int i = 0; i < Weights[l].Length; i++)
                Weights[l][i] = (float)(rng.NextDouble() * 2 - 1) * scale;
            for (int i = 0; i < cur; i++)
                Biases[l][i] = (float)(rng.NextDouble() * 2 - 1) * 0.1f;
        }
    }

    public NeuralNetwork Clone()
    {
        var nn = new NeuralNetwork(Layers);
        for (int l = 1; l < Layers.Length; l++)
        {
            Array.Copy(Weights[l], nn.Weights[l], Weights[l].Length);
            Array.Copy(Biases[l], nn.Biases[l], Biases[l].Length);
        }
        return nn;
    }

    /// <summary>Forward pass. input.Length deve ser == Layers[0]</summary>
    public float[] Forward(float[] input)
    {
        Array.Copy(input, Activations[0], Layers[0]);

        for (int l = 1; l < Layers.Length; l++)
        {
            int prev = Layers[l - 1];
            int cur = Layers[l];
            var w = Weights[l];
            var b = Biases[l];
            var prevAct = Activations[l - 1];
            var act = Activations[l];

            for (int j = 0; j < cur; j++)
            {
                float sum = b[j];
                int row = j * prev;
                for (int i = 0; i < prev; i++)
                    sum += w[row + i] * prevAct[i];

                // tanh na escondida, linear na saída
                act[j] = l < Layers.Length - 1 ? MathF.Tanh(sum) : sum;
            }
        }
        return Activations[^1];
    }

    public void Mutate(float rate, float strength, Random rng)
    {
        for (int l = 1; l < Layers.Length; l++)
        {
            var w = Weights[l];
            for (int i = 0; i < w.Length; i++)
                if (rng.NextDouble() < rate)
                    w[i] += (float)(rng.NextDouble() * 2 - 1) * strength;

            var b = Biases[l];
            for (int i = 0; i < b.Length; i++)
                if (rng.NextDouble() < rate)
                    b[i] += (float)(rng.NextDouble() * 2 - 1) * strength * 0.5f;
        }
    }

    /// <summary>Crossover uniforme entre dois pais → filho</summary>
    public static NeuralNetwork Crossover(NeuralNetwork a, NeuralNetwork b, Random rng)
    {
        var child = a.Clone();
        for (int l = 1; l < a.Layers.Length; l++)
        {
            var wa = a.Weights[l];
            var wb = b.Weights[l];
            var wc = child.Weights[l];
            for (int i = 0; i < wc.Length; i++)
                wc[i] = rng.NextDouble() < 0.5 ? wa[i] : wb[i];

            var ba = a.Biases[l];
            var bb = b.Biases[l];
            var bc = child.Biases[l];
            for (int i = 0; i < bc.Length; i++)
                bc[i] = rng.NextDouble() < 0.5 ? ba[i] : bb[i];
        }
        return child;
    }

    public float[] GetGenome()
    {
        var g = new float[TotalWeights];
        int idx = 0;
        for (int l = 1; l < Layers.Length; l++)
        {
            Array.Copy(Weights[l], 0, g, idx, Weights[l].Length);
            idx += Weights[l].Length;
            Array.Copy(Biases[l], 0, g, idx, Biases[l].Length);
            idx += Biases[l].Length;
        }
        return g;
    }

    public void SetGenome(float[] genome)
    {
        int idx = 0;
        for (int l = 1; l < Layers.Length; l++)
        {
            Array.Copy(genome, idx, Weights[l], 0, Weights[l].Length);
            idx += Weights[l].Length;
            Array.Copy(genome, idx, Biases[l], 0, Biases[l].Length);
            idx += Biases[l].Length;
        }
    }
}
