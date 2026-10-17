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

Fase 5 — Itens e progressão (em andamento): duas classes com 8 skills e rota de combo própria, modo Despertar,
equipamento com melhoria até +10 e proteção contra azar, pedras de opção, atributos, armazém, três NPCs e oito
missões do nível 1 ao 30 — tudo decidido pelo servidor. O modo offline segue como campo de treino de combate.
Fases 1 a 4 (combate, mundo, rede e contas com PostgreSQL) concluídas. Veja o Backlog do MVP no GDD.
