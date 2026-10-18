# Rodada de correção de bugs — Fase 7

Achados rodando todos os testes automáticos (online, interface, social, dungeon, arena, offline), carga com 30 bots
(em grupo, Debug e Release) e olhando as capturas. Prioridade: **alta** = o jogador perde informação ou não consegue
fazer algo; **média** = atrapalha ou parece quebrado; **baixa** = detalhe ou só para quem desenvolve.

| # | Prioridade | Problema | Situação |
|---|---|---|---|
| 1 | Alta | Avisos do servidor ("Comprou…", "Vendeu…", erros da ferreira) ficavam escondidos atrás da loja, da troca e da ferreira | Corrigido: com essas janelas abertas o aviso vai para o espaço livre à esquerda |
| 2 | Alta | Nomes dos NPCs escritos por cima do painel de ajuda, do minimapa e do quadro do jogador | Corrigido: o nome não aparece onde há painel do HUD ou janela aberta |
| 3 | Alta | Painel do tutorial por cima da ferreira e das conversas | Corrigido: o tutorial some enquanto há conversa ou janela do lado direito |
| 4 | Média | Barra com 8 skills cortava a linha "mágico · 25 MP" | Corrigido: letra menor nessa linha quando há mais de 4 skills |
| 5 | Média | Painel de controles sempre aberto, ocupando o canto esquerdo | Corrigido: F1 esconde e mostra (atalho configurável), a escolha fica salva |
| 6 | Média | Servidor fechava com o erro técnico quando a porta (ou a da API) já estava em uso | Corrigido: mensagem clara dizendo qual porta e o que fazer |
| 7 | Baixa | Cliente de outra versão não conseguia ler a recusa do servidor ("atualize o jogo") se a mensagem tivesse mudado | Corrigido daqui para a frente: a recusa é lida mesmo com campos a mais (a v9 antiga continua sem ler) |
| 8 | Baixa | Avisos de API obsoleta do Unity no teste automático | Corrigido |
| 9 | Baixa | Anel dourado da subida de nível fica fraco em chão claro (brilho aditivo) | Aberto: revisar quando houver chão com textura |
| 10 | Baixa | Tick do servidor em Debug é ~4x o de Release (4 ms contra 1 ms com 30 bots em grupo) | Não é bug: medições de carga devem usar Release (`Docs/testes-de-carga.md`) |
| 11 | — | Testes com PostgreSQL de verdade não rodam com o Docker Desktop desligado | Ambiente: ligar o Docker antes de `dotnet test` com `GAME_DB_TEST` |

## Conferido sem problemas

- Teste social completo (grupo, chat, filtro, amigos, bloqueio, denúncia, loja, troca, duelo).
- Dungeon (cripta vencida, medalha e ranking), arena 1v1 com bot, modo offline com o boneco e o chefe.
- 30 bots em grupo por 60 s em Release: 20 ticks/s, tick médio 0,9–1,1 ms, máximo 4 ms, nenhum erro no servidor.
- 312 testes automáticos (195 do Shared e 117 do servidor) passando.
