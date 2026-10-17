using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.UI
{
    /// <summary>
    /// Teclado para os comandos do jogo (andar, skills, atalhos das janelas). Enquanto o jogador digita no chat — e no
    /// quadro em que termina de digitar — é nulo, para "w" ou "1" na mensagem não andarem nem soltarem skill.
    /// </summary>
    public static class GameInput
    {
        private static bool _typing;
        private static int _typingEndedFrame = -1;

        public static bool Typing
        {
            get => _typing;
            set
            {
                if (_typing && !value) _typingEndedFrame = Time.frameCount;
                _typing = value;
            }
        }

        public static Keyboard Keyboard => _typing || Time.frameCount <= _typingEndedFrame + 1 ? null : UnityEngine.InputSystem.Keyboard.current;
    }
}
