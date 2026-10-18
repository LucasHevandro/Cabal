using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>Sons da interface: abrir e fechar janelas e clicar nelas.</summary>
    public static class UiSounds
    {
        private static int _clickFrame = -1;

        /// <summary>Janela abriu (true) ou fechou (false).</summary>
        public static void Toggle(bool opened) => SoundPlayer.Play(opened ? SoundId.UiOpen : SoundId.UiClose, 0.5f, 0.03f);

        /// <summary>Clique numa janela (uma vez por quadro, mesmo com várias janelas sob o mouse).</summary>
        public static void Click()
        {
            if (_clickFrame == Time.frameCount) return;
            _clickFrame = Time.frameCount;
            SoundPlayer.Play(SoundId.UiClick, 0.45f, 0.05f);
        }

        /// <summary>
        /// Chamado no OnGUI antes da escala: se o botão do mouse desceu sobre uma das janelas (tela virtual), toca o clique.
        /// </summary>
        public static void ClickIn(IReadOnlyList<Rect> virtualRects, float scale, Vector2 offset)
        {
            var e = Event.current;
            if (e == null || e.type != EventType.MouseDown || scale <= 0) return;
            var point = new Vector2((e.mousePosition.x - offset.x) / scale, (e.mousePosition.y - offset.y) / scale);
            foreach (var rect in virtualRects)
            {
                if (!rect.Contains(point)) continue;
                Click();
                return;
            }
        }
    }
}
