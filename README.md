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

    dotnet run --project SimBox.Tests -c Release              # 96 testes automáticos do motor
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
