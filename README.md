# Projeto Action MMORPG (nome provisório)

Action MMORPG com combate por combos de timing, inspirado na jogabilidade de jogos como CABAL Online, com mundo, arte e números próprios.

- Game Design Document e Backlog do MVP: [GDD — Action MMORPG](https://claude.ai/code/artifact/12dab18b-3ed5-4653-a90f-4d0624814832)

## Estrutura

| Pasta | Conteúdo |
| --- | --- |
| `Client/` | Projeto Unity 6 (cliente do jogo) |
| `Server/` | Servidor de jogo autoritativo em .NET |
| `Shared/` | Biblioteca C# de regras usada pelo cliente e pelo servidor (fórmulas, combo, tabelas) |
| `Tools/` | Ferramentas de apoio (ex.: exportar planilhas de design para JSON) |
| `Docs/` | Anotações técnicas e decisões |

A biblioteca `Shared` mira **.NET Standard 2.1** para poder ser usada dentro do Unity. O servidor e os testes usam o .NET mais recente.

## Pré-requisitos

- [Git](https://git-scm.com/) e [Git LFS](https://git-lfs.com/) (`git lfs install` uma vez por máquina)
- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Unity Hub](https://unity.com/download) com Unity 6 LTS
- Docker Desktop (para o PostgreSQL, nas fases de persistência)

## Como rodar

**Testes das regras** (na raiz do repositório):

```bash
dotnet test Game.slnx
```

**Protótipo de combate** (Unity 6000.6.6f1):

1. Abra a pasta `Client/` no Unity Hub.
2. Abra a cena `Assets/Scenes/Treino.unity` e aperte Play.
3. Se a cena não existir ou quiser recriá-la: menu **Protótipo > Criar cena de treino**.

Controles: WASD andar, Shift correr, Espaço esquivar, botão direito gira a câmera, Tab ou clique seleciona o alvo,
1–4 skills, Q inicia o combo (gasta SP), F finalizador. Os parâmetros do combo ficam no componente
`PlayerCombat` do objeto *Jogador* (seção *Combo Tuning*), para ajustar durante os testes.

**Demo automática** (verifica o protótipo sem teclado): gere o executável pelo menu
**Protótipo > Gerar build Windows** e rode

```bash
Client/Builds/Windows/Prototipo.exe -autodemo saida-demo
```

A pasta `saida-demo` recebe screenshots e um `relatorio.txt` terminando em SUCESSO ou FALHA.

## Status

Fase 1 — Combate offline: protótipo do combo jogável na cena de treino. Veja o Backlog do MVP no GDD.
