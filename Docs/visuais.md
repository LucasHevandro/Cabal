# Como colocar modelos no jogo

O jogo procura um modelo para cada coisa pelo **id**. Se achar, usa o modelo; se não achar, continua com a forma
simples de hoje (cápsula, cubo, esfera). Assim dá para trocar um modelo por vez, sem mexer em código e sem o jogo
ficar quebrado no meio do caminho.

## Onde colocar

Crie um **prefab** com o nome exato do id dentro de `Client/Assets/Resources/Visuals/<categoria>/`:

| Categoria | Pasta | Ids (exemplos) |
|---|---|---|
| Monstros e chefes | `Visuals/monster/` | `coelho_saltador`, `lobo_cinzento`, `guardiao_das_ruinas`, `senhor_da_cripta` |
| NPCs | `Visuals/npc/` | `anciao_doran`, `ferreira_ilsa`, `guarda_volumes_bento`, `mercadora_lia`, `guardia_selene` |
| Personagem do jogador (por classe) | `Visuals/class/` | `blade` (Lâmina), `arcanist` (Arcanista) |
| Itens no chão | `Visuals/item/` | `espada_ferro`, `pocao_hp_p`, `nucleo_baixo` |

Os ids de todos os monstros, NPCs e itens estão no arquivo de textos `Shared/Game.Shared/Resources/Locales/pt-BR.txt`
(linhas `monster.<id>`, `npc.<id>`, `item.<id>`).

Hoje todas as classes, NPCs e monstros já têm modelo (veja as seções Personagens e Monstros abaixo).

## Regras do prefab

- **Pivô no chão**, no centro dos pés. O jogo põe o prefab na posição da entidade.
- **Frente para +Z** (o eixo azul do Unity). O jogo gira o objeto para onde ele anda ou olha.
- **Escala em metros:** um humano tem cerca de 1,8 m. A altura do modelo define onde aparecem o nome, a barra de vida e
  os números de dano; a largura define o anel de seleção.
- **Sem colisores** (se tiver, o jogo remove): quem colide e recebe o clique é o objeto do jogo, não o modelo.
- **Materiais URP** (Universal Render Pipeline/Lit ou Simple Lit). Para desempenho com muitos jogadores na tela, prefira
  1 material por modelo e uma textura de paleta compartilhada.
- Orçamento sugerido: personagem 5 a 10 mil triângulos, monstro comum menos que isso.

## Animação (opcional)

Se o prefab tiver um **Animator**, o jogo preenche estes parâmetros, se existirem no Animator Controller:

| Parâmetro | Tipo | Quando |
|---|---|---|
| `Speed` | float | Velocidade no chão, em m/s (0 parado; ~4 andando; ~7 correndo) |
| `Attack` | trigger | O monstro começa o golpe (aviso no chão) ou o jogador usa uma skill |
| `Hit` | trigger | Levou dano |
| `Dodge` | trigger | O jogador esquivou |
| `Dead` | bool | Reservado para pose de queda (hoje o corpo some ao morrer) |

Parâmetro que não existe é ignorado: um modelo só com `Speed` (parado → andar → correr) já funciona. O "Apply Root
Motion" fica desligado: quem move o personagem é o jogo (o servidor, no online), não a animação.

Com animações do **Mixamo**: importe o personagem como Humanoid, crie um Animator Controller com um Blend Tree de
`Speed` (parado, andar, correr) e estados de ataque, dano e esquiva ligados aos triggers. Mantendo o mesmo esqueleto
Humanoid, as mesmas animações servem para as duas classes.

## Conferir

Rode o jogo (ou o teste automático com `-onlinetest pasta -onlinefight`) e veja nas capturas se o modelo apareceu no
tamanho certo, com o nome em cima da cabeça e o anel de seleção nos pés.

# Mundo: terreno, céu, névoa, luz e pós-processamento

A cena `Treino.unity` é gerada por código. Depois de mudar o mapa, gere de novo:
**Protótipo > Criar cena de treino**, ou em linha de comando com `-executeMethod Game.Client.Editor.TrainingSceneBuilder.Build`.

| Parte | Onde | O que faz |
|---|---|---|
| Altura do chão | `Scripts/World/Ground.cs` | Fórmula única do relevo. O construtor do terreno usa a fórmula, e o jogo a usa para pôr no chão o que o servidor manda só em X e Z (jogadores, monstros, NPCs, itens e avisos de golpe). |
| Terreno | `Scripts/Editor/TerrainBuilder.cs` | Relevo, chão pintado por zona (grama, folhas da mata, pedra das ruínas, cascalho da arena, terra nas estradas, rocha nas encostas) e tufos de grama e flores low-poly (malhas paradas, só até 45 m da câmera). As texturas são geradas e ficam em `Assets/Art/Mundo`. |
| Cenário | `Scripts/Editor/PropBuilder.cs` + `MeshKit.cs` | Árvores, pinheiros, arbustos, pedras, colunas, muros, casas, cercas, poço e braseiros em low-poly, gerados por código. As malhas ficam em `Assets/Art/Mundo/Malhas`. |
| Clima | `Scripts/World/Atmosphere.cs` | Névoa, luz ambiente, cor do sol e filtro de cor por zona, misturados aos poucos. A dungeon e a arena PvP têm o próprio clima. |
| Pós-processamento | `Assets/Materials/Prototipo/PosProcessamento.asset` | Bloom suave, tonemapping neutro, contraste, vinheta e sombras levemente azuladas. Na qualidade **Baixa** ele é desligado. |
| Céu | `Assets/Materials/Prototipo/Ceu.mat` | Céu procedural do Unity, com o sol da cena. |

