# Liga o servidor do jogo no próprio PC (Windows), com o banco PostgreSQL do Docker, para jogar com amigos pelo Tailscale.
# Uso (na raiz do projeto):  powershell -ExecutionPolicy Bypass -File deploy\scripts\servidor-pc.ps1
# Para desligar: Ctrl+C na janela (todos são salvos antes). Veja Docs/deploy.md, "Servidor no seu PC com Tailscale".

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $root

# Banco: o mesmo contêiner do desenvolvimento (contas e personagens ficam salvos nele).
$running = docker ps --filter "name=game-postgres" --filter "status=running" --format "{{.Names}}"
if (-not $running) {
    Write-Host "Ligando o banco (Docker)..."
    docker compose up -d
}

# IP do PC na rede do Tailscale (é o que vai no servidor.txt dos amigos).
$tailscale = "C:\Program Files\Tailscale\tailscale.exe"
if (Test-Path $tailscale) {
    $ip = (& $tailscale ip -4 2>$null | Select-Object -First 1)
    if ($ip) { Write-Host "Endereço para os amigos: $($ip):7777" }
    else { Write-Host "O Tailscale não está conectado: abra o programa antes de chamar os amigos." }
}

Write-Host "Compilando e ligando o servidor..."
dotnet run --project Server/Game.Server -c Release -- --port 7777
