# Balanceamento: nível 1 → 30

Meta do GDD: **8 a 12 horas** de jogo do nível 1 ao 30. Conferido pelo simulador
`Shared/Game.Shared/Progression/LevelingSimulation.cs` (o teste `LevelingBalanceTests` falha se sair da faixa ou
se um nível sozinho passar de 8% do tempo total). Para ver a tabela atualizada:

```bash
dotnet run --project Tools/Game.Balance -- --markdown
```

## O que o simulador faz

- Usa as regras reais do `Shared`: atributos (60% no atributo principal, 40% em destreza), equipamento completo do
  grau do nível (Ferro até o 9, Aço Negro do 10 ao 19, Astralita do 20 em diante) com melhoria média +2/+4/+5,
  skills liberadas por nível e missão, fórmula de dano contra a defesa do monstro, crítico e MP.
- A cada nível escolhe o monstro que dá mais XP por segundo entre os de até 6 níveis abaixo e 5 acima.
- Soma a XP das missões do catálogo quando o nível e a missão anterior permitem.
- Premissas de um jogador típico: 0,9 s por ação, 7 s entre um monstro e outro, 20% do tempo parado (vila,
  inventário, descanso), +15% de dano médio pelo combo depois de liberado, 3 min extras por missão.
- **Não conta** skills em área acertando vários monstros: na prática quem junta monstros sobe mais rápido. Por isso
  a meta ficou no meio da faixa, com folga.

## Ajustes feitos (2026-10-17)

| O quê | Antes | Depois | Por quê |
|---|---|---|---|
| Curva de XP | `80·N^1,6 + 20·N` | `450·N^1,6 + 110·N` | 1→30 levava ~2 h |
| Vida dos monstros comuns | — | ×2 | morriam em 2–5 s: pouco tempo para o combo |
| XP das missões | — | ×2 | manter as missões valendo ~1 nível cada |
| Monstro novo | — | Cavaleiro Caído (Nv 29, Ruínas) | do 27 ao 29 não havia monstro do nível: o nível 29 levava 67 min |

## Resultado

### Lâmina: 10,4 horas

| Nível | Caça | s/abate | XP/abate | Abates | XP de missão | Minutos | Acumulado (h) |
|---:|---|---:|---:|---:|---:|---:|---:|
| 1 | lobo_cinzento (Nv 6) | 14,2 | 105 | 3 | 300 | 4 | 0,1 |
| 2 | lobo_cinzento (Nv 6) | 13,8 | 98 | 17 | 0 | 5 | 0,1 |
| 3 | lobo_cinzento (Nv 6) | 12,6 | 91 | 20 | 1200 | 8 | 0,3 |
| 4 | lobo_cinzento (Nv 6) | 12,3 | 84 | 55 | 0 | 14 | 0,5 |
| 5 | lobo_cinzento (Nv 6) | 12,0 | 77 | 84 | 0 | 21 | 0,9 |
| 6 | lobo_cinzento (Nv 6) | 11,8 | 70 | 66 | 4000 | 19 | 1,2 |
| 7 | javali_furioso (Nv 12) | 19,1 | 300 | 37 | 0 | 15 | 1,4 |
| 8 | javali_furioso (Nv 12) | 17,9 | 280 | 48 | 0 | 18 | 1,7 |
| 9 | javali_furioso (Nv 12) | 17,5 | 260 | 63 | 0 | 23 | 2,1 |
| 10 | javali_furioso (Nv 12) | 15,7 | 240 | 38 | 10000 | 15 | 2,4 |
| 11 | aranha_da_mata (Nv 16) | 16,8 | 450 | 50 | 0 | 17 | 2,7 |
| 12 | aranha_da_mata (Nv 16) | 16,1 | 420 | 61 | 0 | 20 | 3,0 |
| 13 | aranha_da_mata (Nv 16) | 15,8 | 390 | 74 | 0 | 24 | 3,4 |
| 14 | aranha_da_mata (Nv 16) | 15,5 | 360 | 90 | 0 | 29 | 3,9 |
| 15 | aranha_da_mata (Nv 16) | 15,3 | 330 | 55 | 18000 | 21 | 4,2 |
| 16 | aranha_da_mata (Nv 16) | 15,1 | 300 | 133 | 0 | 42 | 4,9 |
| 17 | golem_de_pedra (Nv 22) | 30,7 | 1050 | 42 | 0 | 27 | 5,4 |
| 18 | golem_de_pedra (Nv 22) | 30,1 | 980 | 49 | 0 | 31 | 5,9 |
| 19 | golem_de_pedra (Nv 22) | 29,5 | 910 | 58 | 0 | 36 | 6,5 |
| 20 | golem_de_pedra (Nv 22) | 25,4 | 840 | 34 | 28000 | 21 | 6,8 |
| 21 | espectro (Nv 26) | 18,0 | 1350 | 46 | 0 | 17 | 7,1 |
| 22 | espectro (Nv 26) | 17,8 | 1260 | 53 | 0 | 20 | 7,5 |
| 23 | espectro (Nv 26) | 17,6 | 1170 | 61 | 0 | 22 | 7,8 |
| 24 | cavaleiro_caido (Nv 29) | 26,0 | 2100 | 36 | 0 | 19 | 8,1 |
| 25 | cavaleiro_caido (Nv 29) | 25,0 | 1960 | 21 | 40000 | 14 | 8,4 |
| 26 | cavaleiro_caido (Nv 29) | 24,7 | 1820 | 47 | 0 | 24 | 8,8 |
| 27 | cavaleiro_caido (Nv 29) | 24,3 | 1680 | 55 | 0 | 28 | 9,2 |
| 28 | cavaleiro_caido (Nv 29) | 24,0 | 1540 | 63 | 0 | 32 | 9,8 |
| 29 | cavaleiro_caido (Nv 29) | 23,7 | 1400 | 73 | 0 | 36 | 10,4 |

