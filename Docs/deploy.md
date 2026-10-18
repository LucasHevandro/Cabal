# Deploy: servidor na VPS, backup, monitoramento e atualização

## Como fica

```
Jogadores ──TCP 7777──────────────────────────▶ game (jogo + API de login, .NET)
          ──HTTPS 443 ─▶ caddy ──/api/*──▶ game:7778          │
                                                              ▼
                       backup (pg_dump todo dia) ◀──── postgres (só na rede interna)
                       offsite (rclone, opcional) ──▶ outro lugar (B2, Drive, S3...)
```

Tudo em `deploy/`: `docker-compose.prod.yml`, `Caddyfile`, `.env.example`, `server.Dockerfile` e `scripts/`.

- **Portas abertas para a internet:** 7777 (jogo), 80 e 443 (Caddy).
- **Portas internas:** a 7778 (API e `/metrics`) só responde no `127.0.0.1` da própria VPS, e o banco não tem porta nenhuma.
- **Endereço que o jogador usa:** só o domínio, por exemplo `jogo.exemplo.com.br:7777`. Com domínio, o cliente usa sozinho `https://jogo.exemplo.com.br/api` para o login; com um IP, usa `http://ip:7778/api` (só para teste).

## 1. VPS

Para o teste fechado (até umas 50 pessoas), uma VPS Linux com **2 vCPU e 2 a 4 GB de RAM** sobra: 30 bots usam cerca de 1 ms por tick.

- **Região:** prefira São Paulo, para ter menos ping.
- **Sistema:** Ubuntu 24.04.

Na primeira vez, entre por SSH como root:

```bash
apt update && apt upgrade -y
adduser jogo && usermod -aG sudo jogo
curl -fsSL https://get.docker.com | sh
usermod -aG docker jogo
ufw allow OpenSSH && ufw allow 80/tcp && ufw allow 443/tcp && ufw allow 7777/tcp && ufw enable
```

- **SSH:** use chave em vez de senha e desligue o login por senha (`PasswordAuthentication no` em `/etc/ssh/sshd_config`).
- **Docker e firewall:** o Docker abre as portas publicadas por fora do ufw. Por isso o compose só publica a 7777, a 80 e a 443, e a 7778 fica presa ao 127.0.0.1.

## 2. Domínio

No painel do domínio, crie um registro **A** apontando para o IP da VPS, por exemplo `jogo.exemplo.com.br`. O Caddy pega o certificado HTTPS sozinho na primeira vez que sobe, e para isso o DNS já precisa estar apontando.

## 3. Subir o servidor

Entre como o usuário `jogo`:

```bash
git clone https://github.com/LucasHevandro/Cabal.git
cd Cabal/deploy
cp .env.example .env
nano .env
```

No `.env`, preencha `GAME_DOMAIN` e `GAME_DB_PASSWORD`. Para gerar a senha, use `openssl rand -hex 24`. Depois suba:

```bash
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d
curl -s https://jogo.exemplo.com.br/api/health
```

- **Saúde:** a resposta deve trazer `"status":"ok"`. As migrações do banco rodam sozinhas quando o servidor liga.
- **Imagem privada:** o pacote no GHCR nasce privado. Antes do `pull`, rode `docker login ghcr.io` com um token do GitHub que tenha `read:packages`. Outra saída é deixar o pacote público em GitHub → Packages → cabal-server → Settings.
- **Sem imagem publicada:** dá para construir na própria VPS com `docker compose -f docker-compose.prod.yml build game`.

Comandos do dia a dia (dentro de `deploy/`, com `C="docker compose -f docker-compose.prod.yml"`):

| Para | Comando |
|---|---|
| Ver o log do jogo | `$C logs -f --tail 200 game` |
| Ver se está tudo de pé | `$C ps` |
| Reiniciar o jogo (salva todos antes) | `$C restart game` |
| Métricas | `curl -s http://127.0.0.1:7778/metrics` |
| Tornar uma conta GM (vale no próximo login) | `$C run --rm --no-deps game --set-gm nomedaconta` |
| Tirar o GM | `$C run --rm --no-deps game --unset-gm nomedaconta` |
| Relatório de métricas (últimos 7 dias) | `$C run --rm --no-deps game --report 7` |
| Apagar uma conta a pedido do jogador (LGPD) | `$C run --rm --no-deps game --delete-account nomedaconta` |

