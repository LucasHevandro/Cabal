using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.Combat
{
    /// <summary>
    /// Seleção de alvo: clique esquerdo no alvo, Tab para o próximo alvo mais perto, Esc para limpar.
    /// </summary>
    public sealed class TargetSelector : MonoBehaviour
    {
        [SerializeField] private float tabRange = 25f;
        [SerializeField] private Camera viewCamera;

        /// <summary>Alvo selecionado (pode ser nulo).</summary>
        public Targetable Current { get; private set; }

        /// <summary>Disparado quando o alvo muda (pode ser nulo).</summary>
        public event Action<Targetable> TargetChanged;

        public void SetCamera(Camera cam) => viewCamera = cam;

        private void Awake()
        {
            if (viewCamera == null) viewCamera = Camera.main;
        }

        private void Update()
        {
            var keyboard = UI.GameInput.Keyboard;
            var mouse = Mouse.current;

            if (UI.KeyBindings.Pressed(UI.GameAction.NextTarget)) SelectNext();
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !UI.OptionsWindow.EscapeTaken) Select(null);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) SelectUnderMouse(mouse);

            if (Current != null && (!Current.isActiveAndEnabled || !Current.IsAlive)) Select(null);
        }

        public void Select(Targetable target)
        {
            if (target == Current) return;
            if (Current != null) Current.SetSelected(false);
            Current = target;
            if (Current != null) Current.SetSelected(true);
            TargetChanged?.Invoke(Current);
        }

        /// <summary>
        /// Percorre os alvos vivos no alcance, do mais perto para o mais longe; volta ao primeiro depois do último.
        /// </summary>
        public void SelectNext()
        {
            _candidates.Clear();
            foreach (var candidate in Targetable.All)
            {
                if (!candidate.IsAlive || !candidate.IsSelectable) continue;
                if (Vector3.Distance(transform.position, candidate.transform.position) <= tabRange) _candidates.Add(candidate);
            }
            if (_candidates.Count == 0) return;

            var origin = transform.position;
            _candidates.Sort((a, b) =>
                Vector3.Distance(origin, a.transform.position).CompareTo(Vector3.Distance(origin, b.transform.position)));
            var index = Current != null ? _candidates.IndexOf(Current) : -1;
            Select(_candidates[(index + 1) % _candidates.Count]);
        }

        private readonly System.Collections.Generic.List<Targetable> _candidates = new System.Collections.Generic.List<Targetable>();

        private void SelectUnderMouse(Mouse mouse)
        {
            if (viewCamera == null || UI.GameWindows.PointerOverUi) return;
            var ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out var hit, 200f, ~0, QueryTriggerInteraction.Collide))
            {
                var target = hit.collider.GetComponentInParent<Targetable>();
                if (target != null && target.IsSelectable) Select(target);
            }
        }
    }
}
