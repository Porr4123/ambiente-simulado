using System.Numerics;
using static SimBox.Item;

namespace SimBox.Train;

/// <summary>
/// Extrai um vetor de features normalizadas do estado do mundo + agente.
/// Tamanho fixo para a rede neural (FeatureCount é calculado a partir das listas abaixo).
/// </summary>
public static class FeatureExtractor
{
    // Itens do inventário que a rede enxerga (contagem/50, limitada a 1)
    public static readonly Item[] TrackedItems =
    {
        // recursos e comida
        Wood, Stone, Ore, Sulfur, Scrap, Iron, Alloy, Hide, Sinew, Fat,
        Meat, CookedMeat, Orange, Tomato, Bandage,
        // ferramentas (todas as famílias e níveis)
        AxeBasic, AxeIron, AxeSulfur, PickBasic, PickIron, PickSulfur, Hammer, Sledge,
        // armas e munição
        Spear, Bow, Pistol9, DesertEagle, Shotgun, MP5, AK47, Sniper, Arrow, BulletLow, BulletHigh, Shell,
        // construções colocáveis e vestimentas
        Campfire, Workbench, ResearchTable, Chest, Smelter,
        Headscarf, Chapka, ArmorBear, ArmorSmith
    };

    // Recursos mais próximos: Árvore, Pedra, Ferro, Enxofre → dx, dy, dist (3 cada)
    static readonly NodeKind[] NodeKinds = { NodeKind.Tree, NodeKind.Rock, NodeKind.IronOre, NodeKind.SulfurOre };

    public static readonly int FeatureCount =
        8 +                         // status do agente
        2 +                         // dia/noite + progresso do ciclo
        TrackedItems.Length +       // inventário
        NodeKinds.Length * 3 +      // recursos próximos (dx, dy, dist) — antes faltava o Y
        6 +                         // ghoul mais próximo
        4 +                         // construções / fogo
        4 +                         // drop mais próximo
        3 +                         // craft / research
        2 +                         // tempo de vida / xp
        6 +                         // agente rival e colega mais próximos (dx, dy, dist)
        2;                          // item selecionado (slot, tecnologia)

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

        // --- Inventário ---
        foreach (var it in TrackedItems)
            f[i++] = Math.Clamp(a.Count(it) / 50f, 0f, 1f);

        // --- Recursos mais próximos (dx, dy, dist) ---
        EncodeNearestNodes(w, a.Pos, f, ref i);

        // --- Ghoul mais próximo (6) ---
        EncodeNearestGhoul(w, a.Pos, f, ref i);

        // --- Construções / fogo (4) ---
        f[i++] = NearBuilding(w, a.Pos, Bld.Campfire) || NearBuilding(w, a.Pos, Bld.Abrasador) ? 1f : 0f;
        f[i++] = w.NearLitFire(a.Pos) ? 1f : 0f;
        f[i++] = NearBuilding(w, a.Pos, Bld.Workbench) ? 1f : 0f;
        f[i++] = NearBuilding(w, a.Pos, Bld.Chest) ? 1f : 0f;

        // --- Drop mais próximo (4) ---
        EncodeNearestDrop(w, a.Pos, f, ref i);

        // --- Craft / Research (3) ---
        f[i++] = a.CraftLeft > 0 ? 1f : 0f;
        f[i++] = a.CraftLeft > 0 ? a.CraftLeft / Math.Max(1f, a.CraftTotal) : 0f;
        int canResearch = 0;
        for (int k = 0; k < Defs.ItemCount; k++)
            if (w.ResearchCheck(a, (Item)k) == null) canResearch++;
        f[i++] = Math.Clamp(canResearch / 10f, 0f, 1f);

        // --- Tempo de vida e XP (2) ---
        f[i++] = Math.Clamp((w.Time - a.BornAt) / (w.Cfg.Cycle * 5f), 0f, 5f);   // em ciclos/5 (antes usava 960 fixo, que não vale no treino)
        f[i++] = a.Xp / 5000f;

        // --- Agentes: rival e colega mais próximos (6) ---
        EncodeNearestAgent(w, a, enemy: true, f, ref i);
        EncodeNearestAgent(w, a, enemy: false, f, ref i);

        // --- Item selecionado (2): qual slot e que tecnologia ele tem ---
        f[i++] = a.Slots.Length > 1 ? a.Selected / (float)(a.Slots.Length - 1) : 0f;
        var sel = w.SelectedItem(a);
        f[i++] = sel is Item si ? Defs.Tier(si) / 6f : 0f;

        return f;
    }

    static void EncodeNearestNodes(World w, Vector2 pos, float[] f, ref int i)
    {
        // uma única passada pelos nós, guardando o mais próximo de cada tipo
        var best = new float[NodeKinds.Length]; var bestPos = new Vector2[NodeKinds.Length];
        Array.Fill(best, float.MaxValue);
        foreach (var n in w.Nodes)
        {
            if (!n.Alive) continue;
            int k = Array.IndexOf(NodeKinds, n.Kind); if (k < 0) continue;
            float d = Vector2.DistanceSquared(n.Pos, pos);
            if (d < best[k]) { best[k] = d; bestPos[k] = n.Pos; }
        }
        for (int k = 0; k < NodeKinds.Length; k++)
        {
            if (best[k] == float.MaxValue) { f[i++] = 0f; f[i++] = 0f; f[i++] = 1f; continue; }   // nada encontrado → longe (antes: dist 0 = "colado")
            var dir = bestPos[k] - pos; float dist = MathF.Sqrt(best[k]);
            if (dist > 0.01f) dir /= dist;
            f[i++] = dir.X; f[i++] = dir.Y;
            f[i++] = Math.Clamp(dist / 1500f, 0f, 1f);
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

    static void EncodeNearestAgent(World w, Agent me, bool enemy, float[] f, ref int i)
    {
        float best = float.MaxValue; Agent? near = null;
        foreach (var o in w.Agents)
        {
            if (o == me || !o.Alive || (o.Team != me.Team) != enemy) continue;
            float d = Vector2.DistanceSquared(o.Pos, me.Pos);
            if (d < best) { best = d; near = o; }
        }
        if (near == null) { f[i++] = 0; f[i++] = 0; f[i++] = 1f; return; }
        var dir = near.Pos - me.Pos; float dist = MathF.Sqrt(best);
        if (dist > 0.01f) dir /= dist;
        f[i++] = dir.X; f[i++] = dir.Y; f[i++] = Math.Clamp(dist / 2000f, 0f, 1f);
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

    static bool NearBuilding(World w, Vector2 pos, Bld b) => w.FindStation(pos, b, 280f) >= 0;
}
