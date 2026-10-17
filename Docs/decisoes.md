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

## 2026-10-17 — Itens com id único, armazém por personagem e convidados liberados

- **Cada item é uma linha própria** (`item_instances`) com id único dado pelo servidor (`ItemUidAllocator`, que
  continua a partir do maior id do banco). Isso permite rastrear um item nos registros e impede duplicação. Um
  único servidor dá os ids; com vários servidores no futuro, trocar por faixas por servidor ou sequência do banco.
- **Armazém por personagem** no MVP (40 posições, só perto do NPC). Compartilhar entre personagens da conta fica
  para depois.
- **Convidados** (`--guests`, usados por bots e testes) têm todas as skills e o combo liberados sem precisar das
  missões, para os testes de carga continuarem medindo o combate. Personagens de conta seguem as regras normais
  (combo liberado pela missão "A arte do combo").
- **Coletar** em missão é conferido na bolsa na hora de entregar (os itens são consumidos), em vez de contar o que
  foi pego: assim guardar ou jogar fora no caminho não dessincroniza nada.

## 2026-10-17 — Grupo e chat

- **XP de grupo:** o XP do monstro cresce 15% por membro a mais e é dividido entre os membros vivos a até 50 m;
  cada um recebe o ajuste de nível próprio. Em 4, cada um ganha 36% do XP de matar sozinho, mas o grupo mata bem
  mais rápido, então subir em grupo rende mais por hora. O Alz é dividido igual (o resto fica com quem matou).
- **Quem leva o monstro:** o dano é somado por grupo; o grupo (ou jogador sozinho) com mais dano leva. Dentro do
  grupo, quem bateu mais é o dono do drop, mas qualquer membro pode pegar durante o tempo de dono.
- **Só o líder convida, expulsa e passa a liderança.** Se o líder sai, o membro mais antigo assume; grupo de um
  só é desfeito. Sair do jogo tira do grupo (não há grupo salvo no banco).
- **Chat:** canal de mapa vai para todos no mundo (um mapa só no MVP); o servidor corta em 120 caracteres, limita a
  5 mensagens seguidas (uma nova a cada 1,5 s) e troca palavrões por asteriscos. O filtro compara palavras inteiras
  (sem acento, com números no lugar de letras e letras repetidas), para não estragar palavras como "cubo".
- **Revisar se:** o raio de 50 m deixar alguém ganhar XP parado na vila enquanto o grupo caça perto dela
  (aparece nos testes); se acontecer, exigir que o membro tenha causado dano ou esteja em combate.

## 2026-10-17 — Amigos, bloqueio e denúncia

- **Amigos de um lado só:** adicionar não pede aceite (quem foi adicionado só recebe um aviso). Para adicionar ou
  bloquear, a pessoa precisa estar online (o servidor pega o id do personagem); remover e desbloquear funcionam
  pelo nome, com ela offline. Até 50 amigos e 100 bloqueados. Convidados não têm listas.
- **Bloquear** tira dos amigos, esconde as mensagens de mapa, grupo e sussurro daquela pessoa e recusa os
  convites dela. Quem foi bloqueado só descobre ao tentar sussurrar ("não está recebendo suas mensagens").
- **Denúncia** guarda o motivo e as 10 últimas mensagens do denunciado com o **texto original** (sem o filtro de
  palavrões), para a moderação ver o que foi dito. Limite: a mesma pessoa uma vez a cada 10 minutos, e no máximo
  5 denúncias por hora. Por enquanto a moderação lê a tabela `player_reports` direto no banco.
- Amigos, bloqueados e denúncias vão para o banco junto com o salvamento do personagem (a cada 10 s e ao sair).
