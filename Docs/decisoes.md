# Registro de decisões técnicas

Cada decisão importante fica registrada aqui com data e motivo, para não ser rediscutida sem necessidade.

## 2026-10-15 — Motor e stack

- **Cliente:** Unity 6 (C#).
- **Servidor:** .NET, autoritativo. O cliente envia intenções; o servidor decide dano, drop e validação de combo.
- **Regras compartilhadas:** biblioteca `Shared` em .NET Standard 2.1, usada pelo cliente (previsão) e pelo servidor (decisão).
- **Banco:** PostgreSQL.
- **Motivo:** C# nas duas pontas permite compartilhar regras; Unity tem o maior ecossistema para combate de ação 3D com equipe pequena.

## 2026-10-15 — Ordem de desenvolvimento

- O sistema de combo é construído e validado **offline primeiro** (Fase 1), antes de qualquer código de rede.
- **Motivo:** o combo é o pilar do jogo; se não for divertido, o resto não importa.
