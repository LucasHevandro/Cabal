# Terras de Astralis: contexto para o Claude

Action MMORPG com combos de timing. Cliente Unity, servidor autoritativo .NET e regras compartilhadas em C#. O README
tem a estrutura e como rodar, e `Docs/` tem as decisões, o balanceamento, os visuais, o áudio e o deploy.

## Regras de trabalho (pedidas pelo usuário)

- **Português do Brasil** em tudo: respostas, resumos, legendas, comentários e mensagens de commit.
- **Commit por camada**, com prefixo: `Shared:`, `Servidor:`, `Cliente:`, `Tools:` e `Docs:`. Um commit por camada
  tocada, com a linha `Co-Authored-By` no fim.
- **Push e merge só com autorização.** O usuário costuma responder "Pode enviar a branch ao GitHub e mesclar na main."
  O fluxo é `git push origin feat/mundo-visual` e `git push origin feat/mundo-visual:main` (fast-forward), depois
  `git branch -f main feat/mundo-visual`.
- **Depois de rodar o exe com `-screen-*`**, apague `Screenmanager*` e `UnitySelectMonitor*` em
  `HKCU:\Software\Astralis\Terras de Astralis`. Senão o jogo dele abre em janela no segundo monitor.
- **Senhas e tokens nunca são digitados pelo Claude.** Não peça e não repita credenciais; o usuário digita.
- **Remote Control:** o usuário costuma acompanhar pelo celular. Quando o box do wrap_up não aparece, a mensagem
  final começa pelo resumo e traz a pergunta e as opções.

## Repositório

- **GitHub:** `LucasHevandro/TerrasDeAstralis` (era `Cabal`). Trabalhe na branch `feat/mundo-visual`; a `main`
  anda junto, por fast-forward. A branch padrão antiga do GitHub ainda pode ser `feat/fase3-rede`.
- **Pastas:** `Shared/Game.Shared` (netstandard2.1, também usada pelo Unity como pacote local), `Server/Game.Server`,
  `Client/` (Unity), `Tools/` (Bots, Balance, Launcher, instalador, publicar) e `deploy/` (VPS).
- **Testes:** `dotnet test Game.slnx -c Release` (hoje são 226 no Shared e 169 no Servidor). Os testes de textos
  exigem chaves literais no código: nada de `"login.head_" + i`, use um array de chaves literais.
- **Protocolo:** `Shared/Game.Shared/Net/Messages.cs`, `Protocol.Version = 17`. Se mudar alguma mensagem, suba a
  versão e atualize o cliente e o servidor juntos. O `FuzzTests` conta os tipos de mensagem.
- **Migrações:** `cd Server/Game.Server && dotnet ef migrations add Nome -o Persistence/Migrations`.
- **Textos:** `Shared/Game.Shared/Resources/Locales/pt-BR.txt`, lidos por `Loc.T("chave", args)`.

## Unity (Windows)

- **Editor:** `"/c/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Unity.exe" -batchmode -quit -projectPath Client
  -executeMethod <Metodo> -logFile <log>`. Leia o log com `grep -a`.
- **Uma instância por vez:** se o comando voltar antes da hora, espere o `Unity.exe` terminar. Uma segunda
  instância aborta.
- **Métodos de build:**
  - `Game.Client.Editor.PrototypeBuild.BuildWindows`: gera `Client/Builds/Windows/TerrasDeAstralis.exe` e aplica o
    ícone.
  - `Game.Client.Editor.KayKitBuilder.Build`: refaz todos os prefabs. Depois, volte com `git checkout` o que não
    era para mudar (npc, monstro, aldeão e controllers) e commite só o que importa.
  - `Game.Client.Editor.GameIcon.Generate`: refaz o brasão em `Assets/Art/Icone`.
  - `Game.Client.Editor.MusicExport.ExportAll -musicOut pasta`: exporta as músicas e o som ambiente em WAV.
- **Testes do exe** (DevTools): `-onlinetest pasta` com `-onlinefight`, `-onlineui`, `-onlinesanctuary`,
  `-onlinearena` e `-onlinesocial`; e também `-worldtest`, `-geartest`, `-logintest`, `-skilltest`, `-posetest` e
  `-bridgetest`.
  - Cada um grava `relatorio.txt` (SUCESSO ou FALHA) e screenshots.
  - Para os online: servidor local com `dotnet artifacts/bin/Game.Server/release/Game.Server.dll --db memory --guests --port 7790`
    e o exe com `-connect 127.0.0.1:7790 -name Teste`.
  - Rode um jogo por vez: duas instâncias ao mesmo tempo atrapalham o teste.
- **Modelos:** KayKit Adventurers (Knight, Mage, Ranger, Rogue e Barbarian) usam o mesmo `Rig_Medium`. As cabeças
  trocam entre eles (penteados), e na textura a pele fica na célula (0,0) e o cabelo na (1,0).
  - Há um `Ranger.fbx`, que serve para a classe Ranger.

## Armadilhas já vistas

