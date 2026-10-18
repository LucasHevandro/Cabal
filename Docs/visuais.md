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

Já existe um exemplo: `Visuals/monster/coelho_saltador.prefab`, montado com formas simples pelo menu
**Jogo > Visuais > Criar coelho de exemplo**. Pode apagar quando chegar o coelho de verdade.

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
| Terreno | `Scripts/Editor/TerrainBuilder.cs` | Relevo, chão pintado por zona (grama, folhas da mata, pedra das ruínas, cascalho da arena, terra nas estradas, rocha nas encostas) e grama com flores. As texturas são geradas e ficam em `Assets/Art/Mundo`. |
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
