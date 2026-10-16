# Backup do banco do jogo (PostgreSQL do docker-compose).
# Uso:  powershell -File Tools/backup-db.ps1 [-Keep 14]
# Gera backups/game-AAAA-MM-DD_HHmmss.dump (formato do pg_dump, compactado) e apaga os mais antigos.
# Restaurar:  docker exec -i game-postgres pg_restore -U game -d game --clean < backups/arquivo.dump
# Agendar todo dia: Agendador de Tarefas do Windows chamando este script (ou um cron no servidor Linux).
param(
    [int]$Keep = 14
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $root 'backups'
New-Item -ItemType Directory -Force $folder | Out-Null

$name = 'game-' + (Get-Date -Format 'yyyy-MM-dd_HHmmss') + '.dump'
$target = Join-Path $folder $name

# pg_dump roda dentro do contêiner; a saída vem para cá pelo stdout (binário).
$process = Start-Process -FilePath 'docker' -ArgumentList 'exec', 'game-postgres', 'pg_dump', '-U', 'game', '-d', 'game', '-Fc' `
    -RedirectStandardOutput $target -RedirectStandardError (Join-Path $folder 'ultimo-erro.txt') -NoNewWindow -Wait -PassThru
if ($process.ExitCode -ne 0) {
    Remove-Item $target -ErrorAction SilentlyContinue
    throw "pg_dump falhou (código $($process.ExitCode)). Veja backups/ultimo-erro.txt. O Docker e o contêiner game-postgres estão rodando?"
}

$size = (Get-Item $target).Length
if ($size -lt 100) { throw "Backup suspeito: só $size bytes." }
Write-Output "Backup criado: $name ($([math]::Round($size / 1KB, 1)) KB)"

Get-ChildItem $folder -Filter 'game-*.dump' | Sort-Object Name -Descending | Select-Object -Skip $Keep |
    ForEach-Object { Remove-Item $_.FullName; Write-Output "Removido backup antigo: $($_.Name)" }
