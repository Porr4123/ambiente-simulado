# SimBox v4 — devast.io 2D com física livre

Requer o **.NET 8 SDK** (Visual Studio 2022 17.8+ ou `dotnet` na linha de comando).

## Jogar

    dotnet run --project SimBox.Web -c Release

Abre http://localhost:5050 no navegador. No Visual Studio: abra `SimBox.sln`, defina **SimBox.Web** como projeto de
inicialização e use **Ctrl+F5**.

Opções: `-- --day 120 --night 60` (dia/noite mais curtos, em segundos; padrão da wiki: 480/480),
`--craftscale 0.2` (fabricar 5x mais rápido; 1 = tempos da wiki), `--seed 3`, `--port 5051`.

## Controles
WASD anda · Shift corre (gasta stamina) · mouse mira · clique esquerdo usa o item selecionado (golpe, coleta, tiro, comer, colocar)
1-8 / roda do mouse escolhem o slot · R recarrega · Q solta · C fabricar · K pesquisar · E alimentar fogo · F guardar / G pegar (baú, geladeira) · F1 ajuda

## Testes e desempenho

    dotnet run --project SimBox.Tests -c Release              # testes automáticos do motor (inclui regras de time e ferramentas)
    dotnet run --project SimBox.Tests -c Release -- --bench   # velocidade com 16 agentes

## Onde editar
- `SimBox.Core/Defs.cs`: itens, receitas (custo, tempo, nível, pontos de pesquisa), armas, ferramentas, ghouls, construções, configuração.
  Cada valor está marcado `[wiki]`, `[user]` (sua lista) ou `[chute]` (estimativa minha).
- `SimBox.Core/World*.cs`: regras (física, jogador, combate, ghouls, robôs).
- `SimBox.Web/`: servidor local + `index.html` (gráficos).

## Para os bots (ML)
`World.Step(AgentInput[])`: cada agente recebe `{Mx, My, Aim, AimDist, Sprint, Attack, Cmd, Arg}` (movimento e mira contínuos;
`Cmd` = selecionar slot, fabricar, pesquisar, interagir, guardar, pegar, soltar, recarregar, comer, renascer).
`Rewards`, `Reset(seed)`, `Done`. Receitas com `BotOk=false` (flare, diesel) são recusadas para bots.
`World.AccumulateRewards = true` faz o `Rewards` acumular até o próximo `Reset()` (treino por gerações); com `false` (padrão) ele zera a cada `Step`.

## Treino dos agentes (neuroevolução)

    dotnet run --project SimBox.Train -c Release -- [--gens N] [--seed N] [--out runs] [--port 5080] [--speed X] [--resume runs/run_...] [--noopen] [--nomonitor] [--quiet]

Sem `--gens` o treino é **ilimitado**: pare com **Ctrl+S** no console (termina ao fim da geração em curso, que é gravada; um 2º Ctrl+S encerra a geração em curso já). `--gens N` define um limite opcional.

32 agentes (2 times de 16, time = índice % 2) vivem juntos num mundo por geração (1 "semana" = 7 ciclos nas primeiras 28 gerações, depois 2).
Rede `features → 80 → 48 → 32 → 16` (tanh). Os 8 melhores viram elite; o resto nasce de cruzamento + mutação. Os times são sempre 16×16.

**Regras**
- **Muro**: um muro vertical (1 célula) no meio do mapa separa os times (barra agentes, ghouls, tiros, flechas e lanças). Ele some nos **2 últimos dias** da simulação (`SimConfig.TeamWall`/`WallOpenTime`).
- O reward só zera a cada nova geração. Agentes mortos só renascem na próxima geração (a geração acaba antes se todos morrem).
- fitness = reward da geração + 100 (vivo no fim) / −50 (morto) + 2·nível.
- Times (valores em `Rw`, `SimBox.Core/Defs.cs`): fogo amigo e matar amigo penalizam (−0,08/dano e −150); acertar e matar rival bonificam (+0,05/dano e +80);
  acertar e matar ghouls também bonificam. O bônus de combate é multiplicado pela tecnologia da arma usada (`Defs.Tier`/`TechMult`: mão 1,0 … AK-47/sniper 3,5).
  Projéteis e golpes acertam qualquer agente (menos o dono).
- Ferramentas: fabricar dá bônus grande (`Defs.ToolBonus`: 10/25/50), **uma vez por item por agente por geração**. Ao ter uma ferramenta melhor da mesma família
  (machado › machado de ferro › machado de enxofre; idem picareta), a anterior é doada ao colega de time próximo que esteja pior equipado
  (o doador ganha bônus, 1x por item) ou jogada fora. Bots não refazem ferramenta igual/pior à que já têm.
- Estações de trabalho: fabricar também dá bônus grande (`Defs.StationBonus`: fogueira 8, bancada/abrasador 20, mesa de costura 25, mesa de pesquisa 40, smelter 60), 1x por item por agente por geração.
- Comer: comer/curar **sem desperdício** (sem passar de 255 de fome/vida) dá bônus de `Rw.EatPerPoint` (0,06) por ponto recuperado; comer desperdiçando, ou comida que fere (carne crua/podre), não paga bônus.

**Registro** em `runs/run_AAAAMMDD_HHMMSS/`: `summary.csv`, e por geração `gen_0001/log.txt` (resumo, tabela dos 32 agentes, eventos) +
`top1_...brain` … `top8_...brain` (os 8 melhores). `--resume runs/run_...` retoma da última geração gravada.

**Monitor duplo** (abre duas abas; arraste uma para cada monitor): `http://localhost:5080/` = mapa (agentes azul/vermelho, recursos amarelos,
construções quadrados cinza, ghouls verdes) e `http://localhost:5080/focus` = câmera no top 1 atual (status, inventário, decisão da rede, eventos).
Use `--speed 5` para acompanhar em 5× tempo real; sem `--speed` roda o mais rápido possível. No painel de foco, o botão **🐢 Slow** desacelera a simulação até 1× tempo real para acompanhar o agente (clique de novo para voltar à velocidade original). O muro aparece como faixa cinza em ambas as telas.
