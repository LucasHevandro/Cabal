using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Client.Combat;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Client.Player;
using Game.Shared.Net;
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
        private StreamWriter _report;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-onlinetest");
            if (index < 0 || !GameMode.IsOnline) return;

            var test = new GameObject("OnlineSmokeTest").AddComponent<OnlineSmokeTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "onlinetest");
            var wait = Array.IndexOf(args, "-onlinewait");
            if (wait >= 0 && wait + 1 < args.Length) float.TryParse(args[wait + 1], out test._stayOnline);
            test._fight = Array.IndexOf(args, "-onlinefight") >= 0;
            DontDestroyOnLoad(test.gameObject);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            _report = new StreamWriter(Path.Combine(_folder, "relatorio.txt"));
            Log($"Teste online: {GameMode.PlayerName} -> {GameMode.Host}:{GameMode.Port}");

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
            outcome.Ok = kills > 0 && damageDealt > 0 && xpAfter > xpBefore && loot.Alz > alzBefore && excellent > 0 && items > 0;
            outcome.Message = $"{kills} coelho(s) mortos pelo servidor ({damageDealt} de dano), {excellent} Excellent confirmados, " +
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