- **Edições com Python:** heredoc no bash com aspas complicadas quebra. Grave o script com a ferramenta Write.
  Em strings Python com caminhos do Windows, use raw strings (`r"..."`): `\a`, `\b`, `\r` e `\v` viram caracteres
  de controle. Isso já estragou o `.iss` uma vez.
- **Matar processos:** `Stop-Process` filtrando por texto da linha de comando pode matar o próprio shell, se o
  texto estiver no comando. Filtre pelo nome do processo.
- **Exe de janela no PowerShell:** com `&`, o PowerShell não espera o exe terminar. Use `| Out-Null` para esperar.

## Produção (VPS Oracle, ARM, Ubuntu 24.04)

- **Acesso:** `ssh astralis` (configurado no `~/.ssh/config` do PC, chave `~/.ssh/astralis.key`). O IP é
  137.131.249.110 e o domínio é `teradestralis.duckdns.org`.
- **Pasta na VM:** `~/TerrasDeAstralis` (branch `main`). A stack fica em `deploy/docker-compose.prod.yml`, com o
  projeto `game`: `postgres`, `game` (imagem `astralis-server:local`, construída na própria VM), `caddy` (HTTPS,
  `/api/*` e `/download/*`) e `backup` (pg_dump diário em `deploy/backups`).
- **Portas:** 80, 443 e 7777 abertas na Security List da Oracle e no iptables, antes do `REJECT`.
- **Atualizar o servidor:**
  ```bash
  cd ~/TerrasDeAstralis && git pull && cd deploy && docker compose -f docker-compose.prod.yml build game && docker compose -f docker-compose.prod.yml up -d game
  ```
- **Conferir:** `curl -s https://teradestralis.duckdns.org/api/health` (o `protocol` tem que bater com o cliente).

## Cliente para os jogadores (launcher)

- **Launcher:** `Tools/Game.Launcher` (.NET Framework 4.8 e WinForms, `TerrasDeAstralisLauncher.exe`). Ele baixa o
  `https://teradestralis.duckdns.org/download/versao.json` e só os arquivos que mudaram (`arquivos/<sha256>.gz`),
  troca a si mesmo e abre o jogo.
  - Testes: `--pasta`, `--url`, `--verificar` e `--foto`.
  - Ele não está no `Game.slnx`, porque o CI roda no Linux.
- **Publicar uma versão** (depois do build do Windows):
  ```powershell
  powershell -ExecutionPolicy Bypass -File Tools\launcher\publicar.ps1 -Novidades "texto"
  ```
  O script envia só os arquivos novos por `scp` para `~/TerrasDeAstralis/deploy/download` e refaz o instalador.
  A versão é o `bundleVersion` (hoje 0.1.1) mais a data.
- **Instalador:** `Tools/instalador` (Inno Setup 6, já instalado no PC) gera `Client/Builds/InstalarTerrasDeAstralis.exe`.
  Os atalhos abrem o launcher.
- **Passo a passo completo:** `Docs/deploy.md` (seções "Atualizar" e "Launcher").

## Onde paramos (2026-10-21)

O usuário pediu, nesta ordem: **4, 6, 7, 8, 3 e 5**.
- **Feito:**
  - 4: missões diárias, chefe de mundo com horário e a segunda dungeon (Santuário das Ruínas).
  - 6: música por região e som ambiente.
  - 7: penteados, tom de pele e esconder o elmo.
  - Extras: servidor na Oracle, ícone, ponte, instalador, launcher e troca do nome do repositório.
- **Próximo, 8: monstros elite.** Ainda não começou no código. Plano combinado:
  - **Shared:** `EliteRules`, com cerca de 5% de chance ao nascer ou renascer, só para monstros do mundo aberto (nem
    chefe, nem dungeon, nem chefe com horário). Vida ×4, dano ×1,5, XP ×4, Alz ×3 e uma rolagem de drop a mais.
  - **Servidor:**
    - `ServerMonster.IsElite`, sorteado com o `_random` em `CreateMonster` (`GameWorld.cs`) e `RespawnMonster`
      (`GameWorld.Combat.cs`), e recompensas multiplicadas no `KillMonster`.
    - Hoje `ServerMonster.Data` vem do `Spawn` (compartilhado pelo spawn). Para os números por monstro, use
      multiplicadores ou um `MonsterData` clonado.
  - **Protocolo:** um campo `Elite` no `EntitySpawned` do monstro. Isso pede o protocolo 18 (o 17 já está publicado
    na VM e no launcher).
  - **Cliente:** modelo uns 20% maior com tom dourado (no estilo do `VisualCatalog.ApplyVariant`), nome dourado
    "{0} (Elite)" (chave `monster.elite_name`) e talvez uma aura.
  - **Fechamento:** testes no servidor, `Docs/balanceamento.md`, commits por camada, publicar a versão no launcher e
    atualizar o servidor na VM.
- **Depois:**
  - 3: classe **Ranger** (arqueira, com `Ranger.fbx`). Entra em Shared, Servidor e Cliente: classe, skills, arco e
    flechas, e criação de personagem.
  - 5: **barraca ou mercado de jogadores na vila.**
