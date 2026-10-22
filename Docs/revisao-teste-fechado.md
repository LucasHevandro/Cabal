# Revisão para o teste fechado (2026-10-22)

Revisão feita antes do teste com 20 jogadores no servidor público. Foi feita lendo o código, os commits recentes, o deploy
e a VM de produção (só leitura). Nada foi exercitado com cliente modificado, então os ataques descritos são deduzidos do
código. Os achados marcados como **conferido** foram relidos no código depois do levantamento.

## Corrigido em 2026-10-22

- **2. Elite com 25% da vida:** `RollElite` enche a vida; o teste confere a vida cheia ao nascer.
- **3. Login e cadastro:** no máximo 10 pedidos por minuto por IP (`AuthService.MaxAuthRequestsPerMinute`), hashes de
  senha limitados à metade dos núcleos, cadastro de nome repetido recusado antes do hash.
- **4. Fila de envio:** a conexão cai com mais de 4 MB parados na fila (`ClientConnection.MaxPendingBytes`).
- **6. Skill de outra instância:** skill, finalizador e golpes em área só acertam monstros da instância do jogador.
- **7. Salvamento descartado:** a foto que falha a rodada inteira não se perde. Os registros dela vão junto com a próxima
  foto do personagem, e ela é tentada de novo com a fila vazia (até 5 rodadas). Enquanto isso, o personagem não entra de
  novo. Oferta de vendedor apagado deixa de quebrar o salvamento do comprador.
- **8. Trava de senha:** vale para a conta naquele IP, e nome gigante no login é recusado sem hash.
- **9. Trocar de personagem:** se a conexão antiga fecha enquanto o personagem é lido do banco, ele é lido de novo
  (`CharacterSaver.SaveVersion`).
- **11. Limites:** `game` com 3 núcleos, 2 GB e 512 processos; `postgres` com 2 GB; `caddy` com 512 MB.

**Continua com você:** 1 (preencher os termos), 5 (UptimeRobot) e 10 (cópia dos backups fora da VPS, que precisa da
conta do serviço de armazenamento). O que estava em "Para depois do teste" continua aberto.

## Estado atual

- **Testes:** 242 no Shared e 183 no Servidor, todos passando (`dotnet test Game.slnx -c Release`).
- **Produção:** protocolo 19, igual ao cliente publicado no launcher (`0.1.1-20261021.2342`). A VM está no commit
  `0298442`, o mesmo da branch. Os 4 contêineres estão de pé, o backup diário roda e o disco está com 18% de uso.
- **Em andamento, sem commit:** janela do personagem (tecla P), em `GameWindows.Character.cs` e arquivos ligados.
- **CLAUDE.md desatualizado:** diz que falta publicar o mercado, mas ele já está na VM e no launcher.

## Antes de abrir o teste (bloqueadores)

| # | O quê | Onde | Por quê |
|---|---|---|---|
| 1 | Preencher os 6 campos `<<preencher>>` dos termos e da privacidade | `Shared/Game.Shared/Resources/Legal/` | O jogador lê esses textos no cadastro; hoje aparece `<<preencher: e-mail de contato>>` |
| 2 | Elite nascido na partida do servidor começa com 25% da vida (**conferido**) | `GameWorld.cs:468-475` e `RollElite` em `GameWorld.Combat.cs:535` | O `SetMax` só aumenta o máximo; o `Hp.Add(Max)` só existe no respawn. Todo deploy recria elites fáceis com XP ×4 e drop extra. Correção: encher a vida em `RollElite` (ou depois dele no nascimento) e ajustar o teste de `CombatTests.cs:167` para conferir a vida cheia |
| 3 | Login e cadastro sem limite de taxa por IP (**conferido**) | `AuthService.cs:73-97`, `LoginApi.cs`, `deploy/Caddyfile` | Cada pedido faz um PBKDF2 de 600 mil iterações; o cadastro calcula o hash antes de saber se o nome já existe, e o limite de 3 contas por IP só conta cadastros que deram certo. Um script simples ocupa a CPU da VPS e dá lag aos 20 jogadores. Correção: limite por IP antes do hash e no máximo N hashes ao mesmo tempo |
| 4 | Fila de envio sem limite por conexão (**conferido**) | `ClientConnection.cs:17` (`CreateUnbounded`) | Um cliente que não lê o socket e pede o mercado sem parar faz a memória crescer até o contêiner morrer. Correção: fila limitada que derruba a conexão ao encher, e limite de memória no `docker-compose.prod.yml` |
| 5 | Monitoramento externo | UptimeRobot (`Docs/deploy.md`, seção 5) | Não dá para conferir daqui se existe. Sem ele, ninguém fica sabendo se o servidor cair de madrugada |

## Recomendado (pode ir junto ou logo depois)

