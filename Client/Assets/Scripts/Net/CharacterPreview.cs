using Game.Client.Fx;
using Game.Shared.Accounts;
using UnityEngine;

namespace Game.Client.Net
{
    /// <summary>
    /// Prévia do personagem (criação e seleção): o modelo da classe girando devagar, com as cores e a armadura, filmado
    /// por uma câmera própria para uma textura que a tela desenha. Fica no alto, longe do mundo (só o céu atrás).
    /// </summary>
    public sealed class CharacterPreview
    {
        private static readonly Vector3 Spot = new Vector3(0f, 400f, 0f);

        private GameObject _rig;
        private Camera _camera;
        private RenderTexture _texture;
        private GameObject _model;
        private CharacterClass _class;
        private int _hair = -1;
        private int _cloth = -1;
        private CharacterPaint.Gear _gear;
        private bool _painted;

        public Texture Texture => _texture;

        public void Show(CharacterClass characterClass, int hair, int cloth, CharacterPaint.Gear gear = default)
        {
            if (_rig == null)
            {
                _rig = new GameObject("Prévia do personagem");
                _rig.transform.position = Spot;
                _texture = new RenderTexture(360, 440, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Prévia do personagem" };
                _camera = new GameObject("Câmera da prévia").AddComponent<Camera>();
                _camera.transform.SetParent(_rig.transform, false);
                _camera.transform.localPosition = new Vector3(0f, 1.15f, 3.4f);
                _camera.transform.LookAt(Spot + Vector3.up * 0.85f);
                _camera.fieldOfView = 30f;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.07f, 0.08f, 0.1f, 1f);
                _camera.targetTexture = _texture;
                _camera.depth = -10;
                _model = null;
                _hair = _cloth = -1;
            }
            if (_model == null || characterClass != _class)
            {
                if (_model != null) Object.Destroy(_model);
                _class = characterClass;
                _model = VisualCatalog.Spawn(VisualCatalog.Class, characterClass == CharacterClass.Arcanist ? "arcanist" : "blade", _rig.transform);
                _hair = _cloth = -1;
            }
            if (_model == null) return;
            if (hair != _hair || cloth != _cloth || !gear.Equals(_gear))
            {
                _hair = hair;
                _cloth = cloth;
                _gear = gear;
                CharacterPaint.Apply(_model.transform, gear, hair, cloth);
            }
            _model.transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.unscaledTime * 0.6f) * 50f, 0f);
        }

        public void Hide()
        {
            if (_rig == null) return;
            Object.Destroy(_rig);
            if (_texture != null) _texture.Release();
            Object.Destroy(_texture);
            _rig = null;
            _model = null;
            _texture = null;
        }
    }
}
