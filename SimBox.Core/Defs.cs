using System.Numerics;
using static SimBox.Item;
namespace SimBox;

// =====================================================================================================
//  Catálogo de dados do ambiente (itens, receitas, armas, ghouls, construções).
//  Valores marcados [wiki] vêm da wiki do devast.io; [user] vêm da sua lista; [chute] são estimativas minhas.
//  Unidades: distâncias em "u" (1 célula de construção = 100 u; raio do jogador = 30 u), tempo em segundos.
// =====================================================================================================
public enum Item
{
    // recursos brutos
    Wood, Stone, Ore, Sulfur, Scrap, Uranium, Meat, Hide, Sinew, Fat, Orange, Tomato, RottenOrange, RottenMeat, GhoulBlood,
    // processados
    Iron, Alloy, Nails, Yarn, Can, CookedMeat, OrangeSeed, TomatoSeed, Gasoline, Diesel, Bandage,
    // munição
    Arrow, BulletLow, BulletHigh, Shell,
    // ferramentas, armas, consumíveis
    AxeBasic, AxeIron, AxeSulfur, PickBasic, PickIron, PickSulfur, Hammer, Sledge,
    Spear, Bow, Pistol9, DesertEagle, MP5, AK47, Sniper, Shotgun, SawedOff, Flare,
    // vestimentas e armaduras (valem automaticamente quando estão no inventário)
    Headscarf, Chapka, WinterCoat, ArmorBear, ArmorSmith, ArmorGladiator,
    // colocáveis (a partir de Campfire tudo é colocável)
    Campfire, Workbench, ResearchTable, SewingTable, Chest, Fridge, Abrasador, Smelter, SpikeTrap, HallBot, LapadaBot,
    FloorWood, FloorStone, FloorIron, WallWood, WallStone, WallIron, ThinWallWood, ThinWallStone, ThinWallIron,
    GateWood, GateStone, GateIron, ThinGateWood, ThinGateStone, ThinGateIron
}

public enum Bld : byte
{
    None = 0, Campfire, Workbench, ResearchTable, SewingTable, Chest, Fridge, Abrasador, Smelter, SpikeTrap,
    FloorWood, FloorStone, FloorIron, WallWood, WallStone, WallIron, ThinWallWood, ThinWallStone, ThinWallIron,
    GateWood, GateStone, GateIron, ThinGateWood, ThinGateStone, ThinGateIron
}

public enum NodeKind : byte { Tree, Rock, IronOre, SulfurOre, Crate, OrangeBush, TomatoBush }
public enum GhoulKind { Normal, Fast, Explosive, Radioactive, Armored }
public enum Cmd { None = 0, Select, Craft, Research, Interact, Store, Take, Drop, Reload, Eat, Respawn }

public sealed record BldDef(string Name, char Glyph, float Hp, bool SolidMob, bool SolidAgent, bool IsFloor, bool BlocksBullets);
public sealed record Recipe(Item Out, int OutN, Bld Station, Bld Alt, (Item item, int n)[] In, float Secs, bool BotOk, int Level, int SkillPts, Item? Prereq);
public sealed record Gun(Item Item, Item Ammo, float Dmg, int Pellets, float Rate, int Mag, float Reload, float Speed, float Range, float SpreadDeg, float Stamina);
public sealed record ToolDef(Item Item, float Dmg, float Cd, float Stamina, (int lo, int hi) Wood, (int lo, int hi) Stone, (int lo, int hi) Iron, (int lo, int hi) Sulfur, int XpTree, int XpStone, int XpIron, int XpSulfur);
public sealed record GhoulDef(string Name, char Glyph, float Hp, float SpeedDay, float SpeedNight, float DmgDay, float DmgNight, float Radius, float Detect, float AttackCd, int UnlockCycle, int Xp);

public static class Defs
{
    public static readonly int ItemCount = Enum.GetValues<Item>().Length;
    public const int StackMax = 255;          // [wiki] pilha máxima 255
    public const float StatMax = 255f;        // [wiki] vida/fome/frio/stamina/radiação vão até 255