**Regras do relevo**
- **Planos:** a praça da vila, as estradas, os grupos de monstros (o raio deles + 3 m), os NPCs e a arena do chefe. Assim os avisos de golpe no chão ficam retos.
- **Morros suaves:** ficam entre esses lugares. São mais altos na mata e nas ruínas.
- **Montanhas:** sobem a partir da borda da área jogável do servidor (x de −130 a 130, z de −70 a 230) e fecham o horizonte. O que fica nelas é só enfeite, sem colisão.
- **Servidor:** não muda nada. Ele continua só com X e Z.

**Trocar pelo KayKit (ou outro pacote).** O `PropBuilder` escolhe uma lista de malhas por tipo (`arvore`, `pinheiro`, `pedra`, `coluna`, `muro`, `casa`...). Para usar os modelos do pacote, troque a variante gerada pelo modelo importado no tipo correspondente. Os mesmos lugares, colisões e escalas continuam valendo. Os modelos dos pacotes também têm a frente para +Z e o pivô no chão.

# Personagens (KayKit)

As classes e os NPCs usam os pacotes grátis **KayKit** de Kay Lousberg (www.kaylousberg.com), em licença CC0: uso livre, inclusive comercial, sem crédito obrigatório. As licenças estão em `Assets/Art/KayKit/LICENSE_*.txt`.

| Pacote (versão grátis) | O que usamos |
|---|---|
| Character Pack: Adventurers 2.0 | Knight, Mage, Barbarian, Rogue, Rogue_Hooded e Ranger, além de armas e acessórios |
| Character Animations 1.1 | Rig_Medium: General, MovementBasic, MovementAdvanced, CombatMelee, CombatRanged e Special |
| Character Pack: Skeletons 1.1 | Esqueletos (Warrior, Mage, Minion e Rogue) e as armas deles, para a etapa dos monstros |

Para gerar tudo: **Jogo > Visuais > Montar personagens KayKit**, ou `-executeMethod Game.Client.Editor.KayKitBuilder.Build`. O construtor:
- configura a importação (rig Generic, laço nas animações de parado, andar e correr);
- cria os materiais URP com as texturas de cada personagem;
- monta o Animator `Assets/Art/KayKit/Gerado/Personagem.controller`;
- grava os prefabs que o jogo procura.

| Prefab | Modelo | Na mão |
|---|---|---|
| `Visuals/class/blade` (Lâmina) | Knight | espada e escudo |
| `Visuals/class/arcanist` (Arcanista) | Mage | cajado |
| `Visuals/npc/anciao_doran` | Rogue_Hooded | livro aberto |
| `Visuals/npc/ferreira_ilsa` | Barbarian | machado |
| `Visuals/npc/guarda_volumes_bento` | Ranger | caneca |
| `Visuals/npc/mercadora_lia` | Rogue | adaga |
| `Visuals/npc/guardia_selene` | Knight sem viseira | espada de duas mãos |

**Animator**
- **Locomoção:** blend por `Speed` (parado em 0, andar em 2,2 m/s, correr em 6 m/s).
- **Golpes:** um estado para cada `AttackKind`, definido em `Fx/AttackStyle.cs`: 0 corte, 1 golpe de cima, 2 estocada, 3 giro, 4 golpe com salto, 5 corte diagonal, 6 disparo mágico, 7 magia em área, 8 magia erguida e 9 finalizador. Cada skill escolhe o seu estilo nesse arquivo.
- **Outros estados:** dano (`Hit`), esquiva (`Dodge`) e morte (`Dead`).
- **Morte no jogo:** o personagem cai. Monstros animados somem 2 s depois; jogadores ficam caídos até renascer.
- **Golpes dos outros:** o golpe de outro jogador também anima o modelo dele, pela skill que veio no evento de combate.

**Conferir as animações no jogo:** `Prototipo.exe -posetest pasta`. Ele põe as classes e os NPCs lado a lado e captura cada estado. Faço a conferência no jogo porque, no Editor em modo batch, a malha não é deformada pelos ossos.

# Monstros

