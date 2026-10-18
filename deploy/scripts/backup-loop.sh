#!/bin/sh
# Serviço "backup" do compose: um backup ao ligar e depois um por dia na hora BACKUP_HOUR (padrão 4h).
set -u
hour="${BACKUP_HOUR:-4}"
# Espera o servidor do jogo criar as tabelas (migrações) antes do primeiro.
sleep 60
sh /scripts/backup.sh || true
while true; do
    now=$(date +%s)
    next=$(date -d "today ${hour}:00" +%s 2>/dev/null || echo 0)
    if [ "$next" -le "$now" ]; then next=$(date -d "tomorrow ${hour}:00" +%s); fi
    echo "[backup] próximo em $(( (next - now) / 60 )) min"
    sleep $((next - now))
    sh /scripts/backup.sh || echo "[backup] falhou; tenta de novo amanhã" >&2
done
