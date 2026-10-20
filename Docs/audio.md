# Áudio

Tudo o que o jogo toca hoje é gerado em código (músicas em `MusicSynth`, sons em `ProceduralSounds`), até chegarem
arquivos de verdade. Para trocar, basta pôr o arquivo na pasta com o nome certo: o jogo usa o arquivo no lugar do som
gerado, sem mudar código.

## Músicas

| Arquivo (em `Client/Assets/Resources/Audio/music/`) | Quando toca |
|---|---|
| `city` | Vila de Partida e menu de entrada |
| `field` | Campos do Vento e estradas (dórico, aventura) |
| `forest` | Mata Sombria (menor, devagar, arpejo baixinho) |
| `ruins` | Ruínas Esquecidas e Arena do Guardião (frígio maior, tambor ritual e sinos) |
| `dungeon` | Cripta Esquecida |
| `sanctuary` | Santuário das Ruínas (lídio, pad aberto e sinos) |
| `boss` | Chefe de campo a menos de 28 m, sala do chefe das dungeons e arena |

A troca é um cruzamento suave de 2 s. O volume vem das opções (Música × Volume geral). As músicas devem emendar o
fim no começo (tocam em loop). Para ouvir as geradas fora do jogo: menu **Jogo > Áudio > Exportar músicas e ambiente
gerados** (salva em `Client/Builds/Musicas`, cada um repetido duas vezes para ouvir a emenda).

## Som ambiente

Camadas em loop de 16 s (`AmbientSynth`), misturadas pelo `AmbientPlayer` conforme o lugar, a hora e o tempo. O
volume de cada camada muda devagar (meio segundo do zero ao máximo) e segue o mesmo peso por região do clima visual
(cheio no meio da região, some na borda). O volume vem das opções (Ambiente × Volume geral).

| Arquivo (em `Client/Assets/Resources/Audio/ambient/`) | Como é | Onde |
|---|---|---|
| `wind` | Ruído grave que sobe e desce | Campos do Vento (mais nas rajadas), Ruínas, um pouco na vila e na mata; santuário e arena PvP |
| `birds` | Frases curtas de assobios | De dia: vila, campos e mata |
| `crickets` | Dois grilos em ritmos diferentes | À noite: vila, campos e, mais forte, a mata |
| `rain` | Chiado com gotas estalando | Mata Sombria, quando chove |
| `drone` | Grave pulsando, ar e um sino de vez em quando | Ruínas, Arena do Guardião e santuário |
| `drips` | Ronco grave e gotas com eco | Cripta Esquecida |

## Sons

Arquivo em `Client/Assets/Resources/Audio/sfx/<Nome>` (o nome é o do `SoundId`):

| Nome | Quando |
|---|---|
| `Hit`, `HitHeavy` | Golpe acertando (normal, Excellent ou crítico) |
| `SwordSwing` | Skill física: a lâmina cortando o ar |
| `Cast`, `MagicImpact` | Skill mágica saindo e chegando |
| `Good`, `Excellent`, `Miss`, `Route`, `ComboStart`, `Finisher` | Combo |
| `Step` | Passos do jogador (mais espaçados correndo) |
| `UiClick`, `UiOpen`, `UiClose` | Clique e abrir/fechar janelas |
| `LevelUp`, `RareDrop`, `UpgradeSuccess`, `UpgradeFail`, `Error` | Progresso e avisos |

## Falta

- **Voz curta das skills** (gritos de ataque): precisa de gravação; não dá para gerar bem em código. Quando houver,
  entra como um som novo por classe.
- Passos de outros jogadores e de monstros, e sons diferentes por tipo de chão.
- Ambiente da vila com gente (conversa ao longe, ferreiro batendo): por enquanto são só pássaros, grilos e vento.
