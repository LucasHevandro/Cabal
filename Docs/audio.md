# Áudio

Tudo o que o jogo toca hoje é gerado em código (músicas em `MusicSynth`, sons em `ProceduralSounds`), até chegarem
arquivos de verdade. Para trocar, basta pôr o arquivo na pasta com o nome certo: o jogo usa o arquivo no lugar do som
gerado, sem mudar código.

## Músicas

| Arquivo (em `Client/Assets/Resources/Audio/music/`) | Quando toca |
|---|---|
| `city` | Vila de Partida e menu de entrada |
| `field` | Mapa de caça e estradas |
| `dungeon` | Cripta Esquecida |
| `boss` | Chefe de campo a menos de 28 m, sala do chefe da cripta e arena |

A troca é um cruzamento suave de 2 s. O volume vem das opções (Música × Volume geral). As músicas devem emendar o
fim no começo (tocam em loop). Para ouvir as geradas fora do jogo: menu **Jogo > Áudio > Exportar músicas geradas**
(salva em `Client/Builds/Musicas`, cada uma repetida duas vezes para ouvir a emenda).

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
