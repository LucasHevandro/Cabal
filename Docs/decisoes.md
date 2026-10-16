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

## 2026-10-16 — Rede: TCP próprio em vez de FishNet/Mirror

- **Transporte:** TCP com protocolo binário próprio (mensagens com prefixo de tamanho), definido no `Shared`.
- **Servidor:** aplicação .NET de console (`Server/`), sem Unity. Usa as mesmas regras do `Shared`
  (combo, skills, dano, IA dos monstros, drops).
- **Autoridade:** o servidor decide combate, dano, monstros, XP e drops. O movimento do jogador é enviado pelo
  cliente e validado no servidor (velocidade máxima); correção por teleporte se passar do limite.
- **Motivo:** FishNet e Mirror rodam só dentro do Unity, o que obrigaria o servidor a ser um Unity headless e
  impediria usar o `Shared` em .NET puro. TCP próprio funciona igual nos dois lados, sem dependências, e é o
  modelo usado por MMOs do gênero. Para 20 a 50 jogadores por mapa a latência do TCP é aceitável.
- **Revisar se:** o combate em rede parecer travado com 150 ms de latência simulada (Fase 3), ou o número de
  jogadores por mapa crescer muito — nesse caso, avaliar UDP (ex.: LiteNetLib) mantendo o mesmo protocolo.

## 2026-10-17 — Sistemas novos só no modo online

- A partir da Fase 5, equipamento, melhoria, missões, atributos e classes existem só no modo online (servidor
  decide).
- O modo offline continua como **campo de treino de combate** (combo, rotas, monstros, efeitos), sem os sistemas
  novos.
- **Motivo:** manter duas versões completas de cada sistema (cliente offline e servidor) dobraria o trabalho e os
  testes; o MVP é um jogo online.