| Monstro | Modelo | Como anima |
|---|---|---|
| Coelho Saltador, Lobo Cinzento, Javali Furioso, Aranha da Mata, Golem de Pedra | low-poly em partes, gerado por `Scripts/Editor/CreatureBuilder.cs` (menu **Jogo > Visuais > Montar bichos**) | por código (`Fx/ProceduralRig.cs`): patas pela velocidade, pulo do coelho, bote no ataque, recuo ao apanhar e tombo na morte |
| Esqueleto Guerreiro, Esqueleto Arqueiro, Cavaleiro Ossudo, Servo da Cripta, Senhor da Cripta (chefe) | KayKit Skeletons, com as armas do pacote | Animator `Esqueleto.controller` (parado, andar e morte próprios de esqueleto e os golpes do personagem) |
| Cavaleiro Caído, Espectro (translúcido), Guardião das Ruínas (chefe) | KayKit Skeletons; o Espectro com material roxo transparente e o Guardião com olhos vermelhos | o mesmo Animator |

- **Golpe de cada monstro:** sai de `AttackStyle.ForMonster` (arqueiro dispara, espectro e Senhor da Cripta lançam magia, os chefes guerreiros dão o golpe de duas mãos).
- **Tamanhos:** os modelos são escalados para a altura definida em `KayKitBuilder.MonsterLooks`. A colisão e o alcance continuam sendo os do servidor.
- **Conferir no jogo:** `Prototipo.exe -posetest pasta -posemonsters`.

# Skills: golpe, rastro e impacto

Cada uma das 16 skills tem o próprio golpe em `Fx/SkillMoves.cs`. Ele define:
- a sequência de animações (por exemplo, a Estocada Dupla são duas estocadas e a Tempestade de Aço é um giro seguido de um giro largo);
- quando a arma acerta ou quando a magia sai da ponta do cajado;
- quanto o corpo avança e a janela do rastro na arma;
- se a magia junta energia antes de sair.

O Animator ganha um estado para cada passo (no `KayKitBuilder`), no ritmo do parâmetro `AttackSpeed`. O Excellent sai 15% mais rápido.

**No jogo**
- **Na hora do aperto:** aparecem a nota e a rota do combo e começa a animação. A resposta é imediata, como o ritmo do combo pede.
- **No momento do acerto (`Impacts`):**
  - **Golpes físicos:** caem as faíscas, o número de dano, o tremor e a pausa de impacto.
  - **Magias:** o projétil sai da ponta do cajado. Antes, a energia junta na ponta (`Charge`), e o dano aparece quando ele chega.
- **Online:** o servidor continua decidindo no aperto, e o resultado dele espera o golpe chegar para aparecer.
- **Arma (`Fx/WeaponFx.cs`, nos prefabs das classes):**
  - rastro da cor da skill (dourado e mais largo no Excellent);
  - brasas na Lâmina Flamejante;
  - avanço do modelo nas investidas. A posição de verdade continua sendo a do jogo, e o andar não conta esse avanço.
- **Assinatura no impacto (`SkillFx.Signature`):**
  - runas no Golpe Rúnico, rachaduras na Fenda Sísmica, labareda na Lâmina Flamejante e X vermelho na Execução;
  - cristais e geada na Corrente Gélida, implosão e duas ondas na Explosão Arcana;
  - explosão com fumaça no Meteoro, e chuva de pedras com três ondas de choque no Cataclismo.

**Conferir:** `Prototipo.exe -skilltest pasta` toca as 16 skills na frente de um esqueleto. Ele captura o preparo e o acerto de cada uma, alternando Good e Excellent.

**Clarões:** usam um brilho grande de partícula, que o bloom espalha. Luzes pontuais deixavam um ponto branco solto na tela.

# Equipamento visível e brilho da melhoria

- **Arma na mão:** cada arma tem o próprio modelo em `Visuals/weapon/<id>`, gerado pelo `ItemModelBuilder` (menu **Jogo > Visuais > Montar itens**).
  - **Ferro:** o modelo do KayKit.
  - **Aço negro:** maior e escurecido.
  - **Astralita:** a espada colorida, ou o cajado em azul-cristal, com um brilho leve.
- **Brilho da melhoria** (`WeaponFx.GlowFor`): um "casco" da mesma malha, aditivo, pulsando sobre a arma.

  | Melhoria | Brilho |
  |---|---|
  | +4 a +6 | azul fraco |
  | +7 e +8 | ciano, com partículas subindo da lâmina |
  | +9 | roxo |
  | +10 | dourado, pulsando mais forte |

- **Outros jogadores:** o servidor manda a arma e a melhoria de cada jogador no `EntitySpawned` e, quando mudam, no `EntityLook` (protocolo v13). Assim todo mundo vê o brilho de todo mundo.
- **Itens no chão:** os 37 itens têm modelo em `Visuals/item/<id>`: as armas, armadura, luvas, botas e anel por nível, cristais dos núcleos, gemas, poções, materiais e a chave da cripta. Giram e flutuam com o feixe da raridade.
- **Conferir:** `Prototipo.exe -geartest pasta`. Mostra as duas classes com cada arma em +0, +4, +7, +9 e +10, e depois todos os itens.