## 4. Backup

O serviço `backup` faz um `pg_dump` compactado:
- **Quando:** todo dia às `BACKUP_HOUR` (padrão 4h, horário de Brasília) e também logo depois de ligar.
- **Onde:** em `deploy/backups/`.
- **Quantos:** guarda os `BACKUP_KEEP` mais novos (padrão 14).
- **Conferência:** cada arquivo é verificado com `pg_restore --list` antes de ser aceito.

Para fazer um backup na hora, por exemplo antes de atualizar:

```bash
docker compose -f docker-compose.prod.yml exec backup sh /scripts/backup.sh
```

**Cópia fora da VPS (recomendado).** Se a VPS morrer, os backups que estão nela somem junto. Para copiar para outro lugar com o rclone (Backblaze B2, Google Drive, S3 etc.):

```bash
mkdir -p rclone
docker run --rm -it -v "$PWD/rclone:/config/rclone" rclone/rclone:1.68 config
```

Siga as perguntas e crie um remoto chamado, por exemplo, `b2`. Depois, no `.env`, defina `RCLONE_TARGET=b2:meu-bucket/jogo` e suba o serviço:

```bash
docker compose -f docker-compose.prod.yml --profile offsite up -d
```

Ele copia a pasta de backups a cada 6 horas.

**Restaurar.** O script desliga o jogo (salvando quem está online), faz um backup do estado atual por garantia, troca o banco e liga de novo. Ele pede para digitar RESTAURAR antes de começar:

```bash
sh scripts/restore.sh backups/game-2026-10-20_040000.dump
```

**Teste a restauração de vez em quando** num banco à parte, sem mexer no de verdade:

```bash
C="docker compose -f docker-compose.prod.yml"
$C exec -T postgres psql -U game -d game -c "DROP DATABASE IF EXISTS restore_test" -c "CREATE DATABASE restore_test"
$C exec -T postgres pg_restore -U game -d restore_test --no-owner < backups/ARQUIVO.dump
$C exec -T postgres psql -U game -d restore_test -c "select count(*) from accounts"
```

## 5. Monitoramento

- **UptimeRobot** (grátis), monitor do tipo *Keyword*:
  - URL `https://jogo.exemplo.com.br/api/health`, palavra-chave `"status":"ok"`, a cada 5 minutos, com alerta no e-mail ou no Discord.
  - A resposta é 503 se o servidor está ligando ou se o loop do jogo travou por mais de 5 s.
  - Crie também um monitor do tipo *Port* na 7777.
- **O que o `/api/health` mostra:**
  - jogadores, monstros e conexões;
  - ticks por segundo (o normal é 20) e tempo médio e máximo do tick;
  - erros pegos no loop desde que ligou (o normal é 0; cada um vira uma linha `[erro]` no log);
  - memória, protocolo e `build` (a versão da imagem).
- **Prometheus/Grafana (opcional):** o `/metrics` está no formato do Prometheus, em `http://127.0.0.1:7778/metrics`.
- **Logs:** o Docker guarda até 5 arquivos de 20 MB por serviço.
- **O que procurar no log:** `[erro]` (defeito no servidor), `[suspeita]` (detecção de bot), `[gm]` (ações de GM) e `Falha` (gravação no banco).

## 6. Atualizar em menos de 1 hora

1. **Corrija e teste no PC:** `dotnet test Game.slnx` e o teste online do cliente.
2. **Mande para a `main`** (merge ou push). O GitHub Actions roda os testes e publica a imagem `ghcr.io/lucashevandro/cabal-server:latest`, com o commit como tag. Isso leva uns 10 minutos.
3. **Avise no Discord** que o servidor vai reiniciar em 5 minutos.
4. **Na VPS:**
   ```bash
   cd ~/Cabal/deploy
   docker compose -f docker-compose.prod.yml exec backup sh /scripts/backup.sh   # se a atualização mexe no banco
   docker compose -f docker-compose.prod.yml pull game
   docker compose -f docker-compose.prod.yml up -d game
   curl -s https://jogo.exemplo.com.br/api/health
   ```
   Os jogadores ficam fora por uns 10 segundos. Todos são salvos ao desligar, porque o `docker stop` dá 30 s para isso.
