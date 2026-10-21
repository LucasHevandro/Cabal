# Gera Client/Builds/InstalarTerrasDeAstralis.exe a partir do build em Client/Builds/Windows (gere o build antes).
# Uso:  powershell -File Tools/instalador/gerar-instalador.ps1 [-Version 0.1.0]
# Precisa do Inno Setup 6 (winget install --id JRSoftware.InnoSetup -e).
# O ícone do instalador (icone.ico) sai do brasão do jogo, Client/Assets/Art/Icone/TerrasDeAstralis.png.
param(
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$build = Join-Path $root 'Client\Builds\Windows\TerrasDeAstralis.exe'
if (-not (Test-Path $build)) { throw "Sem build em $build. Gere antes: menu Protótipo > Gerar build Windows." }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 não encontrado (winget install --id JRSoftware.InnoSetup -e).' }

# Versão: a do Player Settings do Unity (bundleVersion), se não vier no parâmetro.
if (-not $Version) {
    $settings = Get-Content (Join-Path $root 'Client\ProjectSettings\ProjectSettings.asset') -Raw
    $Version = if ($settings -cmatch '(?m)^\s*bundleVersion:\s*(\S+)') { $Matches[1] } else { '0.1.0' }
}

# Ícone .ico com vários tamanhos (PNG dentro do ICO, aceito do Windows Vista em diante).
$png = Join-Path $root 'Client\Assets\Art\Icone\TerrasDeAstralis.png'
$ico = Join-Path $PSScriptRoot 'icone.ico'
if (-not (Test-Path $ico) -or (Get-Item $png).LastWriteTime -gt (Get-Item $ico).LastWriteTime) {
    Add-Type -AssemblyName System.Drawing
    $source = [System.Drawing.Image]::FromFile($png)
    $images = foreach ($size in 256, 64, 48, 32, 16) {
        $bitmap = New-Object System.Drawing.Bitmap $size, $size
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($source, 0, 0, $size, $size)
        $graphics.Dispose()
        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    }
    $source.Dispose()
    $out = New-Object System.IO.BinaryWriter([System.IO.File]::Create($ico))
    $out.Write([uint16]0); $out.Write([uint16]1); $out.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
        $out.Write([byte]$dimension); $out.Write([byte]$dimension); $out.Write([byte]0); $out.Write([byte]0)
        $out.Write([uint16]1); $out.Write([uint16]32); $out.Write([uint32]$image.Bytes.Length); $out.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }
    foreach ($image in $images) { $out.Write($image.Bytes) }
    $out.Close()
    Write-Host "Ícone: $ico"
}

& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'TerrasDeAstralis.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup falhou ($LASTEXITCODE)." }
Write-Host "Instalador $Version`: $(Join-Path $root 'Client\Builds\InstalarTerrasDeAstralis.exe')"
