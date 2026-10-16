using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Client.Net
{
    /// <summary>
    /// Menu inicial: jogar offline ou conectar a um servidor. Ao escolher online, recarrega a cena já no modo
    /// online (spawners locais desligados, mundo vindo do servidor).
    /// </summary>
    public sealed class ConnectMenu : MonoBehaviour
    {
        private string _address = "127.0.0.1:7777";
        private string _name = "Aventureiro";
        private string _error = "";
        private GUIStyle _title;
        private GUIStyle _label;

        public static bool IsOpen => !GameMode.ChoiceMade;

        private void Awake()
        {
            if (GameMode.ChoiceMade) enabled = false;
            _address = $"{GameMode.Host}:{GameMode.Port}";
            _name = GameMode.PlayerName;
        }

        private void OnGUI()
        {
            if (GameMode.ChoiceMade) return;
            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 18 };

            var scale = Mathf.Max(1f, Screen.height / 900f);
            var width = 460 * scale;
            var height = 360 * scale;
            var rect = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Space(10 * scale);
            GUILayout.Label("Action MMORPG — protótipo", _title);
            GUILayout.Space(20 * scale);

            GUILayout.Label("Seu nome", _label);
            _name = GUILayout.TextField(_name, 20, GUILayout.Height(28 * scale));
            GUILayout.Label("Servidor (endereço:porta)", _label);
            _address = GUILayout.TextField(_address, 60, GUILayout.Height(28 * scale));
            GUILayout.Space(14 * scale);

            if (GUILayout.Button("Conectar", GUILayout.Height(40 * scale)))
            {
                if (GameMode.TryParseAddress(_address, out var host, out var port))
                {
                    GameMode.SetOnline(host, port, _name);
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                }
                else
                {
                    _error = "Endereço inválido. Exemplo: 127.0.0.1:7777";
                }
            }
            if (GUILayout.Button("Jogar offline", GUILayout.Height(34 * scale))) GameMode.SetOffline();

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = new Color(1f, 0.5f, 0.5f);
                GUILayout.Label(_error, _label);
                GUI.color = Color.white;
            }
            GUILayout.EndArea();
        }
    }
}
