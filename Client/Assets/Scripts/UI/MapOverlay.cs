using Game.Client.Combat;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Desenha o mapa grande por cima de todas as janelas do jogo (o minimapa pequeno fica no HUD). Criado sozinho ao
    /// abrir o jogo, como a janela de opções.
    /// </summary>
    public sealed class MapOverlay : MonoBehaviour
    {
        private const float VirtualWidth = 1920f;
        private const float VirtualHeight = 1080f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("Mapa");
            DontDestroyOnLoad(go);
            go.AddComponent<MapOverlay>();
        }

        private void OnGUI()
        {
            var player = PlayerCombat.Local;
            if (!Minimap.BigOpen || player == null || OptionsWindow.IsOpen) return;
            GUI.depth = -20; // por cima das janelas do jogo, por baixo das opções
            var scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            GUI.matrix = Matrix4x4.TRS(
                new Vector3((Screen.width - VirtualWidth * scale) / 2, (Screen.height - VirtualHeight * scale) / 2, 0),
                Quaternion.identity, new Vector3(scale, scale, 1));
            Minimap.DrawBig(player.transform, player.Target, VirtualWidth, VirtualHeight);
            GUI.matrix = Matrix4x4.identity;
        }
    }
}
