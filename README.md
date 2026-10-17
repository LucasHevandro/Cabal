# Projeto Action MMORPG (nome provisório)

Action MMORPG com combate por combos de timing, inspirado na jogabilidade de jogos como CABAL Online, com mundo, arte e números próprios.

- Game Design Document e Backlog do MVP: [GDD — Action MMORPG](https://claude.ai/code/artifact/12dab18b-3ed5-4653-a90f-4d0624814832)

## Estrutura

| Pasta | Conteúdo |
| --- | --- |
| `Client/` | Projeto Unity 6 (cliente do jogo) |
| `Server/` | Servidor de jogo autoritativo em .NET |
| `Shared/` | Biblioteca C# de regras usada pelo cliente e pelo servidor (fórmulas, combo, tabelas) |
| `Tools/` | Ferramentas de apoio (ex.: exportar planilhas de design para JSON) |
| `Docs/` | Anotações técnicas e decisões |

A biblioteca `Shared` mira **.NET Standard 2.1** para poder ser usada dentro do Unity. O servidor e os testes usam o .NET mais recente.

## Pré-requisitos

- [Git](https://git-scm.com/) e [Git LFS](https://git-lfs.com/) (`git lfs install` uma vez por máquina)
- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Unity Hub](https://unity.com/download) com Unity 6 LTS
- Docker Desktop (para o PostgreSQL, nas fases de persistência)

## Como rodar

**Testes das regras** (na raiz do repositório):

```bash
dotnet test Game.slnx
```

**Banco de dados** (PostgreSQL no Docker; abra o Docker Desktop antes):

```bash
docker compose up -d
```

**Servidor** (jogo na porta 7777, API de login na 7778; Ctrl+C desliga e salva todos os personagens):

```bash
dotnet run --project Server/Game.Server -- --port 7777
```

Sem Docker, `--db memory` usa um banco em memória (perde tudo ao desligar). `--guests` permite entrar sem conta
(bots e testes automáticos; convidados não são salvos).

O servidor decide monstros, combate (incluindo o timing do combo, com compensação de latência), XP, drops e o chefe,
e salva os personagens a cada 10 s e ao sair.

**Jogar online:** ligue o servidor, abra `Client/Builds/Windows/Prototipo.exe` e no menu inicial escolha
**Conectar** (endereço `127.0.0.1:7777` para o mesmo PC; em outro PC da rede, o IP de quem roda o servidor).
Para pular o menu:

```bash
Client/Builds/Windows/Prototipo.exe -connect 127.0.0.1:7777 -name Ana
```

**Teste automático online** (servidor ligado na porta 7777): conecta, luta contra coelhos pelo servidor e grava
`saida-online/relatorio.txt` (SUCESSO ou FALHA) com screenshots.

```bash
Client/Builds/Windows/Prototipo.exe -connect 127.0.0.1:7777 -name Teste -onlinetest saida-online -onlinefight
```

Com conta (servidor sem `--guests`), o mesmo teste também passeia pela interface: inventário, conversa com o
ancião, missão "Primeiros passos" e a janela da ferreira.

```bash
Client/Builds/Windows/Prototipo.exe -server 127.0.0.1:7777 -autologin teste senha12345 -onlinetest saida-online -onlinefight -onlineui
```

**Controles online** (além dos do combate): 1–8 skills da classe (as bloqueadas mostram o nível ou a missão que
falta), I inventário (arraste para mover, vestir, guardar ou jogar fora; botão direito veste/usa), C atributos,
J diário de missões, T fala com o NPC por perto (ancião = missões, ferreira = melhoria e opções, armazém),
R ativa o Despertar quando a barra enche. O combo de um personagem novo libera na missão "A arte do combo".

**Chat e grupo:** Enter abre o chat (Enter envia, Esc cancela, Tab troca entre mapa e grupo). Comandos: `/m` mapa,
`/g` grupo, `/s nome` sussurro, `/r` responde o último sussurro, `/convidar nome`, `/sair`, `/expulsar nome`,
`/lider nome`, `/amigo nome`, `/remover nome`, `/bloquear nome`, `/desbloquear nome`, `/denunciar nome motivo`,
`/trocar nome`, `/ajuda`. O abre a janela de amigos (quem está online, sussurrar, convidar, remover e desbloquear). Bloquear esconde
as mensagens e recusa os convites daquela pessoa; a denúncia guarda o motivo e as últimas mensagens dela para a
moderação (tabela `player_reports`). A Mercadora Lia (T perto dela) vende poções, núcleo baixo e o conjunto de
Ferro, e compra itens (botão direito ou arraste da bolsa). Na troca, cada lado monta a oferta (botão direito na
bolsa e botões de Alz), aperta Pronto e depois Confirmar. **Cripta Esquecida:** com uma Chave da Cripta por membro
(a mercadora vende), o líder fala com a Guardiã Selene e entra com o grupo: três salas com portões que só abrem
com a sala limpa, o Senhor da Cripta (escudo com servos e golpe no chão avisado), 15 minutos, medalha S/A/B pelo
tempo e pelo combo máximo, baú com sorteios por medalha e ranking dos melhores tempos no portal. **PvP:**
`/duelo nome` desafia alguém por perto (3 s de contagem, ninguém morre: perde quem chega a 1 de vida, desiste ou
foge); `/arena` entra na fila da arena 5 contra 5 (times equilibrados por nível, 3 pontos de captura, 10 minutos,
vence quem chegar a 500 pontos). Entre jogadores o dano vale 35% e o Despertar metade. Grupo de até 4: quem está a até 50 m do monstro divide o XP (com bônus de 15% por membro a
mais) e o Alz, conta a missão e pode pegar o drop. O servidor filtra palavrões e limita mensagens seguidas.

**Teste do social com um cliente:** um bot com conta convida o personagem do cliente; o teste aceita, conversa,
adiciona o bot como amigo, bloqueia, desbloqueia, denuncia, compra e vende na mercadora, troca e duela com o bot,
conferindo cada passo. Use um personagem novo (o teste vende 4 das 5 poções iniciais).

```bash
dotnet run --project Tools/Game.Bots -- --port 7777 --count 3 --seconds 110 --accounts --invite NomeDoPersonagem
Client/Builds/Windows/Prototipo.exe -server 127.0.0.1:7777 -autologin conta senha12345 -onlinetest saida-online -onlinesocial
```

**Teste da dungeon:** servidor com `--guests --dungeon-test` (Cripta fácil e sem nível mínimo). O cliente entra
sozinho, limpa as salas, derruba o chefe e volta ao portal; os bots fazem o mesmo em grupos de 4, cada grupo na
sua cópia.

```bash
dotnet run --project Server/Game.Server -- --port 7777 --db memory --guests --dungeon-test
Client/Builds/Windows/Prototipo.exe -connect 127.0.0.1:7777 -name Teste -onlinetest saida-online -onlinedungeon
dotnet run --project Tools/Game.Bots -- --port 7777 --count 8 --seconds 300 --dungeon
```

**Teste da arena:** com `--arena-size 1` no servidor, o cliente entra na fila com um bot passivo, derruba o bot,
toma um ponto e sai. Para a carga, 20 bots fazem duas partidas 5v5 ao mesmo tempo.

```bash
dotnet run --project Server/Game.Server -- --port 7777 --db memory --guests --arena-size 1
dotnet run --project Tools/Game.Bots -- --port 7777 --count 1 --seconds 240 --arena-passive
Client/Builds/Windows/Prototipo.exe -connect 127.0.0.1:7777 -name Teste -onlinetest saida-online -onlinearena
```

`--party` nos bots forma grupos de 4 entre eles e conversa no chat (teste de carga do social). `--accounts` faz
cada bot entrar com conta (`bot01`, `bot02`...) em vez de convidado.

**Atalho:** `Tools/iniciar-servidor.bat` liga o banco no Docker e o servidor na porta 7777.

**Protótipo de combate** (Unity 6000.6.6f1):

1. Abra a pasta `Client/` no Unity Hub.
2. Abra a cena `Assets/Scenes/Treino.unity` e aperte Play.
3. Se a cena não existir ou quiser recriá-la: menu **Protótipo > Criar cena de treino**.

Controles: WASD andar, Shift correr, Espaço esquivar, botão direito gira a câmera, Tab ou clique seleciona o alvo,
1–4 skills, Q inicia o combo (gasta SP), F finalizador, E coleta itens, I abre o inventário. Os parâmetros do combo ficam no componente
`PlayerCombat` do objeto *Jogador* (seção *Combo Tuning*), para ajustar durante os testes.

**Demo automática** (verifica o protótipo sem teclado): gere o executável pelo menu
**Protótipo > Gerar build Windows** e rode

```bash
Client/Builds/Windows/Prototipo.exe -autodemo saida-demo
```

A pasta `saida-demo` recebe screenshots e um `relatorio.txt` terminando em SUCESSO ou FALHA.

## Status

Fase 6 — Conteúdo e social (em andamento): grupo de até 4 com XP e drop divididos, chat (mapa, grupo, sussurro,
sistema) com filtro e limite de mensagens, lista de amigos, bloquear e denunciar, loja da mercadora e troca com
dupla confirmação (economia revisada em `Docs/balanceamento.md`) e a dungeon Cripta Esquecida em instância por
grupo, com medalha, baú e ranking, duelo e arena 5v5 de captura de pontos.
Fases 1 a 5 (combate, mundo, rede, contas com PostgreSQL, itens e progressão 1→30) concluídas. Veja o Backlog do
MVP no GDD.
