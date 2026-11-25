using System.Numerics;
using static SimBox.Item;

namespace SimBox.Train;

/// <summary>
/// Extrai um vetor de features normalizadas do estado do mundo + agente.
/// Tamanho fixo para a rede neural.
/// </summary>
public static class FeatureExtractor
{
    // Índices importantes de itens para inventário (compacto)
    static readonly Item[] TrackedItems =
    {
        Wood, Stone, Ore, Sulfur, Scrap, Iron, Alloy,
        Meat, CookedMeat, Orange, Tomato,
        AxeBasic, AxeIron, PickBasic, PickIron,
        Spear, Bow, Arrow, Pistol9, BulletLow,
        Campfire, Workbench, Chest, Bandage,
        Headscarf, Chapka, ArmorBear
    };

    public const int FeatureCount =
        8 +                         // status do agente
        2 +                         // dia/noite + progresso do ciclo
        TrackedItems.Length +       // inventário
        8 +                         // recursos próximos (dir + dist normalizada)
        6 +                         // ghouls próximos
        4 +                         // construções / fogo
        4 +                         // drops no chão
        3 +                         // craft / research info
        2;                          // time / level

    public static float[] Extract(World w, Agent a)
    {
        var f = new float[FeatureCount];
        int i = 0;

        // --- Status do agente (8) ---
        f[i++] = a.Hp / Defs.StatMax;
        f[i++] = a.Hunger / Defs.StatMax;
        f[i++] = a.Cold / Defs.StatMax;
        f[i++] = a.Stamina / Defs.StatMax;
        f[i++] = a.Rad / Defs.StatMax;
        f[i++] = a.Alive ? 1f : 0f;
        f[i++] = a.Level / 30f;
        f[i++] = a.SkillPts / 10f;

        // --- Ciclo dia/noite (2) ---
        f[i++] = w.IsDay ? 1f : 0f;
        f[i++] = w.TimeInCycle / w.Cfg.Cycle;

        // --- Inventário (TrackedItems) ---
        foreach (var it in TrackedItems)
            f[i++] = Math.Clamp(a.Count(it) / 50f, 0f, 1f);

        // --- Recursos mais próximos (4 tipos × 2 = 8) ---
        // Árvore, Pedra, Ferro, Enxofre
        EncodeNearestNode(w, a.Pos, NodeKind.Tree, f, ref i);
        EncodeNearestNode(w, a.Pos, NodeKind.Rock, f, ref i);
        EncodeNearestNode(w, a.Pos, NodeKind.IronOre, f, ref i);
        EncodeNearestNode(w, a.Pos, NodeKind.SulfurOre, f, ref i);

        // --- Ghouls (6) ---
        // mais próximo: dx, dy, dist, tipo normalizado + quantidade próxima + hostil?
        EncodeNearestGhoul(w, a.Pos, f, ref i);

        // --- Construções / fogo (4) ---
        f[i++] = NearBuilding(w, a.Pos, Bld.Campfire) || NearBuilding(w, a.Pos, Bld.Abrasador) ? 1f : 0f;
        f[i++] = NearLitFire(w, a.Pos) ? 1f : 0f;
        f[i++] = NearBuilding(w, a.Pos, Bld.Workbench) ? 1f : 0f;
        f[i++] = NearBuilding(w, a.Pos, Bld.Chest) ? 1f : 0f;

        // --- Drops no chão (4) ---
        EncodeNearestDrop(w, a.Pos, f, ref i);

        // --- Craft / Research (3) ---
        f[i++] = a.CraftLeft > 0 ? 1f : 0f;
        f[i++] = a.CraftLeft > 0 ? a.CraftLeft / Math.Max(1f, a.CraftTotal) : 0f;
        int canResearch = 0;
        for (int k = 0; k < Defs.ItemCount; k++)
            if (w.ResearchCheck(a, (Item)k) == null) canResearch++;
        f[i++] = Math.Clamp(canResearch / 10f, 0f, 1f);

        // --- Tempo de vida e nível relativo (2) ---
        f[i++] = Math.Clamp((w.Time - a.BornAt) / 960f, 0f, 5f); // em ciclos
        f[i++] = a.Xp / 5000f;

        return f;
    }

    static void EncodeNearestNode(World w, Vector2 pos, NodeKind kind, float[] f, ref int i)
    {
        float best = float.MaxValue;
        Vector2 bestPos = pos;
        foreach (var n in w.Nodes)
        {
            if (!n.Alive || n.Kind != kind) continue;
            float d = Vector2.DistanceSquared(n.Pos, pos);
            if (d < best) { best = d; bestPos = n.Pos; }
        }
        if (best > 1e10f)
        {
            f[i++] = 0f; f[i++] = 0f; // nada encontrado → direção zero + dist máxima
        }
        else
        {
            var dir = bestPos - pos;
            float dist = MathF.Sqrt(best);
            if (dist > 0.01f) dir /= dist;
            f[i++] = dir.X;
            f[i++] = Math.Clamp(dist / 1500f, 0f, 1f); // normalizado
        }
    }

    static void EncodeNearestGhoul(World w, Vector2 pos, float[] f, ref int i)
    {
        float best = float.MaxValue;
        Ghoul? nearest = null;
        int nearby = 0;
        foreach (var g in w.Ghouls)
        {
            if (!g.Alive) continue;
            float d = Vector2.DistanceSquared(g.Pos, pos);
            if (d < 900f * 900f) nearby++;
            if (d < best) { best = d; nearest = g; }
        }

        if (nearest == null)
        {
            f[i++] = 0; f[i++] = 0; f[i++] = 1f; f[i++] = 0; f[i++] = 0; f[i++] = 0;
        }
        else
        {
            var dir = nearest.Pos - pos;
            float dist = MathF.Sqrt(best);
            if (dist > 0.01f) dir /= dist;
            f[i++] = dir.X;
            f[i++] = dir.Y;
            f[i++] = Math.Clamp(dist / 1200f, 0f, 1f);
            f[i++] = (int)nearest.Kind / 4f;
            f[i++] = Math.Clamp(nearby / 8f, 0f, 1f);
            f[i++] = nearest.Hostile ? 1f : 0f;
        }
    }

    static void EncodeNearestDrop(World w, Vector2 pos, float[] f, ref int i)
    {
        float best = float.MaxValue;
        GroundItem? nearest = null;
        foreach (var d in w.Drops)
        {
            if (w.Time < d.NoPick) continue;
            float dist = Vector2.DistanceSquared(d.Pos, pos);
            if (dist < best) { best = dist; nearest = d; }
        }
        if (nearest == null)
        {
            f[i++] = 0; f[i++] = 0; f[i++] = 1f; f[i++] = 0;
        }
        else
        {
            var dir = nearest.Pos - pos;
            float dist = MathF.Sqrt(best);
            if (dist > 0.01f) dir /= dist;
            f[i++] = dir.X;
            f[i++] = dir.Y;
            f[i++] = Math.Clamp(dist / 800f, 0f, 1f);
            f[i++] = (int)nearest.Item / (float)Defs.ItemCount;
        }
    }

    static bool NearBuilding(World w, Vector2 pos, Bld b)
    {
        return w.FindStation(pos, b, 280f) >= 0;
    }

    static bool NearLitFire(World w, Vector2 pos)
    {
        return w.NearLitFire(pos);
    }
}
