# Teste fechado: termos, privacidade e métricas

## Termos de uso e política de privacidade

- **Onde ficam os textos:** `termos.txt` e `privacidade.txt`, na pasta `Shared/Game.Shared/Resources/Legal/`.
  - O cliente mostra os textos no cadastro, nos botões "Termos de uso" e "Privacidade".
  - O servidor publica os mesmos textos em `https://SEU-DOMINIO/api/legal/termos` e `/api/legal/privacidade`. São os links para pôr no Discord e na página do jogo.
- **Antes de abrir o teste, preencha os campos `<<preencher: ...>>`:** nome do responsável, e-mail de contato, empresa e país da VPS e serviço de backup. Os textos foram escritos com a LGPD em mente, mas não são uma consultoria jurídica; se puder, peça para alguém da área revisar.
- **Aceite:**
  - Criar conta exige marcar "Li e aceito". O servidor confere a versão, `Legal.Version` em `Shared/Game.Shared/Accounts/Legal.cs`, e grava na conta qual versão foi aceita e quando.
  - **Mudou o texto de forma importante?** Troque a `Legal.Version`. No próximo login, cada jogador vê os textos novos e precisa clicar em "Aceitar e entrar".
  - Contas de antes desta versão também passam por esse aceite uma vez.

### Pedido de exclusão de conta (LGPD)

Primeiro confira que o pedido veio do dono da conta, por exemplo pela mesma conta do Discord que pediu o convite. Depois, na VPS:

```bash
docker compose -f docker-compose.prod.yml run --rm --no-deps game --delete-account nomedaconta
```

- **O que é apagado:** a conta, os personagens, os itens, as missões, os amigos, o registro de itens e as métricas.
- **O que fica:** as denúncias e as ações de GM, pela segurança do jogo, como diz a política.
- **Jogador online:** se a pessoa estiver jogando, use `/gm kick` antes.
- **Backups antigos:** ainda têm os dados até saírem do rodízio de 14 dias.

Para atender um pedido de acesso aos dados, use os comandos de suporte: `/gm info nome` e as tabelas `item_log`, `metric_events` e `player_reports` no banco.

## Métricas

### O que é coletado

| Evento (`metric_events.kind`) | Quem manda | Quando | Valor | Detalhes |
|---|---|---|---|---|
| `session` | servidor | ao sair do jogo (ou ao desligar o servidor) | duração em segundos | `nivel=1>3;mortes;abates;excellent;good;miss;missoes;dungeons` |
| `client.tutorial` | cliente | a cada passo do tutorial alcançado | número do passo (0 = andar … 9 = fim) | nome do passo |
| `client.fps` | cliente | a cada 5 minutos jogando (e ao sair) | FPS médio | resolução e qualidade |
| `client.error` | cliente | erro do jogo (até 5 diferentes por sessão) | 0 | mensagem e onde aconteceu |

- **Convidados ficam de fora:** bots e testes com `--guests` não geram métricas.
- **Limites no servidor:** ele só aceita esses tipos, corta textos em 200 caracteres e para em 120 relatórios do cliente por sessão.
- **Opção do jogador:** as métricas do cliente podem ser desligadas em Opções > Geral. As de sessão fazem parte do funcionamento do teste e estão descritas na política.
- **Gravação:** os eventos vão para o banco junto do salvamento do personagem, na mesma transação.

### Relatório

```bash
docker compose -f docker-compose.prod.yml run --rm --no-deps game --report       # desde o início
docker compose -f docker-compose.prod.yml run --rm --no-deps game --report 7     # últimos 7 dias
```

Para rodar localmente: `dotnet run --project Server/Game.Server -- --report`.

O relatório mostra só números agregados, sem nomes:
- contas e jogadores com sessão;
- sessões (mediana, média e horas totais) e jogadores por dia;
- retenção D1 e D7;
- nível mais alto por personagem;
- monstros, mortes e missões por hora de jogo;
- notas do combo;
- funil do tutorial;
- FPS (mediana e os 10% piores);
- erros mais comuns do cliente.

### Como ler

- **Retenção:** é contada a partir do dia da primeira sessão (D0).
  - **D1** é a fração que voltou no dia seguinte.
  - **D7** é a fração que voltou 7 dias ou mais depois.
  - Só entram contas com idade suficiente para isso: a base do D7 ignora quem começou há menos de 7 dias.
- **Funil do tutorial:** responde ao critério da Fase 7, "alguém novo aprende o combo sem ajuda". Olhe quantos chegam ao passo 7 (`Finisher`, o primeiro combo completo) em relação ao passo 0. Uma queda grande num passo mostra onde o texto ou o ritmo precisa melhorar.
- **Excellent alto demais (acima de 90%) para muitos jogadores:** a janela do combo pode estar fácil. Cruze com a detecção de bot (`/gm suspeitos`).
- **FPS:** se os 10% piores estiverem abaixo de 30, veja nos detalhes (resolução e qualidade) quem sofre e compare com `Docs/desempenho.md`.

### Guardar por quanto tempo

A política diz até 12 meses. Para apagar as métricas antigas, uma vez por mês:

```bash
docker compose -f docker-compose.prod.yml exec postgres psql -U game -d game -c "DELETE FROM metric_events WHERE \"At\" < now() - interval '12 months'"
```
