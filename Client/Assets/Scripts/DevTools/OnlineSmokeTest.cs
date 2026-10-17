using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Client.Combat;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Client.Player;
using Game.Shared.Net;
using Game.Shared.Progression;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Verificação automática do modo online. Rodar com
    /// <c>-connect host:porta -name Nome -onlinetest pasta</c>: conecta, espera ver as entidades do servidor,
    /// tira screenshot, grava um relatório (SUCESSO/FALHA) e fecha. <c>-onlinefight</c> também luta contra um coelho
    /// pelo servidor. <c>-onlinewait N</c> mantém o cliente aberto N segundos (para outro cliente de teste enxergá-lo).
    /// </summary>
    public sealed class OnlineSmokeTest : MonoBehaviour
    {
        private string _folder;
        private float _stayOnline;
        private bool _fight;
        private bool _uiTour;
        private bool _social;
        private StreamWriter _report;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-onlinetest");
            // Com -autologin o jogo só fica online depois do menu recarregar a cena: o teste sobrevive à troca e espera.
            if (index < 0 || (!GameMode.IsOnline && string.IsNullOrEmpty(GameMode.AutoLoginAccount))) return;

            var test = new GameObject("OnlineSmokeTest").AddComponent<OnlineSmokeTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "onlinetest");
            var wait = Array.IndexOf(args, "-onlinewait");
            if (wait >= 0 && wait + 1 < args.Length) float.TryParse(args[wait + 1], out test._stayOnline);
            test._fight = Array.IndexOf(args, "-onlinefight") >= 0;
            test._uiTour = Array.IndexOf(args, "-onlineui") >= 0;
            test._social = Array.IndexOf(args, "-onlinesocial") >= 0;
            DontDestroyOnLoad(test.gameObject);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            _report = new StreamWriter(Path.Combine(_folder, "relatorio.txt"));

            var loginGiveUp = Time.realtimeSinceStartup + 20f;
            while (!GameMode.IsOnline && Time.realtimeSinceStartup < loginGiveUp) yield return null;
            if (!GameMode.IsOnline)
            {
                Finish("FALHA: o login automático não terminou");
                yield break;
            }
            yield return null; // deixa a cena recarregada iniciar
            Log($"Teste online: {GameMode.PlayerName} (personagem {GameMode.CharacterId}) -> {GameMode.Host}:{GameMode.Port}");

            var giveUpAt = Time.realtimeSinceStartup + 15f;
            NetworkSession session = null;
            while (Time.realtimeSinceStartup < giveUpAt)
            {
                session = NetworkSession.Instance;
                if (session != null && !string.IsNullOrEmpty(session.Error)) break;
                if (session != null && session.IsWelcomed && session.Clock.IsSynced && session.Views.Count > 0) break;
                yield return null;
            }

            if (session == null || !session.IsWelcomed)
            {
                Finish($"FALHA: não entrou no mundo ({session?.Error ?? "sem NetworkSession"})");
                yield break;
            }

            // Dá tempo de chegarem alguns snapshots e a interpolação andar.
            yield return new WaitForSecondsRealtime(1.5f);

            var monsters = 0;
            var players = 0;
            foreach (var view in session.Views.Values)
            {
                if (view.Kind == EntityKind.Monster) monsters++;
                else players++;
            }
            Log($"Entrou como id {session.PlayerId}; RTT {session.Clock.LastRtt * 1000:0} ms; offset {session.Clock.Offset:0.000} s");
            if (PlayerCombat.Local != null)
            {
                var me = PlayerCombat.Local;
                var progress = me.GetComponent<PlayerProgression>().Progress;
                var bag = me.GetComponent<PlayerLoot>();
                var count = 0;
                foreach (var stack in bag.Inventory) count += stack.Quantity;
                Log($"Personagem ao entrar: Nv {progress.Level}, XP {progress.Xp}, Alz {bag.Alz}, itens {count}, HP {me.Hp.Current}/{me.Hp.Max}");
            }
            Log($"Vendo {monsters} monstros e {players} outros jogadores");

            // Seleciona o monstro mais perto para a foto mostrar o anel e a plaquinha.
            var selector = PlayerCombat.Local != null ? PlayerCombat.Local.GetComponent<TargetSelector>() : null;
            if (selector != null) selector.SelectNext();

            Capture("1-online");
            yield return new WaitForEndOfFrame();
            yield return null;

            var fightResult = "";
            if (_fight)
            {
                var outcome = new FightOutcome();
                yield return Fight(session, outcome);
                if (!outcome.Ok)
                {
                    Finish("FALHA: " + outcome.Message);
                    yield break;
                }
                fightResult = "; " + outcome.Message;
            }

            if (_uiTour)
            {
                var outcome = new FightOutcome();
                yield return UiTour(outcome);
                if (!outcome.Ok)
                {
                    Finish("FALHA: " + outcome.Message);
                    yield break;
                }
                fightResult += "; " + outcome.Message;
            }

            if (_social)
            {
                var outcome = new FightOutcome();
                yield return Social(session, outcome);
                if (!outcome.Ok)
                {
                    Finish("FALHA: " + outcome.Message);
                    yield break;
                }
                fightResult += "; " + outcome.Message;
            }

            if (_stayOnline > 0)
            {
                Log($"Ficando online por {_stayOnline:0} s");
                var maxPlayers = 0;
                var until = Time.realtimeSinceStartup + _stayOnline;
                while (Time.realtimeSinceStartup < until)
                {
                    var seen = 0;
                    foreach (var view in session.Views.Values) if (view.Kind == EntityKind.Player) seen++;
                    maxPlayers = Mathf.Max(maxPlayers, seen);
                    yield return null;
                }
                players = Mathf.Max(players, maxPlayers);
                Log($"Enquanto online viu até {maxPlayers} outros jogadores");
                Capture("4-antes-de-sair");
                yield return new WaitForEndOfFrame();
                yield return null;
            }

            Finish(monsters > 0
                ? $"SUCESSO: online com {monsters} monstros e {players} jogadores à vista{fightResult}"
                : "FALHA: nenhum monstro recebido do servidor");
        }

        private sealed class FightOutcome
        {
            public bool Ok;
            public string Message = "";
        }

        /// <summary>
        /// Anda até um coelho (velocidade de corrida, que o servidor aceita), faz um combo com 3 acertos no centro da
        /// janela, termina de matar e confere o que o servidor decidiu: notas do combo, XP, Alz e coleta do drop.
        /// </summary>
        private IEnumerator Fight(NetworkSession session, FightOutcome outcome)
        {
            var combat = PlayerCombat.Local;
            var motor = combat.GetComponent<PlayerMotor>();
            var selector = combat.GetComponent<TargetSelector>();
            var loot = combat.GetComponent<PlayerLoot>();
            var grades = new List<byte>();
            var damageDealt = 0;
            void OnMessage(INetMessage message)
            {
                if (message is CombatEvent hit && hit.AttackerId == session.PlayerId)
                {
                    damageDealt += hit.Damage;
                    if (hit.Grade != 255) grades.Add(hit.Grade);
                }
            }
            session.MessageReceived += OnMessage;
            var xpBefore = combat.GetComponent<PlayerProgression>().Progress.Xp;
            var alzBefore = loot.Alz;

            // Caça até cair algum item (cerca de 1 em 3 coelhos não derruba nada), no máximo 4 coelhos.
            // O combo é feito só no primeiro.
            var kills = 0;
            NetEntityView rabbit = null;
            for (var round = 0; round < 4; round++)
            {
                rabbit = null;
                foreach (var view in session.Views.Values)
                {
                    if (view.TypeId != "coelho_saltador" || view.IsDead) continue;
                    if (rabbit == null || Distance(view, motor) < Distance(rabbit, motor)) rabbit = view;
                }
                if (rabbit == null)
                {
                    outcome.Message = "nenhum coelho à vista";
                    yield break;
                }
                Log($"Indo até o coelho {rabbit.Id} a {Distance(rabbit, motor):0} m");

                // Anda a 7 m/s (abaixo do limite do servidor) até ficar a 1,8 m.
                var giveUpAt = Time.realtimeSinceStartup + 30f;
                while (Distance(rabbit, motor) > 1.8f && Time.realtimeSinceStartup < giveUpAt)
                {
                    var direction = rabbit.transform.position - motor.transform.position;
                    direction.y = 0;
                    motor.Teleport(motor.transform.position + direction.normalized * Mathf.Min(direction.magnitude - 1.7f, 7f * Time.deltaTime));
                    yield return null;
                }
                selector.Select(rabbit.Target);
                yield return new WaitForSecondsRealtime(0.3f);

                if (round == 0)
                {
                    // Combo: espera o marcador no centro da janela e aperta a skill 1.
                    combat.RequestStartCombo();
                    for (var i = 0; i < 3 && combat.Combo.State == Shared.Combat.ComboState.Active && !rabbit.IsDead; i++)
                    {
                        while (combat.Combo.State == Shared.Combat.ComboState.Active &&
                               combat.Combo.GetCursor(Time.time) < combat.Combo.Config.WindowCenter)
                        {
                            Follow(motor, rabbit); // coelhos passeiam: acompanha como um jogador faria
                            yield return null;
                        }
                        if (combat.Combo.State != Shared.Combat.ComboState.Active) break;
                        combat.RequestSkill(0);
                        yield return null;
                    }
                    Capture("2-combo-online");
                    yield return new WaitForEndOfFrame();
                }

                // Termina de matar fora do combo, se precisar.
                giveUpAt = Time.realtimeSinceStartup + 20f;
                while (!rabbit.IsDead && Time.realtimeSinceStartup < giveUpAt)
                {
                    Follow(motor, rabbit);
                    if (selector.Current != rabbit.Target) selector.Select(rabbit.Target);
                    if (combat.CooldownRemaining(0) <= 0) combat.RequestSkill(0);
                    yield return null;
                }
                if (!rabbit.IsDead) break;
                kills++;
                yield return new WaitForSecondsRealtime(0.8f);
                Log($"Coelho {kills} morto; itens no chão {LootPickup.All.Count}");
                if (LootPickup.All.Count > 0 || loot.Inventory.Count > 0) break;
            }
            session.MessageReceived -= OnMessage;

            var xpAfter = combat.GetComponent<PlayerProgression>().Progress.Xp;
            Log($"Dano confirmado pelo servidor {damageDealt}; notas do combo [{string.Join(",", grades)}]");
            Log($"XP {xpBefore} -> {xpAfter}; Alz {alzBefore} -> {loot.Alz}; itens no chão {LootPickup.All.Count}");
            Capture("3-drop-online");
            yield return new WaitForEndOfFrame();

            // Coleta: anda por cima de cada item vindo do servidor (o pedido vai ao servidor, que confirma).
            var pickupGiveUp = Time.realtimeSinceStartup + 10f;
            while (LootPickup.All.Count > 0 && Time.realtimeSinceStartup < pickupGiveUp)
            {
                var item = LootPickup.All[0];
                var offset = item.transform.position - motor.transform.position;
                offset.y = 0;
                motor.Teleport(motor.transform.position + offset.normalized * Mathf.Min(offset.magnitude, 7f * Time.deltaTime));
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            var items = 0;
            foreach (var stack in loot.Inventory) items += stack.Quantity;

            var excellent = grades.FindAll(g => g == (byte)Shared.Combat.ComboGrade.Excellent).Count;
            // Personagem de conta novo ainda não tem o combo (libera na missão "A arte do combo"): aí não exige Excellent.
            var comboLocked = CharacterState.HasSheet && !CharacterState.Sheet.ComboUnlocked;
            outcome.Ok = kills > 0 && damageDealt > 0 && xpAfter > xpBefore && loot.Alz > alzBefore && (excellent > 0 || comboLocked) && items > 0;
            outcome.Message = $"{kills} coelho(s) mortos pelo servidor ({damageDealt} de dano), {(comboLocked ? "combo ainda bloqueado pela missão" : $"{excellent} Excellent confirmados")}, " +
                              $"XP +{xpAfter - xpBefore}, Alz +{loot.Alz - alzBefore}, {items} itens coletados";
        }

        /// <summary>Dá um passo (7 m/s) na direção do alvo se ele estiver a mais de 1,6 m.</summary>
        private static void Follow(PlayerMotor motor, NetEntityView target)
        {
            var direction = target.transform.position - motor.transform.position;
            direction.y = 0;
            if (direction.magnitude <= 1.6f) return;
            motor.Teleport(motor.transform.position + direction.normalized * Mathf.Min(direction.magnitude - 1.5f, 7f * Time.deltaTime));
        }

        private static float Distance(NetEntityView view, PlayerMotor motor)
        {
            var offset = view.transform.position - motor.transform.position;
            offset.y = 0;
            return offset.magnitude;
        }

        /// <summary>
        /// Passeio pela interface online: inventário, ficha, conversa com o ancião, missão "Primeiros passos"
        /// (aceitar, falar com a ferreira, entregar) e a janela da ferreira — com screenshots de cada uma.
        /// </summary>
        private IEnumerator UiTour(FightOutcome outcome)
        {
            var windows = UI.GameWindows.Instance;
            var me = PlayerCombat.Local;
            if (windows == null || me == null)
            {
                outcome.Message = "janelas do jogo online não existem";
                yield break;
            }
            var giveUp = Time.realtimeSinceStartup + 5f;
            while (!CharacterState.HasSheet && Time.realtimeSinceStartup < giveUp) yield return null;
            if (!CharacterState.HasSheet)
            {
                outcome.Message = "a ficha do personagem não chegou";
                yield break;
            }
            var weapon = CharacterState.Equipment[Game.Shared.Items.EquipSlot.Weapon];
            Log($"Ficha: classe {CharacterState.Class}, ataque {CharacterState.Sheet.AttackPower}, combo liberado {CharacterState.Sheet.ComboUnlocked}; " +
                $"arma {weapon?.ItemId ?? "nenhuma"}, {CharacterState.Bag.FreeSlots} posições livres na bolsa");
            if (weapon == null)
            {
                outcome.Message = "personagem sem a arma inicial";
                yield break;
            }

            windows.SetInventoryOpen(true);
            windows.SetAttributesOpen(true);
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("5-inventario");
            yield return new WaitForEndOfFrame();
            windows.SetInventoryOpen(false);
            windows.SetAttributesOpen(false);

            var quest = "primeiros_passos";
            if (!CharacterState.QuestCompleted(quest))
            {
                yield return WalkTo(NpcCatalog.Elder);
                windows.TalkToNearestNpc();
                yield return WaitFor(() => CharacterState.Dialog != null, 3f);
                Capture("6-anciao");
                yield return new WaitForEndOfFrame();
                if (CharacterState.Dialog == null || !CharacterState.Dialog.Offered.Contains(quest))
                {
                    outcome.Message = "o ancião não ofereceu a missão";
                    yield break;
                }
                NetworkSession.Instance.Send(new QuestCommand { Action = QuestActionKind.Accept, QuestId = quest });
                yield return WaitFor(() => CharacterState.CountsOf(quest) != null, 3f);

                yield return WalkTo(NpcCatalog.Smith);
                windows.TalkToNearestNpc();
                yield return WaitFor(() => CharacterState.Dialog != null && CharacterState.Dialog.NpcId == NpcCatalog.Smith, 3f);
                windows.SelectForSmith(weapon.Uid);
                yield return new WaitForSecondsRealtime(0.5f);
                Capture("7-ferreira");
                yield return new WaitForEndOfFrame();

                yield return WalkTo(NpcCatalog.Elder);
                windows.TalkToNearestNpc();
                yield return WaitFor(() => CharacterState.Dialog != null && CharacterState.Dialog.ReadyToTurnIn.Contains(quest), 3f);
                NetworkSession.Instance.Send(new QuestCommand { Action = QuestActionKind.TurnIn, QuestId = quest });
                yield return WaitFor(() => CharacterState.QuestCompleted(quest), 3f);
                if (!CharacterState.QuestCompleted(quest))
                {
                    outcome.Message = "a missão não foi concluída";
                    yield break;
                }
            }

            yield return new WaitForSecondsRealtime(0.5f);
            Capture("8-missao-concluida");
            yield return new WaitForEndOfFrame();
            outcome.Ok = true;
            outcome.Message = $"missão \"{QuestCatalog.Get(quest).Name}\" concluída, Nv {me.Level}, {CharacterState.Bag.Count("pocao_hp_p")} poções";
        }

        /// <summary>Anda até perto do NPC no ritmo de caminhada (o servidor recusa teleporte).</summary>
        /// <summary>
        /// Espera o convite de grupo de um bot (Game.Bots --invite Nome), aceita, manda mensagens no grupo e no mapa
        /// (uma com palavrão, que o servidor filtra) e confere o quadro do grupo, o chat e o sussurro do bot.
        /// </summary>
        private IEnumerator Social(NetworkSession session, FightOutcome outcome)
        {
            var social = UI.SocialWindows.Instance;
            if (social == null)
            {
                outcome.Message = "sem SocialWindows";
                yield break;
            }
            yield return WaitFor(() => social.PendingInvite != null, 30f);
            if (social.PendingInvite == null)
            {
                outcome.Message = "nenhum convite de grupo chegou";
                yield break;
            }
            Log($"Convite de {social.PendingInvite.InviterName}");
            Capture("5-convite");
            yield return new WaitForEndOfFrame();
            yield return null;

            social.AcceptInvite();
            yield return WaitFor(() => social.Party.Members.Count >= 2, 5f);
            if (social.Party.Members.Count < 2)
            {
                outcome.Message = "aceitou o convite mas o grupo não chegou";
                yield break;
            }
            social.Submit("/g oi, cheguei!");
            social.Submit("/m que porra de lobo");
            UI.GameInput.Typing = true; // mostra a caixa do chat aberta na foto
            yield return WaitFor(() => Contains(social, "bem-vindo ao grupo") && Contains(social, "sussurro do bot")
                                      && Contains(social, "oi, cheguei") && Contains(social, "que *****"), 8f);
            var ok = Contains(social, "bem-vindo ao grupo") && Contains(social, "sussurro do bot") && Contains(social, "que *****");
            Capture("6-grupo-e-chat");
            yield return new WaitForEndOfFrame();
            yield return null;
            UI.GameInput.Typing = false;
            foreach (var line in social.RecentLines) Log("Chat: " + line);
            if (!ok)
            {
                outcome.Message = "o chat não recebeu as mensagens esperadas";
                yield break;
            }
            // Amigos, bloqueio e denúncia (o bot precisa ter entrado com conta: Game.Bots --accounts).
            var inviter = social.Party.Members.Find(m => m.Id != session.PlayerId).Name;
            social.Submit($"/amigo {inviter}");
            yield return WaitFor(() => social.Lists.Friends.Exists(f => f.Name == inviter && f.Online), 5f);
            if (!social.Lists.Friends.Exists(f => f.Name == inviter && f.Online))
            {
                outcome.Message = $"{inviter} não apareceu online na lista de amigos";
                yield break;
            }
            social.SetFriendsOpen(true);
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("7-amigos");
            yield return new WaitForEndOfFrame();
            yield return null;

            social.Submit($"/bloquear {inviter}");
            yield return WaitFor(() => social.Lists.Blocked.Contains(inviter), 5f);
            var blocked = social.Lists.Blocked.Contains(inviter) && !social.Lists.Friends.Exists(f => f.Name == inviter);
            Capture("8-bloqueado");
            yield return new WaitForEndOfFrame();
            yield return null;
            social.Submit($"/desbloquear {inviter}");
            yield return WaitFor(() => social.Lists.Blocked.Count == 0, 5f);
            social.Submit($"/denunciar {inviter} teste automático de denúncia");
            yield return WaitFor(() => Contains(social, $"Denúncia de {inviter} enviada"), 5f);
            social.SetFriendsOpen(false);
            if (!blocked || social.Lists.Blocked.Count != 0 || !Contains(social, $"Denúncia de {inviter} enviada"))
            {
                outcome.Message = "bloquear, desbloquear ou denunciar não funcionou";
                yield break;
            }

            outcome.Ok = true;
            outcome.Message = $"grupo com {social.Party.Members.Count} ({string.Join(", ", social.Party.Members.ConvertAll(m => m.Name))}), " +
                              "chat, filtro, amigos, bloqueio e denúncia ok";
        }

        private static bool Contains(UI.SocialWindows social, string text)
        {
            foreach (var line in social.RecentLines) if (line.Contains(text)) return true;
            return false;
        }

        private IEnumerator WalkTo(string npcId)
        {
            var npc = NpcCatalog.Find(npcId);
            var motor = PlayerCombat.Local.GetComponent<PlayerMotor>();
            var target = new Vector3(npc.X, 0, npc.Z - 2f);
            var giveUp = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < giveUp)
            {
                var position = motor.transform.position;
                var flat = new Vector3(target.x - position.x, 0, target.z - position.z);
                if (flat.magnitude < 0.2f) break;
                motor.Teleport(position + Vector3.ClampMagnitude(flat, 6f * Time.deltaTime));
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.3f); // a última posição chega ao servidor
        }

        private static IEnumerator WaitFor(Func<bool> condition, float seconds)
        {
            var giveUp = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < giveUp) yield return null;
        }

        private void Capture(string name)
        {
            var path = Path.Combine(_folder, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log($"Screenshot: {path}");
        }

        private void Log(string line)
        {
            Debug.Log("[OnlineTest] " + line);
            _report?.WriteLine(line);
            _report?.Flush();
        }

        private void Finish(string result)
        {
            Log(result);
            StartCoroutine(QuitSoon(result.StartsWith("SUCESSO") ? 0 : 1));
        }

        private IEnumerator QuitSoon(int code)
        {
            yield return new WaitForSecondsRealtime(1f);
            _report?.Dispose();
            _report = null;
            Application.Quit(code);
        }
    }
}
