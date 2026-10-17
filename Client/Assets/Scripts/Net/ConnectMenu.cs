using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Shared.Accounts;
using Game.Shared.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Client.Net
{
    /// <summary>
    /// Tela inicial: entrar com a conta (ou criar), escolher ou criar personagem e jogar; ou jogar offline.
    /// Ao escolher o personagem, recarrega a cena já no modo online com o token e o personagem.
    /// Com <c>-autologin conta senha</c> faz tudo sozinho (testes): cria conta e personagem se faltarem.
    /// </summary>
    public sealed class ConnectMenu : MonoBehaviour
    {
        private enum Screen
        {
            Login,
            Register,
            Characters,
            CreateCharacter,
            Busy,
        }

        private Screen _screen = Screen.Login;
        private string _address = "127.0.0.1:7777";
        private string _account = "";
        private string _password = "";
        private string _passwordAgain = "";
        private string _newName = "";
        private CharacterClass _newClass = CharacterClass.Blade;
        private string _message = "";
        private bool _messageIsError;
        private string _busyText = "";
        private AccountClient _api;
        private List<CharacterSummary> _characters = new List<CharacterSummary>();
        private GUIStyle _title;
        private GUIStyle _label;
        private GUIStyle _small;

        public static bool IsOpen => !GameMode.ChoiceMade;

        private void Awake()
        {
            if (GameMode.ChoiceMade)
            {
                enabled = false;
                return;
            }
            _address = $"{GameMode.Host}:{GameMode.Port}";
            if (!string.IsNullOrEmpty(GameMode.AutoLoginAccount)) _ = AutoLoginAsync();
        }

        // ===== Ações =====

        private async Task LoginAsync()
        {
            if (!PrepareApi()) return;
            await Run(Loc.T("login.entering"), async () =>
            {
                var login = await _api.LoginAsync(_account, _password);
                if (!login.Ok)
                {
                    ShowError(login.Error);
                    _screen = Screen.Login;
                    return;
                }
                _password = "";
                await LoadCharactersAsync();
            });
        }

        private async Task RegisterAsync()
        {
            var error = (AccountRules.ValidateAccount(_account) ?? AccountRules.ValidatePassword(_password))?.ToString();
            if (error == null && _password != _passwordAgain) error = Loc.T("login.passwords_differ");
            if (error != null)
            {
                ShowError(error);
                return;
            }
            if (!PrepareApi()) return;
            await Run(Loc.T("login.creating_account"), async () =>
            {
                var register = await _api.RegisterAsync(_account, _password);
                if (!register.Ok)
                {
                    ShowError(register.Error);
                    _screen = Screen.Register;
                    return;
                }
                ShowInfo(Loc.T("login.account_created"));
                await LoginAsync();
            });
        }

        private async Task LoadCharactersAsync()
        {
            var list = await _api.ListCharactersAsync();
            if (!list.Ok)
            {
                ShowError(list.Error);
                _screen = Screen.Login;
                return;
            }
            _characters = list.Value.Characters ?? new List<CharacterSummary>();
            _screen = _characters.Count == 0 ? Screen.CreateCharacter : Screen.Characters;
        }

        private async Task CreateCharacterAsync()
        {
            var error = AccountRules.ValidateCharacterName(_newName)?.ToString();
            if (error != null)
            {
                ShowError(error);
                return;
            }
            await Run(Loc.T("login.creating_character"), async () =>
            {
                var created = await _api.CreateCharacterAsync(_newName, _newClass);
                if (!created.Ok)
                {
                    ShowError(created.Error);
                    _screen = Screen.CreateCharacter;
                    return;
                }
                _newName = "";
                ShowInfo(Loc.T("login.character_created", created.Value.Name));
                await LoadCharactersAsync();
            });
        }

        private void Play(CharacterSummary character)
        {
            GameMode.SetOnlineCharacter(GameMode.Host, GameMode.Port, _api.Token, character.Id, character.Name);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>Testes: entra (ou cria a conta), usa o primeiro personagem (ou cria um) e joga.</summary>
        private async Task AutoLoginAsync()
        {
            _account = GameMode.AutoLoginAccount;
            _password = GameMode.AutoLoginPassword;
            if (!PrepareApi()) return;
            _screen = Screen.Busy;
            _busyText = Loc.T("login.auto");

            var login = await _api.LoginAsync(_account, _password);
            if (!login.Ok)
            {
                await _api.RegisterAsync(_account, _password);
                login = await _api.LoginAsync(_account, _password);
            }
            if (!login.Ok)
            {
                ShowError(Loc.T("login.auto_failed", login.Error));
                _screen = Screen.Login;
                return;
            }

            var list = await _api.ListCharactersAsync();
            var characters = list.Ok ? list.Value.Characters : new List<CharacterSummary>();
            if (characters.Count == 0)
            {
                var name = "Teste" + Mathf.Abs(_account.GetHashCode() % 100000);
                var created = await _api.CreateCharacterAsync(name, CharacterClass.Blade);
                if (!created.Ok)
                {
                    ShowError(Loc.T("login.auto_create_failed", created.Error));
                    _screen = Screen.Login;
                    return;
                }
                characters = new List<CharacterSummary> { created.Value };
            }
            Debug.Log($"[Login] Login automático como {_account}, personagem {characters[0].Name} (id {characters[0].Id})");
            Play(characters[0]);
        }

        private bool PrepareApi()
        {
            if (!GameMode.TryParseAddress(_address, out var host, out var port))
            {
                ShowError(Loc.T("login.bad_address"));
                return false;
            }
            if (_api == null || GameMode.Host != host || GameMode.Port != port) _api = new AccountClient(host, port);
            GameMode.SetServer(host, port);
            return true;
        }

        private async Task Run(string busyText, System.Func<Task> work)
        {
            var previous = _screen;
            _screen = Screen.Busy;
            _busyText = busyText;
            await work();
            if (_screen == Screen.Busy) _screen = previous;
        }

        private void ShowError(string text)
        {
            _message = text;
            _messageIsError = true;
        }

        private void ShowInfo(string text)
        {
            _message = text;
            _messageIsError = false;
        }

        // ===== Desenho =====

        private void OnGUI()
        {
            if (GameMode.ChoiceMade) return;
            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 17 };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };

            GUI.color = new Color(0, 0, 0, 0.82f);
            GUI.DrawTexture(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var scale = Mathf.Max(1f, UnityEngine.Screen.height / 900f);
            var width = 480 * scale;
            var height = 470 * scale;
            var area = new Rect((UnityEngine.Screen.width - width) / 2, (UnityEngine.Screen.height - height) / 2, width, height);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Space(8 * scale);
            GUILayout.Label(Loc.T("login.title"), _title);
            GUILayout.Space(12 * scale);

            switch (_screen)
            {
                case Screen.Login: DrawLogin(scale); break;
                case Screen.Register: DrawRegister(scale); break;
                case Screen.Characters: DrawCharacters(scale); break;
                case Screen.CreateCharacter: DrawCreate(scale); break;
                case Screen.Busy: GUILayout.Label(_busyText, _label); break;
            }

            if (!string.IsNullOrEmpty(_message))
            {
                GUILayout.Space(8 * scale);
                GUI.color = _messageIsError ? new Color(1f, 0.55f, 0.5f) : new Color(0.6f, 1f, 0.6f);
                GUILayout.Label(_message, _small);
                GUI.color = Color.white;
            }
            GUILayout.EndArea();
        }

        private void DrawLogin(float scale)
        {
            GUILayout.Label(Loc.T("login.server"), _label);
            _address = GUILayout.TextField(_address, 60, GUILayout.Height(26 * scale));
            GUILayout.Label(Loc.T("login.account"), _label);
            _account = GUILayout.TextField(_account, AccountRules.MaxAccountLength, GUILayout.Height(26 * scale));
            GUILayout.Label(Loc.T("login.password"), _label);
            _password = GUILayout.PasswordField(_password, '•', AccountRules.MaxPasswordLength, GUILayout.Height(26 * scale));
            GUILayout.Space(10 * scale);
            if (GUILayout.Button(Loc.T("login.enter"), GUILayout.Height(38 * scale))) _ = LoginAsync();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("login.create_account"), GUILayout.Height(30 * scale)))
            {
                _screen = Screen.Register;
                _message = "";
            }
            if (GUILayout.Button(Loc.T("login.offline"), GUILayout.Height(30 * scale))) GameMode.SetOffline();
            if (GUILayout.Button(Loc.T("options.title"), GUILayout.Height(30 * scale))) UI.OptionsWindow.Open();
            GUILayout.EndHorizontal();
        }

        private void DrawRegister(float scale)
        {
            GUILayout.Label(Loc.T("login.new_account"), _label);
            GUILayout.Label(Loc.T("login.account_rule", AccountRules.MinAccountLength, AccountRules.MaxAccountLength), _small);
            _account = GUILayout.TextField(_account, AccountRules.MaxAccountLength, GUILayout.Height(26 * scale));
            GUILayout.Label(Loc.T("login.password_rule", AccountRules.MinPasswordLength), _small);
            _password = GUILayout.PasswordField(_password, '•', AccountRules.MaxPasswordLength, GUILayout.Height(26 * scale));
            GUILayout.Label(Loc.T("login.repeat_password"), _small);
            _passwordAgain = GUILayout.PasswordField(_passwordAgain, '•', AccountRules.MaxPasswordLength, GUILayout.Height(26 * scale));
            GUILayout.Space(10 * scale);
            if (GUILayout.Button(Loc.T("login.create_and_enter"), GUILayout.Height(38 * scale))) _ = RegisterAsync();
            if (GUILayout.Button(Loc.T("ui.back"), GUILayout.Height(28 * scale)))
            {
                _screen = Screen.Login;
                _message = "";
            }
        }

        private void DrawCharacters(float scale)
        {
            GUILayout.Label(Loc.T("login.your_characters", _characters.Count, AccountRules.MaxCharactersPerAccount), _label);
            foreach (var character in _characters)
            {
                var text = Loc.T("login.character_row", character.Name, AccountRules.ClassDisplayName((CharacterClass)character.ClassId), character.Level);
                if (GUILayout.Button(text, GUILayout.Height(44 * scale))) Play(character);
            }
            GUILayout.Space(10 * scale);
            if (_characters.Count < AccountRules.MaxCharactersPerAccount && GUILayout.Button(Loc.T("login.create_character"), GUILayout.Height(30 * scale)))
            {
                _screen = Screen.CreateCharacter;
                _message = "";
            }
            if (GUILayout.Button(Loc.T("login.logout"), GUILayout.Height(28 * scale)))
            {
                _screen = Screen.Login;
                _api = null;
                _message = "";
            }
        }

        private void DrawCreate(float scale)
        {
            GUILayout.Label(Loc.T("login.new_character"), _label);
            GUILayout.Label(Loc.T("login.name_rule", AccountRules.MinCharacterNameLength, AccountRules.MaxCharacterNameLength), _small);
            _newName = GUILayout.TextField(_newName, AccountRules.MaxCharacterNameLength, GUILayout.Height(26 * scale));
            GUILayout.Label(Loc.T("login.class"), _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_newClass == CharacterClass.Blade, " " + Loc.T("login.class_blade"), GUI.skin.button, GUILayout.Height(34 * scale)))
                _newClass = CharacterClass.Blade;
            if (GUILayout.Toggle(_newClass == CharacterClass.Arcanist, " " + Loc.T("login.class_arcanist"), GUI.skin.button, GUILayout.Height(34 * scale)))
                _newClass = CharacterClass.Arcanist;
            GUILayout.EndHorizontal();
            GUILayout.Space(10 * scale);
            if (GUILayout.Button(Loc.T("login.create"), GUILayout.Height(38 * scale))) _ = CreateCharacterAsync();
            if (_characters.Count > 0 && GUILayout.Button(Loc.T("ui.back"), GUILayout.Height(28 * scale)))
            {
                _screen = Screen.Characters;
                _message = "";
            }
        }
    }
}