| # | O quê | Onde | Gravidade |
|---|---|---|---|
| 6 | Skill e finalizador não conferem se o monstro está na mesma instância do jogador; com o movimento sem limite de mapa, dá para bater em monstros de dungeon alheia | `GameWorld.Combat.cs:113` e `:207` | Média |
| 7 | Salvamento descartado após 8 falhas (**conferido**) deixa a oferta do mercado ativa no banco com o item já entregue ao comprador: item duplicado após reinício | `CharacterSaver.cs:92` | Média, só se o banco cair |
| 8 | Trava de 5 min da conta é por nome, não por IP: qualquer um trava a conta de outro (inclusive de GM); o nome no login não tem limite de tamanho | `AuthService.cs:114-119` e `:231-240` | Média |
| 9 | Corrida ao trocar de personagem: se o `Hello` novo chegar antes do fechamento da conexão antiga, o personagem é lido do banco antes do salvamento de saída | `ServerHost.cs:255-271` e `:325` | Média, pouco provável |
| 10 | Cópia dos backups fora da VPS não está ligada (serviço `offsite`) | `deploy/docker-compose.prod.yml` | Média: se a VM morrer, os backups vão junto |
| 11 | Contêineres sem limite de CPU e memória | `deploy/docker-compose.prod.yml` | Baixa sozinha, piora os itens 3 e 4 |

## Para depois do teste

- **Token de sessão em texto puro no TCP 7777, válido por 12 h** (`Messages.cs:127`). Risco em Wi-Fi público. Encurtar o
  token ou ligá-lo ao IP; TLS no jogo mais à frente.
- **Horário do combo informado pelo cliente** (`GameWorld.Combat.cs:20-26`): um bot acerta sempre a zona excelente. A
  detecção só registra `[suspeita]` no log. Vale olhar o log durante o teste.
- **Launcher aceita caminhos com `..` ou absolutos no `versao.json`** (`Tools/Game.Launcher/Release.cs:66`). Só importa se
  a VPS for invadida, mas a correção é pequena.
- **Limites por IP podem barrar testadores atrás de CGNAT:** 3 contas por IP a cada 24 h e 8 conexões por IP. Se alguém
  reclamar, reiniciar o servidor zera o limite de contas.
- **Spam de convite de grupo e de troca** ao mesmo alvo (só o limite geral de 40 mensagens/s).
- **Troca entre jogadores não pede salvamento imediato**, ao contrário do mercado. Só importa se o servidor cair.
- **Taxa de 5% arredondada para baixo:** ofertas abaixo de 20 Alz não pagam taxa.
- **Ranger:** o tiro básico tem alcance de 12 m sem custo de MP. Rodar o simulador de balanceamento para ver se o kiting
  fica forte demais.
- **Mercado:** o item vendido é o mesmo objeto na oferta e na bolsa do comprador. Não perde nem duplica, mas a escrivã
  pode mostrar o item já melhorado pelo comprador.
- **Ofertas de conta apagada** continuam no quadro se a conta for apagada com o servidor ligado. Apagar contas com o
  servidor parado, ou limpar o quadro no `--delete-account`.
- **Porta UDP:** o jogo só usa TCP 7777. Se a Security List da Oracle abriu UDP 7777, dá para fechar.

## O que está bem protegido

- Mensagens com tamanho limitado (16 KB do cliente), leitura além do fim derruba só aquela conexão, e erro numa mensagem
  ou num tick não derruba o servidor.
- Limite de mensagens por conexão, timeouts, 8 conexões por IP e chat com limite.
- Loja, ferreira, armazém, mercado, mestre de skills, dungeon e missões conferem a distância do NPC no servidor.
- Dano, alcance, recarga, MP e equipamento calculados e conferidos no servidor.
- Mercado: compra simultânea, inventário cheio, oferta vencida e queda entre a compra e o salvamento estão cobertos.
- Senhas com PBKDF2 e sal, tokens aleatórios de 256 bits, comandos de GM auditados, convidados desligados em produção.
- Banco só na rede interna do Docker, métricas só em 127.0.0.1, contêiner sem root e `.env` fora do git.

## Checklist do dia do teste

1. Fazer os itens 1 a 5 acima, publicar a versão (launcher e VM juntos se o protocolo mudar) e conferir o
   `/api/health`.
2. Fazer um backup na hora: `docker compose -f docker-compose.prod.yml exec backup sh /scripts/backup.sh`.
3. Testar a restauração num banco à parte (`Docs/deploy.md`, seção 4).
4. Confirmar a conta de GM e os comandos `/gm kick` e `/gm info`.
5. Mandar aos testadores o instalador, o link dos termos (`/api/legal/termos`) e o canal do Discord para bugs.
6. Durante o teste, acompanhar o `/api/health` (ticks em 20, erros em 0) e o log: `[erro]`, `[suspeita]` e `Falha`.
7. Depois, gerar o relatório de métricas (`Docs/metricas.md`) e juntar os bugs em `Docs/bugs.md`.