    public static readonly string[] ItemNames =
    {
        "Madeira","Pedra","Minério","Enxofre","Sucata","Urânio","Carne crua","Couro","Tendão","Gordura","Laranja","Tomate","Laranja podre","Carne podre","Sangue de ghoul",
        "Ferro","Alloy","Pregos","Linha","Lata","Carne assada","Semente de laranja","Semente de tomate","Gasolina","Diesel","Bandagem",
        "Flecha","Bala baixa","Bala alta","Cartucho",
        "Machado básico","Machado de ferro","Machado de enxofre","Picareta","Picareta de ferro","Picareta de enxofre","Martelo","Marreta",
        "Lança","Arco","9mm","Desert Eagle","MP5","AK-47","Sniper","Escopeta","Escopeta serrada","Flare",
        "Lenço","Chapka","Casaco de inverno","Armadura de urso","Armadura de ferreiro","Armadura de gladiador",
        "Fogueira","Mesa de trabalho","Mesa de pesquisa","Mesa de costura","Baú","Geladeira","Abrasador","Smelter","Armadilha de espinho","Hall 9000","Lapada bot",
        "Piso de madeira","Piso de pedra","Piso de ferro","Muro de madeira","Muro de pedra","Muro de ferro",
        "Muro fino de madeira","Muro fino de pedra","Muro fino de ferro",
        "Portão de madeira","Portão de pedra","Portão de ferro","Portão fino de madeira","Portão fino de pedra","Portão fino de ferro",
    };
    public static string Name(Item i) => ItemNames[(int)i];
    public static readonly string[] GhoulNames = { "Ghoul", "Ghoul veloz", "Ghoul explosivo", "Ghoul radioativo", "Ghoul blindado" };

    public static int Category(Item i) =>
        i <= GhoulBlood ? 0 : i <= Bandage ? 1 : i <= Shell ? 2 : i <= Sledge ? 3 : i <= Flare ? 4 : i <= ArmorGladiator ? 5 : 6;

    // ---- comida e consumíveis: (fome, vida, stamina)
    public static (float food, float hp, float stam)? Edible(Item i) => i switch
    {
        Meat => (30, -10, 20),           // [wiki]
        CookedMeat => (127, 0, 80),      // [wiki]
        Orange => (40, 0, 10),           // [wiki]
        Tomato => (40, 0, 10),           // [wiki]
        RottenOrange => (5, -40, 0),     // [wiki]
        RottenMeat => (15, -40, 0),      // [wiki]
        Bandage => (0, 60, 0),           // [wiki]
        _ => null
    };
    public static bool IsFood(Item i) => i == Meat || i == CookedMeat || i == Orange || i == Tomato;
    /// <summary>O baú guarda recursos, processados e munição (comida NÃO); a geladeira guarda só comida [user].</summary>
    public static readonly Item[] ChestItems = Enum.GetValues<Item>().Where(i => i <= Shell && !IsFood(i)).ToArray();
    public static readonly Item[] FridgeItems = { Meat, CookedMeat, Orange, Tomato };

    // ---- colocáveis
    public static readonly Item[] Placeables = Enum.GetValues<Item>().Skip((int)Campfire).ToArray();
    public static readonly Bld[] ItemToBld = MakeItemToBld();
    static Bld[] MakeItemToBld()
    {
        var a = new Bld[ItemCount];
        foreach (var it in Placeables) if (Enum.TryParse<Bld>(it.ToString(), out var b)) a[(int)it] = b;
        return a;
    }