5. **Confira** que o `build` no `/api/health` é o commit novo.

**Deu errado? Volte à versão anterior.** No `.env`, ponha `GAME_IMAGE=ghcr.io/lucashevandro/cabal-server:<commit-anterior>` e rode `up -d game`. Se a versão nova mudou o banco (migração), restaure o backup feito no passo 4.

**Mudou o protocolo** (`Protocol.Version`)? Aí o cliente também precisa ser atualizado, porque o servidor recusa cliente de versão diferente. Crie uma tag `v0.x.y`: o Actions gera o cliente e, se o itch.io estiver configurado, envia. O jogador só precisa baixar de novo.

## 7. GitHub Actions

O arquivo é `.github/workflows/ci.yml`.

- **Testes:** em todo push na `main` e em todo pull request, com um PostgreSQL de verdade.
- **Imagem do servidor:** na `main` e nas tags `v*`, publicada no GHCR. Não precisa configurar nada, porque ela usa o `GITHUB_TOKEN`.
- **Branch padrão:** hoje a branch padrão do repositório no GitHub é `feat/fase3-rede`. Troque para `main` em Settings → General → Default branch.
- **Cliente Windows (opcional):**
  - Usa o [game-ci](https://game.ci/docs/github/activation). O build pelo Actions ainda não foi testado; ele roda no Linux com o backend Mono, que é o do projeto.
  - Variável do repositório (Settings → Secrets and variables → Actions → Variables): `BUILD_CLIENT` = `true`.
  - Variável `GAME_SERVER` com o endereço padrão (por exemplo `jogo.exemplo.com.br:7777`). Ela vira o `servidor.txt` do build.
  - Segredos `UNITY_LICENSE`, `UNITY_EMAIL` e `UNITY_PASSWORD`. Para a licença Personal, siga o passo a passo de ativação do game-ci.
  - O build sai como artefato do Actions e fica disponível por 14 dias.
- **itch.io (opcional, só em tags `v*`):** segredo `BUTLER_API_KEY` (gerado no itch.io → Settings → API keys) e variável `ITCH_TARGET` (por exemplo `usuario/nome-do-jogo`).

## 8. Endereço do servidor no cliente

O menu de entrada já vem com o servidor do `servidor.txt`, que fica ao lado do `Prototipo.exe`:

```
# Servidor padrão do jogo
jogo = jogo.exemplo.com.br:7777
# api = https://outro-lugar/api   (opcional: só se a API não estiver no mesmo domínio)
```

- **Na linha de comando:** `-server host:porta` e `-api URL` passam na frente do arquivo.
- **No Editor do Unity:** o arquivo é `Client/servidor.txt`, que não vai para o Git.
- **No build:** o build copia esse arquivo, ou usa a variável `GAME_SERVER`.

## 9. Testar a pilha de produção no próprio PC

Com o Docker Desktop aberto:

```bash
docker build -f deploy/server.Dockerfile -t game-server:local .
cd deploy
printf 'GAME_DOMAIN=localhost\nGAME_DB_PASSWORD=teste_local\nGAME_IMAGE=game-server:local\n' > .env
docker compose -f docker-compose.prod.yml up -d
curl -sk https://localhost/api/health
```

- O banco de desenvolvimento (`game-postgres`) pode continuar ligado; só não deixe um servidor do jogo de desenvolvimento aberto na 7777.
- Os bots e o cliente entram com `127.0.0.1:7777`. A API em `127.0.0.1:7778` está publicada só no loopback.
- Para desligar e apagar o banco de teste: `docker compose -f docker-compose.prod.yml down -v`.

## Limites conhecidos

- **Criação de contas sem limite por IP.** A API ainda não limita quantas contas o mesmo IP cria; o login já trava a conta após 5 senhas erradas. Para o teste fechado com convite, isso basta. Antes de abrir para o público, vale um limite por IP, usando o `X-Forwarded-For` que o Caddy envia.
- **Termos e privacidade:** preencha os campos `<<preencher>>` dos textos antes de abrir o teste (veja `Docs/metricas.md`).
- **Uma VPS só:** se ela cair, o jogo cai. O monitoramento avisa, e o backup fora da VPS garante que nada se perde.
