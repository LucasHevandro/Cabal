# Desempenho do cliente

Meta da Fase 7: **60 FPS em PC intermediário com 30 jogadores na tela.**

## Como medir

Servidor, 30 bots juntos no começo dos Campos do Vento e o cliente medindo por 20 segundos, sem limite de FPS:

```bash
dotnet run --project Server/Game.Server -- --port 7777 --db memory --guests
dotnet run --project Tools/Game.Bots -- --port 7777 --count 30 --seconds 180 --crowd
Client/Builds/Windows/Prototipo.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -connect 127.0.0.1:7777 -name Medidor -onlinetest saida-perf -onlineperf 20
```

O `relatorio.txt` traz o FPS médio, o 1% pior (o quadro mais lento entre os 99% mais rápidos), o pior quadro, as
coletas de lixo, a memória alocada por quadro e quantos jogadores e monstros estavam na tela. Depois mede de novo
com cada grupo da interface desligado, para mostrar quem aloca mais.

## Resultados (2026-10-17)

PC de teste: GeForce GTX 1660 Ti e Core i7-7700, 1920x1080, qualidade Alta, 30 jogadores e 9 monstros na tela.

| | FPS médio | 1% pior | Pior quadro | Coletas de lixo (20 s) | Alocação por quadro |
|---|---|---|---|---|---|
| Antes | 276 | 128 | 42 ms | 323 | — |
| Depois | 334 | 118 | 14 ms | 69 | 15 KB |

Com o limite padrão de 60 FPS sobra mais da metade do quadro livre mesmo no 1% pior, então a meta está cumprida
neste PC com folga. Num PC mais fraco o gargalo provável é a CPU (interface IMGUI e coleta de lixo), não a placa.

## O que foi otimizado

- **Estilos de texto:** `WithCenter()`/`WithAlignment()` criavam um `GUIStyle` novo a cada chamada (dezenas por quadro).
  Agora cada variação é criada uma vez e reaproveitada.
- **Sem passada de layout:** as janelas que só usam `GUI` (não `GUILayout`) desligam o `useGUILayout`; o `OnGUI` roda
  menos vezes por quadro.
- **Textos guardados:** plaquinhas "Nome Nv X", ajuda dos controles, teclas da barra de skills e textos com números
  (vida, mana, SP, XP) só são montados de novo quando mudam. Os nomes dos catálogos usam chaves guardadas
  (`Loc.Key`) em vez de juntar texto a cada leitura.
- O que ainda aloca são textos que mudam todo quadro (ping, tempo de recarga). Com 60 FPS isso dá cerca de 1 MB/s, e
  a coleta de lixo incremental do Unity (ligada no projeto) espalha o trabalho sem travar quadros.

## Próximos passos

- Medir num PC mais fraco (por exemplo GTX 1050 e i5 de 4 núcleos) quando houver um disponível.
- Quando os modelos de verdade chegarem, medir de novo: personagens com muitos materiais ou sem LOD pesam mais na placa.