### Arcanista: 10,5 horas

| Nível | Caça | s/abate | XP/abate | Abates | XP de missão | Minutos | Acumulado (h) |
|---:|---|---:|---:|---:|---:|---:|---:|
| 1 | lobo_cinzento (Nv 6) | 13,8 | 105 | 3 | 300 | 4 | 0,1 |
| 2 | lobo_cinzento (Nv 6) | 13,4 | 98 | 17 | 0 | 5 | 0,1 |
| 3 | lobo_cinzento (Nv 6) | 12,3 | 91 | 20 | 1200 | 8 | 0,3 |
| 4 | lobo_cinzento (Nv 6) | 12,0 | 84 | 55 | 0 | 14 | 0,5 |
| 5 | lobo_cinzento (Nv 6) | 11,7 | 77 | 84 | 0 | 20 | 0,8 |
| 6 | lobo_cinzento (Nv 6) | 11,5 | 70 | 66 | 4000 | 19 | 1,2 |
| 7 | javali_furioso (Nv 12) | 18,4 | 300 | 37 | 0 | 14 | 1,4 |
| 8 | javali_furioso (Nv 12) | 17,9 | 280 | 48 | 0 | 18 | 1,7 |
| 9 | javali_furioso (Nv 12) | 17,4 | 260 | 63 | 0 | 23 | 2,1 |
| 10 | javali_furioso (Nv 12) | 15,6 | 240 | 38 | 10000 | 15 | 2,3 |
| 11 | aranha_da_mata (Nv 16) | 16,7 | 450 | 50 | 0 | 17 | 2,6 |
| 12 | aranha_da_mata (Nv 16) | 16,2 | 420 | 61 | 0 | 21 | 3,0 |
| 13 | aranha_da_mata (Nv 16) | 15,9 | 390 | 74 | 0 | 25 | 3,4 |
| 14 | aranha_da_mata (Nv 16) | 15,7 | 360 | 90 | 0 | 29 | 3,9 |
| 15 | aranha_da_mata (Nv 16) | 15,4 | 330 | 55 | 18000 | 21 | 4,2 |
| 16 | aranha_da_mata (Nv 16) | 15,1 | 300 | 133 | 0 | 42 | 4,9 |
| 17 | golem_de_pedra (Nv 22) | 31,0 | 1050 | 42 | 0 | 27 | 5,4 |
| 18 | golem_de_pedra (Nv 22) | 30,3 | 980 | 49 | 0 | 31 | 5,9 |
| 19 | golem_de_pedra (Nv 22) | 29,7 | 910 | 58 | 0 | 36 | 6,5 |
| 20 | golem_de_pedra (Nv 22) | 25,8 | 840 | 34 | 28000 | 21 | 6,8 |
| 21 | espectro (Nv 26) | 18,3 | 1350 | 46 | 0 | 18 | 7,1 |
| 22 | espectro (Nv 26) | 18,0 | 1260 | 53 | 0 | 20 | 7,4 |
| 23 | espectro (Nv 26) | 17,8 | 1170 | 61 | 0 | 23 | 7,8 |
| 24 | cavaleiro_caido (Nv 29) | 26,3 | 2100 | 36 | 0 | 20 | 8,2 |
| 25 | cavaleiro_caido (Nv 29) | 25,9 | 1960 | 21 | 40000 | 14 | 8,4 |
| 26 | cavaleiro_caido (Nv 29) | 25,5 | 1820 | 47 | 0 | 25 | 8,8 |
| 27 | cavaleiro_caido (Nv 29) | 25,1 | 1680 | 55 | 0 | 29 | 9,3 |
| 28 | cavaleiro_caido (Nv 29) | 24,8 | 1540 | 63 | 0 | 33 | 9,8 |
| 29 | cavaleiro_caido (Nv 29) | 24,4 | 1400 | 73 | 0 | 37 | 10,5 |

