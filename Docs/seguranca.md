# Segurança e moderação

## Limites de conexão (anti flood)

| Limite | Valor | Onde mudar |
|---|---|---|
| Mensagens por segundo por conexão | 40, com folga de 80 para rajadas | `ServerOptions.MessagesPerSecond` / `MessageBurst` |
| Descartes que derrubam a conexão | 100 em 5 s | `InboundLimiter` |
| Tamanho de uma mensagem do cliente | 16 KB | `ClientConnection.MaxClientPayload` |
| Tempo para mandar o Hello | 15 s (recusa com "net.hello_timeout") | `HelloTimeoutSeconds` |
| Sem receber nada (o cliente manda ping a cada 1 s) | 30 s | `IdleTimeoutSeconds` |
| Conexões do mesmo IP | 8 (o próprio PC, 127.0.0.1, não conta) | `--max-per-ip N` |
| Chat | 5 mensagens seguidas, depois 1 a cada 1,5 s | `ChatRules` |
| Login | 5 senhas erradas travam a conta por 5 min | `AuthService` |

O cliente normal manda cerca de 21 mensagens por segundo (20 de movimento e 1 ping). 30 bots em grupo não foram
derrubados por nenhum limite.

## Nada decidido no cliente

O cliente só manda intenções (andar, usar a skill X no alvo Y, comprar Z). Dano, crítico, drop, XP, Alz, itens,
melhoria, troca e posição são decididos e conferidos pelo servidor com as regras do Shared. Como prova, o teste
`FuzzTests` manda milhares de mensagens de cliente com valores aleatórios e extremos (NaN, infinito, negativos, slots
e ids que não existem, posições absurdas) e confere depois que:

- o servidor não lançou nenhuma exceção;
- ninguém tem Alz negativo, vida fora de 0..máximo ou posição inválida;
- nenhum item foi duplicado (id único repetido), nenhuma pilha passou do limite e nenhum item é desconhecido.

## Detecção de bot

Só **marca suspeitos** (linha `[suspeita]` no log do servidor e `/gm suspeitos`); nunca pune sozinha.

| Sinal | Regra |
|---|---|
| Combo perfeito demais | 150 Excellent seguidos, sem nenhum Good ou Miss |
| Horas seguidas | 6 horas online sem sair |
| Rota repetida | o mesmo caminho (quadrados de 4 m, um ponto a cada 10 s) em 4 janelas de 5 minutos seguidas |

## Ferramentas de GM

Uma conta vira GM com o servidor desligado (grava no banco e sai):

```bash
dotnet run --project Server/Game.Server -- --set-gm nomedaconta
dotnet run --project Server/Game.Server -- --unset-gm nomedaconta
```

Na VPS (Docker), veja `Docs/deploy.md`: `docker compose -f docker-compose.prod.yml run --rm --no-deps game --set-gm conta`.

Para testes locais, `--gm conta1,conta2` trata as contas como GM só naquela execução.

No jogo, pelo chat (para quem não é GM o comando não existe):

| Comando | O que faz |
|---|---|
| `/gm info nome` | nível, classe, Alz, posição, conta e tempo online |
| `/gm kick nome motivo` | desconecta |
| `/gm ban nome horas motivo` | bane (0 horas = para sempre); funciona com o jogador offline |
| `/gm unban nome` | tira o banimento |
| `/gm mute nome minutos motivo` / `/gm unmute nome` | tira e devolve o chat |
| `/gm ir nome` / `/gm trazer nome` | teleporta o GM até o jogador, ou o jogador até o GM |
| `/gm dar nome item quantidade` / `/gm tirar nome item quantidade` | dá ou tira itens (ids como `nucleo_alto`) |
| `/gm alz nome valor` | soma ou tira Alz (ex.: `-500`; nunca fica negativo) |
| `/gm suspeitos` | lista quem a detecção de bot marcou |

Toda ação vai para a tabela `gm_actions` (quem, o quê, em quem, detalhes e quando) e para o log do servidor
(`[gm]`). Dar e tirar itens e Alz também entram no registro de itens do jogador (`gm_dar`, `gm_tirar`, `gm_alz`).
Conta banida não faz login nem entra no jogo com um token antigo; a mensagem diz até quando e o motivo.
