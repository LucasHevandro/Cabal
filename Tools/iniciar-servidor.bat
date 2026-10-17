@echo off
rem Liga o banco (Docker) e o servidor do jogo na porta 7777. Feche esta janela (ou Ctrl+C) para desligar e salvar.
title Servidor do jogo
cd /d "%~dp0.."
echo Ligando o banco de dados (o Docker Desktop precisa estar aberto)...
docker compose up -d
if errorlevel 1 (
    echo.
    echo Nao foi possivel ligar o banco. Abra o Docker Desktop e tente de novo.
    pause
    exit /b 1
)
echo Ligando o servidor...
dotnet run --project Server/Game.Server -- --port 7777 --guests
pause
