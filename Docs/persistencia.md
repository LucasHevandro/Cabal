# Contas e persistência (Fase 4)

## Como funciona

1. O cliente fala com a **API de login** do servidor (porta do jogo + 1): cadastro, login, lista e criação de
   personagens. A senha nunca é guardada: só um hash PBKDF2-SHA256 com 600.000 iterações e sal aleatório.
2. O login devolve um **token** (256 bits, vale 12 h, só na memória do servidor). O cliente conecta no jogo com o
   token e o personagem escolhido.
3. O servidor carrega o personagem do **PostgreSQL** fora do loop do jogo e o coloca no mundo como estava.
4. O servidor **salva** a cada 10 s, quando o jogador sai e ao desligar (Ctrl+C), por uma fila em segundo plano
   que grava um por vez, em ordem, e tenta de novo se o banco falhar. Estado, inventário e registros de itens entram
   numa única transação.
5. Todo ganho de Alz, coleta de item e subida de nível vira uma linha em `item_log` (suporte e auditoria).

Proteções: mesma resposta para conta inexistente e senha errada; 5 senhas erradas travam a conta por 5 min; o mesmo
personagem não entra duas vezes; personagem de outra conta é recusado; um item sai do chão no mesmo passo em que
entra no inventário (nunca existe em dois lugares).

## Comandos

```bash
docker compose up -d                                  # sobe o PostgreSQL (127.0.0.1:5432)
dotnet run --project Server/Game.Server -- --port 7777 # aplica migrações e liga jogo (7777) + API (7778)
powershell -File Tools/backup-db.ps1                  # backup em backups/ (mantém os 14 últimos)
dotnet tool run dotnet-ef migrations add Nome --project Server/Game.Server --output-dir Persistence/Migrations
```

Testes contra o PostgreSQL real (banco separado, apagado a cada teste):

```bash
GAME_DB_TEST="Host=localhost;Port=5432;Database=game_tests;Username=game;Password=game_dev_local" dotnet test Server/Game.Server.Tests
```

## 2026-10-16 — Meta da Fase 4

> Derrubar o servidor no meio do jogo não perde mais que alguns segundos de progresso e nunca duplica itens.

| Passo | Resultado |
| --- | --- |
| Cliente entra (login automático), mata um coelho e pega um item | XP 33, Alz 3, 1 item; 2 registros em `item_log` |
| Último salvamento automático | 20:01:34 |
| Servidor derrubado à força (sem salvar), com o cliente online | 20:01:41 |
| Banco depois da queda | XP 33, Alz 3, 1 item, HP 1974; cada registro uma vez só |
| Servidor religado, mesma conta entra de novo | voltou com XP 33, Alz 3, 1 item, HP 1974/2000 |
| Backup e restauração num banco separado | contas, personagens, itens, registros e XP idênticos |

**Resultado: meta atingida.** A perda máxima numa queda é o que aconteceu desde o último salvamento (até 10 s).
Itens no chão somem com a queda (não estavam com ninguém); itens no inventário estão no banco.

## Pendências para produção (Fase 8)

- HTTPS na API de login (proxy reverso ou certificado no Kestrel) e TLS na conexão do jogo.
- Agendar o backup diário e guardar as cópias fora do servidor.
- Tokens na memória: reiniciar o servidor obriga a logar de novo (aceitável no MVP).
