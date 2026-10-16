# Testes de carga e latência

Como repetir (na raiz do repositório, depois de `dotnet build Game.slnx -c Release`):

```bash
dotnet artifacts/bin/Game.Server/release/Game.Server.dll --port 7777 --stats 10 --latency 75 --jitter 10
dotnet artifacts/bin/Game.Bots/release/Game.Bots.dll --port 7777 --count 20 --seconds 75
```

`--latency 75` atrasa cada mensagem 75 ms em cada sentido (150 ms de ida e volta) e `--jitter 10` soma até
10 ms aleatórios. Os bots caçam nos Campos do Vento e na Mata Sombria e fazem combos apertando no centro da janela
com até ±30 ms de erro. O cliente Unity pode entrar junto com `-connect 127.0.0.1:7777 -onlinetest pasta -onlinefight`.

## 2026-10-16 — Meta da Fase 3

> 20 jogadores (ou bots) caçam juntos no mesmo mapa com 150 ms de latência simulada, o combo ainda parece justo
> e o servidor fica abaixo de 50% de CPU.

| Medida | Resultado |
| --- | --- |
| Jogadores ao mesmo tempo | 20 bots + 1 cliente Unity, nenhuma desconexão |
| Ida e volta medida pelos bots | mediana 218 ms, p95 220 ms (150 ms simulados + jitter + loops) |
| Ticks do servidor | 20,0/s estáveis; tick médio 0,4 ms, máximo 13 ms |
| CPU do servidor | até 13% de um núcleo (menos de 2% da máquina) |
| Saída do servidor | 10–14 KB/s por jogador |
| Combo dos bots, julgado pelo servidor | 96% Excellent, 4% Good, **0 combos quebrados por Miss em 230** |
| Combo do cliente Unity a 179 ms | Excellent confirmado, coelho morto, XP, Alz e coleta pelo servidor |

**Resultado: meta atingida.** Folga grande de CPU: o gargalo vai ser banda (snapshots completos a 20/s) muito
antes de processamento.

### O que o teste revelou

- **Cliente pausava sem foco.** Com várias janelas abertas, a que perdia o foco parava de ler o servidor.
  Corrigido com `runInBackground` (cliente e build).
- **Miss falso nos bots.** O bot atacava enquanto esperava a confirmação do início do combo; o golpe chegava ao
  servidor já dentro do combo, no começo da barra, e virava Miss. Era defeito do bot (o cliente real começa o combo
  na hora, por previsão). Depois da correção: 0 Miss em 230 combos.
- **Lotação.** 20 jogadores para 9 monstros num campo passam boa parte do tempo sem alvo ("Selecione um alvo").
  É questão de conteúdo (densidade de monstros por zona), a revisar na Fase 6.

### Próximas otimizações (quando o número de jogadores crescer)

- Snapshots só com o que mudou (delta) e menos vezes por segundo para entidades longe.
- Posições em 16 bits em vez de `float` de 32.