    // HP de construções [wiki]: paredes 3000/7000/15000 (as "finas" têm a mesma força), portas 2500/6000/12500, portas baixas 2000/5000/10000,
    // fogueira 150, bancada 250, firepit 250, mesa de pesquisa 400, mesa de costura 250, baú 300, geladeira 300, smelter 3000. Armadilha [chute].
    public static readonly BldDef[] Blds =
    {
        new("-", ' ', 0, false, false, false, false),
        new("Fogueira", '^', 150, true, true, false, false),
        new("Mesa de trabalho", 'W', 250, true, true, false, true),
        new("Mesa de pesquisa", 'R', 400, true, true, false, true),
        new("Mesa de costura", 'S', 250, true, true, false, true),
        new("Baú", 'C', 300, true, true, false, false),
        new("Geladeira", 'G', 300, true, true, false, true),
        new("Abrasador", 'A', 250, true, true, false, false),       // firepit: dá para atirar por cima [wiki]
        new("Smelter", 'M', 3000, true, true, false, true),
        new("Armadilha de espinho", '%', 400, false, false, false, false),
        new("Piso de madeira", ':', 3000, false, false, true, false), new("Piso de pedra", ':', 7000, false, false, true, false), new("Piso de ferro", ':', 15000, false, false, true, false),
        new("Muro de madeira", '#', 3000, true, true, false, true), new("Muro de pedra", '#', 7000, true, true, false, true), new("Muro de ferro", '#', 15000, true, true, false, true),
        new("Muro fino de madeira", '=', 3000, true, true, false, false), new("Muro fino de pedra", '=', 7000, true, true, false, false), new("Muro fino de ferro", '=', 15000, true, true, false, false),
        new("Portão de madeira", '+', 2500, true, false, false, true), new("Portão de pedra", '+', 6000, true, false, false, true), new("Portão de ferro", '+', 12500, true, false, false, true),
        new("Portão fino de madeira", '+', 2000, true, false, false, false), new("Portão fino de pedra", '+', 5000, true, false, false, false), new("Portão fino de ferro", '+', 10000, true, false, false, false),
    };

    // ---- ghouls: tabela oficial do servidor privado [wiki/devast.io]: vida, velocidade dia/noite, dano dia/noite, hostil após N ciclos (16 min cada)
    // velocidade da tabela ×10 = u/s [chute de escala]. Raio/detecção/cooldown/XP normal-explosivo [chute]; XP veloz 625, radioativo 937, blindado 3125 [wiki].
    public static readonly GhoulDef[] Ghouls =
    {
        new("Ghoul",            'z', 160, 120, 220,  8, 20, 34, 700, 0.65f, 1, 312),
        new("Ghoul veloz",      'f', 120, 180, 280,  3,  7, 28, 450, 0.45f, 2, 625),
        new("Ghoul explosivo",  '!',  80, 120, 230,  6, 20, 32, 650, 0.55f, 3, 625),
        new("Ghoul radioativo", 'r', 160, 120, 230,  5, 15, 34, 650, 0.65f, 4, 937),
        new("Ghoul blindado",   'Z', 800, 120, 230, 20, 50, 44, 650, 0.90f, 5, 3125),
    };

    // ---- ferramentas [wiki: dano; hatchet 2-4 madeira e 1-3 pedra por golpe, 120 golpes/min; pick de ferro 4/8/12 pedra, 2 ferro, 1 enxofre; XP por coleta]
    public static readonly ToolDef Hand = new(Wood, 3, 0.5f, 3, (1, 2), (0, 0), (0, 0), (0, 0), 3, 0, 0, 0);
    public static readonly ToolDef[] Tools =
    {
        new(AxeBasic,  7,  0.5f, 6, (2, 4),  (1, 3),   (0, 0), (0, 0), 6, 9, 0, 0),
        new(PickBasic, 16, 0.6f, 8, (0, 0),  (4, 8),   (1, 1), (0, 0), 0, 28, 25, 0),
        new(PickIron,  22, 0.6f, 8, (0, 0),  (4, 12),  (2, 2), (1, 1), 0, 43, 50, 43),
        new(PickSulfur,30, 0.6f, 8, (0, 0),  (8, 16),  (3, 3), (2, 2), 0, 46, 75, 87),
        new(AxeIron,   26, 0.5f, 8, (4, 8),  (0, 0),   (0, 0), (0, 0), 12, 0, 0, 0),
        new(AxeSulfur, 32, 0.5f, 8, (6, 10), (0, 0),   (0, 0), (0, 0), 15, 0, 0, 0),   // dano do machado de enxofre [chute]
        new(Hammer,    30, 0.7f, 8, (0, 0),  (0, 0),   (0, 0), (0, 0), 0, 0, 0, 0),
        new(Sledge,    60, 1.0f, 12,(0, 0),  (0, 0),   (0, 0), (0, 0), 0, 0, 0, 0),     // marreta [user]: demole construções [chute: dano]
    };
    public static ToolDef? ToolOf(Item i) { foreach (var t in Tools) if (t.Item == i) return t; return null; }

