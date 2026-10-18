#!/bin/sh
# Restaura um backup no banco da produção. Rode na pasta deploy/ da VPS:
#   sh scripts/restore.sh backups/game-2026-10-20_040000.dump
# O servidor do jogo é desligado antes (salvando quem está online) e religado no fim.
# Antes de restaurar, faz um backup do estado atual (por garantia).
set -eu
file="${1:?uso: sh scripts/restore.sh backups/arquivo.dump}"
[ -f "$file" ] || { echo "Arquivo não encontrado: $file" >&2; exit 1; }
compose="docker compose -f docker-compose.prod.yml"

echo "Isto APAGA o banco atual e coloca no lugar o backup $(basename "$file")."
printf "Digite RESTAURAR para continuar: "
read -r answer
[ "$answer" = "RESTAURAR" ] || { echo "Cancelado."; exit 1; }

echo "Desligando o servidor do jogo..."
$compose stop game
echo "Backup do estado atual..."
$compose exec -T backup sh /scripts/backup.sh
echo "Restaurando..."
$compose exec -T postgres pg_restore -U game -d game --clean --if-exists --no-owner --single-transaction < "$file"
echo "Ligando o servidor do jogo..."
$compose start game
echo "Pronto. Confira com: curl -s https://\$GAME_DOMAIN/api/health"
