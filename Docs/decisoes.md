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

## 2026-10-17 — Loja e troca entre jogadores

- **Mercadora Lia** (perto da praça) vende poções, núcleo baixo e o conjunto de Ferro, e compra qualquer item da
  bolsa. Nada da loja revende com lucro (o preço de venda é sempre menor que o de compra). O preço de venda de
  equipamento sobe com o grau e com a melhoria.
- **Troca com dupla confirmação:** cada lado monta a oferta (até 8 itens e Alz) e aperta Pronto; com os dois
  prontos, cada um aperta Confirmar. Qualquer mudança de oferta destrava os dois, para ninguém confirmar uma coisa
  e receber outra. A troca é tudo ou nada: o servidor simula em cópias das bolsas e só mexe se tudo couber.
- Durante a troca a bolsa fica parada (só dá para usar poção), porque a oferta aponta para posições da bolsa. Os
  jogadores precisam estar a até 6 m para pedir e confirmar; a troca cancela se passarem de 12 m, se alguém morrer
  ou sair.
- **Convidados só trocam com convidados**, para itens de teste não chegarem a personagens de verdade.
- Cada item e o Alz que muda de mão ficam no `item_log` dos dois lados (`troca_saida`, `troca_entrada`,
  `troca_alz`, com o número da troca).
- **Risco conhecido:** os dois personagens são salvos em transações separadas. Se o servidor cair entre os dois
  salvamentos, um lado pode perder o que recebeu (nunca duplica). Revisar se aparecer em produção: salvar os dois
  juntos logo depois da troca.

## 2026-10-17 — Dungeon em instância

- **Cada cópia da dungeon é uma faixa própria do mesmo mundo**, longe do mapa aberto (a partir de Z = 1000, uma a
  cada 400 m), e não um mundo separado. Visão, combate, grupo, troca e chat continuam funcionando por distância, sem
  código novo para "qual instância". O servidor cria os monstros da cópia ao entrar e apaga tudo (monstros e itens no
  chão) quando o último jogador sai, quando o tempo acaba ou 90 s depois da vitória.
- **Entrada:** o líder fala com a Guardiã Selene, todo o grupo perto do portal, nível 18 ou mais e uma Chave da
  Cripta por membro (300 Alz na mercadora). Escolhemos o item barato em vez do limite diário, que exigiria guardar
  contadores por dia.
- **Portões:** o servidor não deixa passar do portão de uma sala que ainda tem monstros (corrige a posição). O
  cliente também tem paredes e portões com colisão, mas quem decide é o servidor.
- **Chefe:** escudo aos 66% e 33% da vida (fica imune até os 3 servos morrerem) e um golpe no chão a cada 9 s, avisado
  2 s antes, onde o alvo está. Os monstros da dungeon não renascem.
- **Medalha:** S até 8 minutos e combo máximo do grupo de 12 ou mais; A até 11 minutos; B até 15. O baú dá 3, 2 ou 1
  sorteios por jogador, e cada um só pega os próprios itens.
- **Ranking:** os 10 melhores tempos ficam em memória e na tabela `dungeon_runs`. Só grupos de personagens de conta
  vão para o banco (convidados de teste não sujam o ranking).
- **Morreu na dungeon:** renasce na entrada da cripta. Sair do jogo dentro dela salva a posição no portal.
- **Tempo:** pela estimativa (`Tools/Game.Balance`), um grupo de 4 no nível 22 termina em cerca de 10 minutos, um
  no nível mínimo em cerca de 12, e um grupo bom (+7, mais combo) tira S. Dupla no nível 22 não termina (cerca de 17
  minutos): a dungeon é feita para grupo.
- `--dungeon-test` deixa a Cripta fácil e sem nível mínimo, para bots e testes automáticos.

## 2026-10-17 — PvP: duelo e arena 5v5

- **Dano separado do PvE:** entre jogadores o golpe vale 35% e o bônus do Despertar vale pela metade. Os jogadores
  têm bem menos vida que os monstros, e com o dano cheio uma luta acabaria em 2 ou 3 golpes. O combo, as rotas e a
  esquiva funcionam igual (o timing continua sendo a habilidade que decide).
- **Só bate em quem é hostil:** o adversário do duelo depois da contagem, ou o time inimigo na arena depois do
  início. Fora disso, jogadores não podem ser alvo (não há PvP livre no mapa no MVP).
- **Duelo:** convite e aceite a até 10 m, 3 s de contagem, até 3 minutos. Ninguém morre: o golpe que zeraria a vida
  deixa 1 e encerra o duelo. Desistir ou se afastar mais de 30 m do ponto de início é perder.