    // ---- armas [wiki: dano, cadência, carregador]; velocidade, alcance, espalhamento, recarga [chute]
    public static readonly Gun[] Guns =
    {
        new(Bow,         Arrow,      40, 1, 0.8f, 0, 0,    900,  800,  1,  10),
        new(Pistol9,     BulletLow,  28, 1, 2f,   20, 1.6f, 1400, 700,  2,  0),
        new(DesertEagle, BulletLow,  40, 1, 2f,   7,  1.4f, 1500, 800,  1,  0),
        new(MP5,         BulletLow,  18, 1, 5.6f, 30, 2.0f, 1400, 750,  4,  0),
        new(AK47,        BulletHigh, 30, 1, 5.1f, 30, 2.4f, 1600, 900,  3,  0),
        new(Sniper,      BulletHigh, 90, 1, 1f,   10, 2.5f, 2400, 1800, 0.3f, 0),
        new(Shotgun,     Shell,      21, 5, 1f,   8,  2.5f, 1200, 450,  12, 0),
        new(SawedOff,    Shell,      28, 5, 1f,   8,  2.5f, 1100, 350,  16, 0),
    };
    public const float SpearDmg = 80, SpearSpeed = 1100, SpearRange = 600, SpearCd = 0.8f;     // lança é arremessada [wiki]
    public static Gun? GunOf(Item i) { foreach (var g in Guns) if (g.Item == i) return g; return null; }
    public static readonly float[] ArmorReduction = { 0f, 0.25f, 0.45f, 0.65f };               // corpo a corpo níveis 0..3 [chute]
    public static float ColdRate(Item worn) => 3.5f;

    // ---- XP: [wiki] 450, 950, 1450 nos 3 primeiros níveis; depois cresce ~160 por nível (≈26k no nível 20) [aproximação]
    public static int XpFor(int level)
    {
        if (level <= 0) return 0;
        double x = 0;
        for (int l = 0; l < level; l++) x += l < 3 ? (l == 0 ? 450 : 500) : 500 + 160 * (l - 3);
        return (int)x;
    }
    public static int LevelFor(float xp) { int l = 0; while (XpFor(l + 1) <= xp && l < 99) l++; return l; }

    static Recipe R(Item o, int n, Bld st, float secs, int lvl, int sp, Item? pre, params (Item, int)[] ins) => new(o, n, st, Bld.None, ins, secs, true, lvl, sp, pre);
    static Recipe Alt(Recipe r, Bld alt) => r with { Alt = alt };
    static Recipe NoBot(Recipe r) => r with { BotOk = false };

