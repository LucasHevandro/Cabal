using System.Collections.Generic;
using Game.Client.Net;
using Game.Shared.Accounts;
using Game.Shared.Net;
using Game.Shared.Social;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Modo online: chat (Enter abre e envia, Esc cancela, Tab troca o canal) e quadro do grupo com a vida e o MP de
    /// cada membro, mais o convite para aceitar ou recusar. Quem decide tudo é o servidor; aqui só mostra e pede.
    /// </summary>
    public sealed class SocialWindows : MonoBehaviour
    {
        private const float VirtualWidth = 1920f;
        private const float VirtualHeight = 1080f;
        private const int MaxLines = 80;
        private const int VisibleLines = 9;
        private const float FadeAfterSeconds = 12f;
        private const string InputControl = "chat-input";

        private static readonly Color Panel = new Color(0.05f, 0.06f, 0.09f, 0.82f);
        private static readonly Color HpColor = new Color(0.85f, 0.2f, 0.2f);
        private static readonly Color MpColor = new Color(0.25f, 0.45f, 0.95f);
        private static readonly Color Accent = new Color(1f, 0.82f, 0.35f);

        private readonly struct Line
        {
            public readonly ChatChannel Channel;
            public readonly string Text;
            public readonly float At;

            public Line(ChatChannel channel, string text, float at)
            {
                Channel = channel;
                Text = text;
                At = at;
            }
        }

        private readonly List<Line> _lines = new List<Line>();
        private NetworkSession _session;
        private ChatChannel _channel = ChatChannel.Map;
        private string _draft = "";
        private bool _focusInput;
        private string _lastWhisperFrom = "";
        private int _scroll;

        private PartyState _party = new PartyState();
        private PartyInvite _invite;
        private float _inviteUntil;

        private GUIStyle _label;
        private GUIStyle _small;
        private GUIStyle _input;
        private GUIStyle _button;
        private float _scale = 1f;
        private Vector2 _offset;

        /// <summary>Retângulos desenhados neste quadro (em coordenadas virtuais), para o clique não ir para o mundo.</summary>
        private static readonly List<Rect> OpenRects = new List<Rect>();

        public static SocialWindows Instance { get; private set; }

        public static bool Contains(Vector2 virtualPoint)
        {
            foreach (var rect in OpenRects) if (rect.Contains(virtualPoint)) return true;
            return false;
        }

        private static void AddRect(Rect rect) => OpenRects.Add(rect);
        public PartyState Party => _party;
        public PartyInvite PendingInvite => Time.unscaledTime < _inviteUntil ? _invite : null;
        public IEnumerable<string> RecentLines { get { foreach (var line in _lines) yield return line.Text; } }

        /// <summary>Para o teste automático: manda uma linha como se o jogador tivesse digitado.</summary>
        public void Submit(string text) => SubmitLine(text);

        public void AcceptInvite() => Answer(true);

        private void Awake()
        {
            if (!GameMode.IsOnline)
            {
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            _session = NetworkSession.Instance;
            if (_session != null) _session.MessageReceived += OnMessage;
            AddLine(ChatChannel.System, "Enter abre o chat. Digite /ajuda para ver os comandos.");
        }

        private void OnDestroy()
        {
            if (_session != null) _session.MessageReceived -= OnMessage;
            if (Instance == this) Instance = null;
            OpenRects.Clear();
            GameInput.Typing = false;
        }

        private void OnMessage(INetMessage message)
        {
            switch (message)
            {
                case ChatMessage chat:
                    OnChat(chat);
                    break;
                case PartyState party:
                    _party = party;
                    break;
                case PartyInvite invite:
                    _invite = invite;
                    _inviteUntil = Time.unscaledTime + invite.Seconds;
                    AddLine(ChatChannel.System, $"{invite.InviterName} convidou você para o grupo");
                    break;
            }
        }

        private void OnChat(ChatMessage chat)
        {
            var text = Escape(chat.Text);
            switch (chat.Channel)
            {
                case ChatChannel.System:
                    AddLine(chat.Channel, text);
                    break;
                case ChatChannel.Whisper when chat.ToName.Length > 0:
                    AddLine(chat.Channel, $"Para <b>{Escape(chat.ToName)}</b>: {text}");
                    break;
                case ChatChannel.Whisper:
                    _lastWhisperFrom = chat.FromName;
                    AddLine(chat.Channel, $"De <b>{Escape(chat.FromName)}</b>: {text}");
                    break;
                default:
                    AddLine(chat.Channel, $"<b>{Escape(chat.FromName)}</b>: {text}");
                    break;
            }
        }

        private void AddLine(ChatChannel channel, string text)
        {
            _lines.Add(new Line(channel, text, Time.unscaledTime));
            if (_lines.Count > MaxLines) _lines.RemoveAt(0);
            _scroll = 0;
        }

        // ----- Desenho -----

        private void OnGUI()
        {
            if (ConnectMenu.IsOpen || _session == null || !_session.IsWelcomed) return;
            EnsureStyles();
            OpenRects.Clear();
            HandleKeys(Event.current);

            _scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            _offset = new Vector2((Screen.width - VirtualWidth * _scale) / 2, (Screen.height - VirtualHeight * _scale) / 2);
            GUI.matrix = Matrix4x4.TRS(new Vector3(_offset.x, _offset.y, 0), Quaternion.identity, new Vector3(_scale, _scale, 1));
            GUI.depth = -5;

            DrawParty();
            DrawChat();
            if (PendingInvite != null) DrawInvite();

            GUI.matrix = Matrix4x4.identity;
            GUI.color = Color.white;
        }

        /// <summary>Teclas do chat: tratadas aqui (antes da caixa de texto) para Enter e Esc não virarem texto.</summary>
        private void HandleKeys(Event e)
        {
            if (e.type != EventType.KeyDown) return;
            var enter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
            if (!GameInput.Typing)
            {
                if (!enter) return;
                GameInput.Typing = true;
                _focusInput = true;
                e.Use();
                return;
            }

            if (enter)
            {
                SubmitLine(_draft);
                CloseInput();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                CloseInput();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Tab)
            {
                _channel = _channel == ChatChannel.Map ? ChatChannel.Party : ChatChannel.Map;
                e.Use();
            }
            else if (e.keyCode == KeyCode.PageUp)
            {
                _scroll = Mathf.Min(_scroll + 3, Mathf.Max(0, _lines.Count - VisibleLines));
                e.Use();
            }
            else if (e.keyCode == KeyCode.PageDown)
            {
                _scroll = Mathf.Max(0, _scroll - 3);
                e.Use();
            }
        }

        private void CloseInput()
        {
            _draft = "";
            GameInput.Typing = false;
            GUI.FocusControl(null);
        }

        private void DrawChat()
        {
            var typing = GameInput.Typing;
            var rect = new Rect(40, VirtualHeight - 150 - 300, 460, 290); // termina antes da barra de skills
            var now = Time.unscaledTime;

            // Sem digitar, as linhas antigas somem; o fundo só aparece enquanto digita.
            if (typing) Box(rect, Panel);
            var lineHeight = 26f;
            var end = _lines.Count - (typing ? _scroll : 0);
            var start = Mathf.Max(0, end - VisibleLines);
            var y = rect.yMax - 44 - (end - start) * lineHeight;
            for (var i = start; i < end; i++)
            {
                var line = _lines[i];
                var age = now - line.At;
                var alpha = typing ? 1f : Mathf.Clamp01((FadeAfterSeconds + 2f - age) / 2f);
                if (alpha > 0)
                {
                    var text = $"<color=#{ColorUtility.ToHtmlStringRGB(ChannelColor(line.Channel))}>{Prefix(line.Channel)}{line.Text}</color>";
                    GUI.color = new Color(0, 0, 0, 0.8f * alpha);
                    GUI.Label(new Rect(rect.x + 13, y + 1, rect.width - 24, lineHeight), text, _small);
                    GUI.color = new Color(1, 1, 1, alpha);
                    GUI.Label(new Rect(rect.x + 12, y, rect.width - 24, lineHeight), text, _small);
                }
                y += lineHeight;
            }
            GUI.color = Color.white;

            if (!typing)
            {
                return;
            }
            AddRect(rect);
            var channelRect = new Rect(rect.x + 8, rect.yMax - 38, 92, 30);
            Box(channelRect, new Color(ChannelColor(_channel).r, ChannelColor(_channel).g, ChannelColor(_channel).b, 0.35f));
            GUI.Label(channelRect, _channel == ChatChannel.Party ? "Grupo" : "Mapa", _small.WithCenter());
            GUI.SetNextControlName(InputControl);
            _draft = GUI.TextField(new Rect(channelRect.xMax + 6, rect.yMax - 38, rect.width - 114, 30), _draft, ChatRules.MaxLength, _input);
            if (_focusInput)
            {
                GUI.FocusControl(InputControl);
                _focusInput = false;
            }
            GUI.Label(new Rect(rect.x + 12, rect.y + 4, rect.width - 24, 22),
                "<size=12>Enter envia · Esc cancela · Tab troca o canal · PgUp/PgDn rola · /ajuda</size>", _small);
        }

        private void DrawParty()
        {
            if (_party.Members.Count == 0) return;
            var x = 20f;
            var y = 364f;
            var height = 38f + _party.Members.Count * 62f;
            var panel = new Rect(x, y, 320, height);
            Box(panel, new Color(0, 0, 0, 0.45f));
            AddRect(panel);
            GUI.Label(new Rect(x + 10, y + 4, 300, 26), $"<b>Grupo</b>  <size=13>({_party.Members.Count}/{PartyRules.MaxMembers})</size>", _small);
            if (GUI.Button(new Rect(x + 240, y + 6, 70, 24), "Sair", _button))
                _session.Send(new PartyCommand { Action = PartyActionKind.Leave });
            y += 36;
            foreach (var member in _party.Members)
            {
                var me = member.Id == _session.PlayerId;
                var leader = member.Id == _party.LeaderId ? "★ " : "";
                var className = AccountRules.ClassDisplayName((CharacterClass)member.Class);
                GUI.color = member.Near || me ? Color.white : new Color(1, 1, 1, 0.55f);
                GUI.Label(new Rect(x + 10, y - 2, 300, 26),
                    $"{leader}<b>{Escape(member.Name)}</b>{(me ? " (você)" : "")}  <size=13>Nv {member.Level} · {className}</size>", _small);
                var hp = new Rect(x + 10, y + 26, 300, 16);
                Box(hp, new Color(0, 0, 0, 0.6f));
                Box(new Rect(hp.x, hp.y, hp.width * Mathf.Clamp01(member.Hp / (float)Mathf.Max(1, member.MaxHp)), hp.height), HpColor);
                GUI.Label(new Rect(hp.x, hp.y - 3, hp.width, 22),
                    member.Dead ? "<size=12><b>MORTO</b></size>" : $"<size=12>{member.Hp}/{member.MaxHp}</size>", _small.WithCenter());
                var mp = new Rect(x + 10, y + 44, 300, 6);
                Box(mp, new Color(0, 0, 0, 0.6f));
                Box(new Rect(mp.x, mp.y, mp.width * Mathf.Clamp01(member.Mp / (float)Mathf.Max(1, member.MaxMp)), mp.height), MpColor);
                GUI.color = Color.white;
                y += 62;
            }
        }

        private void DrawInvite()
        {
            var invite = PendingInvite;
            var rect = new Rect(VirtualWidth / 2 - 230, 300, 460, 130);
            Box(rect, Panel);
            AddRect(rect);
            var seconds = Mathf.CeilToInt(_inviteUntil - Time.unscaledTime);
            GUI.Label(new Rect(rect.x, rect.y + 14, rect.width, 30), $"<b>{Escape(invite.InviterName)}</b> convidou você para o grupo", _label.WithCenter());
            GUI.Label(new Rect(rect.x, rect.y + 44, rect.width, 22), $"<size=13>expira em {seconds} s</size>", _small.WithCenter());
            if (GUI.Button(new Rect(rect.x + 60, rect.y + 76, 150, 36), "<b>Aceitar</b>", _button)) Answer(true);
            if (GUI.Button(new Rect(rect.xMax - 210, rect.y + 76, 150, 36), "Recusar", _button)) Answer(false);
        }

        private void Answer(bool accept)
        {
            var invite = PendingInvite;
            if (invite == null) return;
            _session.Send(new PartyCommand { Action = accept ? PartyActionKind.Accept : PartyActionKind.Decline, TargetId = invite.InviterId });
            _invite = null;
            _inviteUntil = 0;
        }

        // ----- Envio -----

        private void SubmitLine(string text)
        {
            var input = ChatParser.Parse(text, _channel, _lastWhisperFrom);
            switch (input.Kind)
            {
                case ChatInputKind.Message:
                    _session.Send(new ChatSend { Channel = input.Channel, Target = input.Target ?? "", Text = input.Text });
                    if (input.Channel != ChatChannel.Whisper) _channel = input.Channel;
                    break;
                case ChatInputKind.PartyInvite:
                    _session.Send(new PartyCommand { Action = PartyActionKind.Invite, TargetName = input.Target });
                    break;
                case ChatInputKind.PartyLeave:
                    _session.Send(new PartyCommand { Action = PartyActionKind.Leave });
                    break;
                case ChatInputKind.PartyKick:
                    _session.Send(new PartyCommand { Action = PartyActionKind.Kick, TargetName = input.Target });
                    break;
                case ChatInputKind.PartyPromote:
                    _session.Send(new PartyCommand { Action = PartyActionKind.Promote, TargetName = input.Target });
                    break;
                case ChatInputKind.Help:
                    AddLine(ChatChannel.System, ChatParser.HelpText);
                    break;
                case ChatInputKind.Invalid:
                    AddLine(ChatChannel.System, input.Error);
                    break;
            }
        }

        // ----- Utilidades -----

        private static string Prefix(ChatChannel channel) => channel switch
        {
            ChatChannel.Party => "[Grupo] ",
            ChatChannel.System => "",
            ChatChannel.Whisper => "",
            _ => "",
        };

        private static Color ChannelColor(ChatChannel channel) => channel switch
        {
            ChatChannel.Party => new Color(0.45f, 0.85f, 1f),
            ChatChannel.Whisper => new Color(0.95f, 0.55f, 1f),
            ChatChannel.System => Accent,
            _ => new Color(0.95f, 0.95f, 0.95f),
        };

        /// <summary>O texto de outro jogador não pode virar formatação (rich text) na tela.</summary>
        private static string Escape(string text) => string.IsNullOrEmpty(text) ? "" : text.Replace("<", "‹").Replace(">", "›");

        private static void Box(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, wordWrap = false };
            _label.normal.textColor = Color.white;
            _small = new GUIStyle(_label) { fontSize = 16, clipping = TextClipping.Clip };
            _input = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 14, richText = true };
        }
    }
}