- **Arena:** fila única, sem grupo. Com 10 na fila forma uma partida, dividindo os times por nível em "cobra"
  (A B B A...). Cada partida é uma faixa própria do mapa (ao sul, uma a cada 400 m), como as dungeons.
  - **Pontos:** 3 pontos de captura. Tomar um ponto neutro leva 6 s com só um time dentro. Com os dois times dentro
    o ponto fica disputado e para. Tomar o ponto do outro time leva o dobro (neutraliza e depois toma).
  - **Placar:** cada ponto dominado dá 1 por segundo e cada abate dá 3. Vence quem chegar a 500 ou tiver mais pontos
    no fim dos 10 minutos.
  - **Durante a partida:** renasce na base em 6 s. Quem sai antes não ganha nada. Um time que fica vazio perde.
  - **Prêmio:** 1.500 Alz para quem vence e 500 para quem perde (registro `arena`).
- `--arena-size N` muda o tamanho do time para testes (1 contra 1).
- **Revisar:** com o dano a 35%, uma luta 1 contra 1 de mesmo nível dura uns 20 a 30 golpes. Ajustar com gente de
  verdade, e decidir se a arena terá ranking e moeda própria.

## 2026-10-17 — Textos em arquivo de tradução

- **Um arquivo por idioma:** `Shared/Game.Shared/Resources/Locales/pt-BR.txt`, com linhas `chave = texto` e
  argumentos `{0}`, `{1}` (com formato, como `{0:N0}`). O formato é simples de propósito: dá para editar em qualquer
  editor e sem ferramenta. Para traduzir, copia-se o arquivo como `en-US.txt` e troca-se só o texto. O que faltar na
  tradução aparece em português.
- **Nomes dos catálogos saíram do código:** itens, monstros, skills, NPCs, missões, zonas e salas têm só o id. O nome
  vem da chave `item.<id>`, `monster.<id>` etc. A cena guarda as chaves das zonas e dos bonecos de treino.
- **O servidor manda chave, não frase:** avisos, linhas do chat do sistema, resultado da ferreira, recusa de conexão e
  erros da API de contas vão como chave mais argumentos (`LocalText`). Cada cliente monta o texto no idioma dele.
  Nomes de itens, monstros e NPCs vão como referência a outra chave (`@item.pocao_hp_p`). Por isso o protocolo foi
  para a v10. Logs e o registro de denúncias continuam em português (são para nós, não para o jogador).
- **Teste que segura a regra:** um teste do Shared procura no código (Shared, servidor, cliente e ferramentas) toda
  chave escrita e falha se ela não existir no arquivo. Outro confere que todo item, monstro, skill, NPC e missão tem
  nome. Assim um texto novo sem tradução não passa.
- Ficaram em português: os comandos do chat (`/convidar`, `/duelo`...), o menu do editor e as ferramentas de teste.

## 2026-10-17 — Sensação de cada skill (game feel)

- Antes todas as skills físicas soltavam as mesmas faíscas e todas as mágicas a mesma esfera. Agora cada uma das 16
  tem cor, jeito de sair e peso próprios (`SkillFx`, só no cliente: dano, alcance e área continuam no Shared e no
  servidor).
- **Lâmina:** corte no ar na frente do personagem em toda skill física; Giro de Lâmina abre um anel em volta dele;
  Golpe Rúnico é azul; Estocada Dupla e Tempestade de Aço batem duas vezes; Lâmina Flamejante é fogo; Fenda Sísmica
  e Execução são pesadas (mais hit stop, mais tremida, som grave).
- **Arcanista:** Faísca e Lança de Força saem como lança (menores e mais rápidas); Corrente Gélida é gelo; Raio Duplo
  são dois projéteis; Meteoro e Cataclismo caem do céu e são os golpes mais pesados do jogo.
- **Áreas:** toda skill de área desenha no chão um anel do tamanho real da área, uma vez por uso (mesmo acertando
  vários monstros), para o jogador aprender o alcance.
- **Peso:** o peso da skill soma no hit stop (até +0,07 s), multiplica a tremida da câmera e escolhe o som de impacto.
  Excellent e crítico continuam somando por cima.
- **Revisar com gente:** se o hit stop das skills pesadas não atrapalha o timing do combo em sequência rápida.


## 2026-10-19 — Skills aprendidas no mestre e barra de atalhos

- **Mestre de skills** (Mestre Aldric, na vila): as skills não entram mais sozinhas ao subir de nível. O nível e a
  missão de rank só deixam aprender, e a skill entra quando o jogador paga ao mestre. Preço provisório:
  40 × nível² Alz (Nv 3 = 360, Nv 12 = 5.760, Nv 25 = 25.000). As skills de nível 1 já vêm com o personagem.
- **Ninguém perde skill:** personagens salvos antes do mestre (sem a lista no banco) ficam com tudo o que o nível e as
  missões já liberavam.
- **Barra de atalhos com 8 casas**, arrumada pelo jogador como no CABAL:
  - da janela de skills (K) ou do mestre para a barra;
  - entre as casas, trocando de lugar;
  - para fora da barra, tirando a skill.
  Clicar na casa usa a skill. O servidor confere a barra (só skills aprendidas, sem repetir) e salva com o
  personagem nas colunas `LearnedSkills` e `Hotbar` (migração `SkillsAprendidas`). O `UseSkill` continua mandando o
  índice da skill na classe; a barra só diz qual tecla usa qual skill.
- Convidados continuam sabendo todas as skills, para os bots e os testes.