    // Custos [wiki] quando existem; [user] onde você deu números; resto [chute]. Tempos: armas [wiki], resto [chute].
    // Nível / pontos de habilidade = "pesquisa" (SP 0 = disponível desde o início). Níveis e pré-requisitos [wiki] quando conhecidos.
    public static readonly Recipe[] Recipes =
    {
        // ---- na mão
        Alt(R(AxeBasic, 1, Bld.None, 4, 1, 0, null, (Wood, 10), (Stone, 2)), Bld.Workbench),         // [wiki]
        R(Workbench, 1, Bld.None, 8, 1, 0, null, (Wood, 40), (Stone, 20)),                            // [wiki]
        R(Campfire, 1, Bld.None, 5, 1, 0, null, (Wood, 30), (Stone, 5)),                              // [wiki]
        NoBot(R(Flare, 1, Bld.None, 4, 1, 0, null, (Fat, 3), (Wood, 10))),                            // [user] gordura + madeira; bots não fazem
        R(Bow, 1, Bld.None, 50, 4, 1, null, (Wood, 200), (Sinew, 6)),                                 // [wiki] 200 madeira + 6 tendões; 50 s
        R(Arrow, 5, Bld.None, 10, 4, 1, null, (Wood, 40)),                                            // [wiki] 40 madeira; 5 flechas [chute]
        // ---- mesa de trabalho
        R(PickBasic, 1, Bld.Workbench, 15, 1, 1, null, (Wood, 150), (Stone, 30)),                    // [wiki]
        R(Spear, 1, Bld.Workbench, 15, 3, 1, null, (Wood, 70)),                                       // [wiki] 15 s
        R(Chest, 1, Bld.Workbench, 15, 8, 1, null, (Wood, 200), (Stone, 60)),                         // [wiki]
        R(SewingTable, 1, Bld.Workbench, 20, 2, 1, null, (Wood, 100), (Stone, 20)),                   // custo [chute]
        R(ResearchTable, 1, Bld.Workbench, 30, 6, 1, null, (Iron, 6), (Scrap, 3)),                    // [wiki] 6 chapas + peça eletrônica (→ sucata) [chute]
        Alt(R(Yarn, 1, Bld.Workbench, 5, 1, 0, null, (Sinew, 2)), Bld.SewingTable),                   // [wiki] 2 tendões
        R(Can, 1, Bld.Workbench, 5, 1, 0, null, (Iron, 1)),                                           // [user] sem função ainda
        R(Abrasador, 1, Bld.Workbench, 20, 4, 1, null, (Wood, 140), (Stone, 20), (Ore, 6)),           // [wiki] firepit
        R(SpikeTrap, 1, Bld.Workbench, 15, 4, 1, null, (Wood, 100), (Iron, 2)),                       // [chute]
        R(Nails, 85, Bld.Workbench, 5, 5, 0, null, (Iron, 1)),                                        // [user] 85 por barra
        R(Hammer, 1, Bld.Workbench, 20, 7, 1, null, (Wood, 250), (Iron, 10)),                         // [wiki]
        R(Bandage, 1, Bld.Workbench, 8, 5, 1, null, (Yarn, 1), (Hide, 2)),                            // [wiki]
        Alt(R(ArmorBear, 1, Bld.Workbench, 30, 8, 1, null, (Iron, 3), (Sinew, 3), (Hide, 3), (Nails, 80)), Bld.SewingTable), // [user]
        R(FloorWood, 1, Bld.Workbench, 3, 1, 0, null, (Wood, 15)),                                    // [wiki]
        R(WallWood, 1, Bld.Workbench, 5, 1, 0, null, (Wood, 20)),                                     // [wiki]
        R(ThinWallWood, 1, Bld.Workbench, 4, 1, 0, null, (Wood, 10)),                                 // custo [chute]; "half wall" tem a mesma força [wiki]
        R(GateWood, 1, Bld.Workbench, 6, 1, 0, null, (Wood, 60)),                                     // [wiki] wooden door
        R(ThinGateWood, 1, Bld.Workbench, 5, 1, 0, null, (Wood, 40)),                                 // [wiki] low wood door
        R(FloorStone, 1, Bld.Workbench, 3, 3, 1, null, (Stone, 15)),                                  // custo [chute]
        R(WallStone, 1, Bld.Workbench, 6, 3, 1, null, (Stone, 20)),                                   // [wiki]
        R(ThinWallStone, 1, Bld.Workbench, 5, 3, 1, null, (Stone, 10)),                               // custo [chute]
        R(GateStone, 1, Bld.Workbench, 8, 3, 1, null, (Stone, 60)),                                   // [wiki] stone door
        R(ThinGateStone, 1, Bld.Workbench, 6, 3, 1, null, (Stone, 40)),                               // [wiki] low stone door
        // ---- mesa de costura (tecelagem)
        Alt(R(Headscarf, 1, Bld.SewingTable, 10, 5, 1, null, (Yarn, 4), (Hide, 6)), Bld.None),        // [wiki]
        R(Chapka, 1, Bld.SewingTable, 15, 7, 1, Headscarf, (Yarn, 10), (Hide, 15), (Headscarf, 1)),   // [wiki]
        R(WinterCoat, 1, Bld.SewingTable, 20, 9, 1, Chapka, (Yarn, 20), (Hide, 30), (Chapka, 1)),     // [wiki]
        // ---- mesa de pesquisa
        R(AxeIron, 1, Bld.ResearchTable, 20, 5, 1, null, (Wood, 150), (Iron, 7)),                     // [wiki] steel axe
        R(PickIron, 1, Bld.ResearchTable, 20, 6, 1, PickBasic, (Stone, 200), (Iron, 6)),              // [wiki] steel pickaxe
        R(PickSulfur, 1, Bld.ResearchTable, 30, 9, 1, PickIron, (Iron, 30), (Sulfur, 20), (Alloy, 5)),// [wiki] + alloys [chute]
        R(AxeSulfur, 1, Bld.ResearchTable, 30, 9, 1, AxeIron, (Wood, 150), (Iron, 25), (Sulfur, 15), (Alloy, 5)), // [chute]
        R(Gasoline, 1, Bld.ResearchTable, 10, 1, 0, null, (RottenOrange, 4), (Sulfur, 1)),            // [user] (wiki: 15 + 2)
        NoBot(R(Diesel, 1, Bld.ResearchTable, 15, 10, 1, null, (Gasoline, 1), (Fat, 4))),             // [user] bots não fazem
        R(ArmorSmith, 1, Bld.ResearchTable, 40, 11, 1, ArmorBear, (Alloy, 8), (Iron, 6), (Hide, 4), (Nails, 40)),   // [user: usa alloy] custo [chute]
        R(ArmorGladiator, 1, Bld.ResearchTable, 50, 14, 1, ArmorSmith, (Alloy, 16), (Iron, 8), (Hide, 6), (Nails, 60)), // [user] [chute]
        R(Sledge, 1, Bld.ResearchTable, 25, 10, 1, null, (Wood, 100), (Iron, 10)),                    // [user] [chute]
        R(Smelter, 1, Bld.ResearchTable, 40, 10, 1, null, (Iron, 20), (Stone, 200), (Sulfur, 30)),    // [wiki] sem o urânio (era dos alloys)
        R(Fridge, 1, Bld.ResearchTable, 30, 9, 1, null, (Iron, 10), (Sulfur, 30)),                    // [wiki]
        R(HallBot, 1, Bld.ResearchTable, 60, 12, 1, null, (Alloy, 10), (Iron, 8), (Scrap, 10)),       // [user: usa alloy] [chute]
        R(LapadaBot, 1, Bld.ResearchTable, 50, 11, 1, null, (Alloy, 8), (Iron, 6), (Scrap, 6)),       // [user: usa alloy] [chute]
        R(FloorIron, 1, Bld.ResearchTable, 5, 6, 1, FloorStone, (Iron, 2)),                           // [chute]
        R(WallIron, 1, Bld.ResearchTable, 8, 6, 1, WallStone, (Iron, 3)),                             // [wiki] steel wall
        R(ThinWallIron, 1, Bld.ResearchTable, 6, 6, 1, ThinWallStone, (Iron, 2)),                     // [chute]
        R(GateIron, 1, Bld.ResearchTable, 10, 6, 1, GateStone, (Iron, 15)),                           // [wiki] steel door
        R(ThinGateIron, 1, Bld.ResearchTable, 8, 6, 1, ThinGateStone, (Iron, 10)),                    // [wiki] low steel door
        R(Pistol9, 1, Bld.ResearchTable, 160, 7, 1, null, (Scrap, 6)),                                // [wiki] 6 sucata, 2m40s (9mm: sem alloy [user])
        R(DesertEagle, 1, Bld.ResearchTable, 180, 9, 1, Pistol9, (Alloy, 4)),                         // [wiki]
        R(MP5, 1, Bld.ResearchTable, 200, 10, 1, null, (Alloy, 6)),                                   // [wiki]
        R(AK47, 1, Bld.ResearchTable, 180, 12, 1, null, (Alloy, 14)),                                 // [wiki]
        R(Sniper, 1, Bld.ResearchTable, 180, 13, 1, null, (Alloy, 10)),                               // [wiki]
        R(Shotgun, 1, Bld.ResearchTable, 200, 11, 1, null, (Alloy, 6)),                               // [wiki]
        R(SawedOff, 1, Bld.ResearchTable, 200, 13, 1, Shotgun, (Shotgun, 1), (Alloy, 6)),             // [wiki]
        R(BulletLow, 20, Bld.ResearchTable, 15, 6, 1, null, (Sulfur, 9), (Iron, 4), (Fat, 10)),       // [wiki] custo; 20 por receita [chute]
        R(BulletHigh, 20, Bld.ResearchTable, 20, 11, 1, BulletLow, (Sulfur, 20), (Iron, 8), (Fat, 15), (Alloy, 1)), // [wiki] + alloy [user]
        R(Shell, 10, Bld.ResearchTable, 20, 10, 1, null, (Sulfur, 15), (Iron, 5), (Fat, 15), (Alloy, 1)),           // [wiki] + alloy [user]
        // ---- smelter (gasta combustível: 1 gasolina = 4 operações, 1 diesel = 12 [user])
        R(Alloy, 1, Bld.Smelter, 20, 1, 0, null, (Scrap, 1), (Ore, 1), (Sulfur, 1)),                  // [user]
        R(Iron, 2, Bld.Smelter, 10, 1, 0, null, (Ore, 2), (Iron, 1)),                                 // [user] muito mais rápido que o abrasador
        // ---- fogueira / abrasador
        R(OrangeSeed, 1, Bld.Campfire, 5, 1, 0, null, (Orange, 4)),                                   // [wiki]
        R(TomatoSeed, 1, Bld.Campfire, 5, 1, 0, null, (Tomato, 4)),                                   // [wiki]
        R(CookedMeat, 1, Bld.Campfire, 10, 1, 0, null, (Meat, 1)),                                    // [wiki]
        R(Iron, 1, Bld.Abrasador, 30, 1, 0, null, (Ore, 2)),                                          // [user] só no abrasador; devagar
    };
}

