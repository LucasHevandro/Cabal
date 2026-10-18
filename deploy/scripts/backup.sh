#!/bin/sh
# Um backup do banco agora: /backups/game-AAAA-MM-DD_HHMMSS.dump (formato do pg_dump, compactado).
# Usa PGHOST, PGUSER, PGPASSWORD e PGDATABASE; guarda os BACKUP_KEEP mais novos (padrão 14).
set -eu
keep="${BACKUP_KEEP:-14}"
name="game-$(date +%Y-%m-%d_%H%M%S).dump"
target="/backups/$name"

if ! pg_dump -Fc -f "$target.tmp"; then
    rm -f "$target.tmp"
    echo "[backup] ERRO: pg_dump falhou" >&2
    exit 1
fi
size=$(wc -c < "$target.tmp")
if [ "$size" -lt 100 ]; then
    rm -f "$target.tmp"
    echo "[backup] ERRO: backup suspeito, só $size bytes" >&2
    exit 1
fi
# Confere que o arquivo abre (lista o conteúdo) antes de dar como bom.
pg_restore --list "$target.tmp" > /dev/null
mv "$target.tmp" "$target"
echo "[backup] criado $name ($((size / 1024)) KB)"

# Apaga os mais antigos (os nomes têm a data, então a ordem alfabética é a ordem do tempo).
ls -1 /backups/game-*.dump 2>/dev/null | sort -r | tail -n +"$((keep + 1))" | while read -r old; do
    rm -f "$old"
    echo "[backup] removido $(basename "$old")"
done