### Sensibilidade

Jogador rápido = 4 s entre monstros, 8% parado, +30% pelo combo; sem pressa = 10 s, 30% parado, +5%.

- Lâmina: jogador rápido 7,1 h, sem pressa 14,3 h
- Arcanista: jogador rápido 7,1 h, sem pressa 14,4 h

## Economia (2026-10-17)

`dotnet run --project Tools/Game.Balance` também estima o Alz de 1 a 30 com a mesma caçada da simulação acima.

- **Entra:** o Alz dos monstros e a venda dos drops na mercadora. Núcleos e pedras não são vendidos: vão para a
  melhoria.
- **Sai:** melhoria das 5 peças de cada grau até o nível médio (+2, +4 e +5), núcleos que faltarem (comprados na
  loja), 25 poções por hora de caça e o conjunto de Ferro da loja.

| Faixa | Horas | Entra | Sai | Saldo |
|---|---:|---:|---:|---:|
| Nv 1–10 (Ferro) | 2,1 | 23.800 | 7.015 | 16.785 |
| Nv 10–20 (Aço Negro) | 4,4 | 85.119 | 50.778 | 34.341 |
| Nv 20–30 (Astralita) | 3,9 | 126.595 | 115.494 | 11.101 |

Valores da Lâmina; o Arcanista fica praticamente igual.

- No total entram cerca de 235 mil Alz e saem 74%. Sobram cerca de 62 mil no nível 30, para trocas, para a entrada
  da dungeon e para tentar passar do +5.
- Os núcleos baixos que caem bastam para o Ferro. Por isso a compra de núcleos aparece zerada: a loja é só uma
  reserva para quem tiver azar.
- A faixa 20–30 é a mais apertada, porque a melhoria de Astralita custa caro. É de propósito: o +6 em diante, com
  risco de queda, fica para depois do nível 30.
- O teste `Economia_fecha_positiva_em_cada_faixa_e_gasta_boa_parte` exige saldo positivo em cada faixa e um gasto
  entre 60% e 90% do que entra.
- **Revisar** quando a dungeon (entrada e baú) e o PvP entrarem na conta.

## Cripta Esquecida (2026-10-17)

Estimativa do `Tools/Game.Balance`. Soma a vida dos monstros de cada sala e divide pelo dano por segundo do grupo
(o mesmo cálculo da subida de nível). Acrescenta 12 s por grupo de monstros, 15 s por sala, e no chefe os servos dos
dois escudos e 15% de dano perdido fugindo do golpe no chão.

| Grupo | Tempo |
|---|---:|
| 4 no nível 22 (2 Lâminas, 2 Arcanistas) | 10:06 |
| 4 no nível 18 (mínimo) | 12:22 |
| 4 no nível 28 | 9:08 |
| 2 no nível 22 | 17:37 (não termina) |

- A primeira versão levava 4 minutos. A vida dos monstros da dungeon foi multiplicada por 5 (e o XP por 3), para um
  grupo típico ficar perto de 10 minutos e a medalha S (até 8 minutos com combo de 12) pedir jogo bom.
- O teste `Grupo_de_quatro_termina_em_menos_de_15_minutos_e_um_grupo_bom_tira_S` cobra que:
  - o grupo típico fique entre 8 e 13 minutos;
  - o grupo no nível mínimo termine antes do limite;
  - um grupo bom (+7, 35% de bônus de combo) tire S.
- **Revisar** com grupos de verdade: os 12 s por grupo de monstros são um chute.