public sealed class SimConfig
{
    public int Tps = 30;
    public float WorldW = 6000, WorldH = 6000, Cell = 100;
    public float DayLen = 480, NightLen = 480;              // [wiki] 1 dia = 1 noite = 8 min
    public float CraftTimeScale = 1f;                       // 1 = tempos da wiki; menor = fabricar mais rápido (testes)
    public int InventorySlots = 8, ChestSlots = 24;         // [chute] (wiki: 7 slots úteis no início)
    public float WalkSpeed = 260, SprintSpeed = 400, CraftSlow = 0.65f;   // [chute de escala]
    public int Trees = 900, Rocks = 300, IronNodes = 140, SulfurNodes = 70, Crates = 60, Bushes = 110, Animals = 45;
    public float HungerRate = 1.2f;                         // [wiki]
    public float ColdRate = 3.5f, ColdRegenFire = 5f, ColdRegenDay = 4f;   // [wiki] (regen de dia [chute])
    public float StaminaDrain = 28f, StaminaRegen = 14f;    // [wiki]
    public float HealthRegen = 3f, StarveDamage = 4f, FreezeDamage = 5f; // [chute]
    public float FireRadius = 200, FireWoodSeconds = 20f, FireMaxSeconds = 600f;
    public float GhoulStructMult = 5f;                      // dano de ghoul em construções [chute]
    public float SunDeathSeconds = 40f;                     // ghouls "começam a morrer" quando amanhece [wiki]
    public float RotCheckSeconds = 60f, RotChance = 0.10f;  // comida fora da geladeira apodrece [chute]
    public float PlantLifeDays = 5f, PlantGrowSeconds = 45f;
    public float ReachBuild = 260f, PickupRadius = 70f;
    public int NightGhoulsBase = 4, NightGhoulsPerNight = 2;
    public float FlareSeconds = 5f, FlareRadius = 400f;
    public float Cycle => DayLen + NightLen;
}

public sealed class Rng
{
    ulong s;
    public Rng(int seed) { s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL; if (s == 0) s = 1; }
    public uint Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (uint)(s >> 32); }
    public int Int(int n) => (int)(Next() % (uint)n);
    public int Range(int lo, int hi) => lo >= hi ? lo : lo + Int(hi - lo + 1);
    public float Float() => (Next() >> 8) * (1f / 16777216f);
    public float Float(float lo, float hi) => lo + (hi - lo) * Float();
    public bool Chance(float p) => Float() < p;
}
