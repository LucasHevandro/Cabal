using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.Client.Fx;
using Game.Client.Net;
using Game.Shared.Items;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Vitrine do equipamento: <c>-geartest pasta</c> mostra as duas classes com cada nível de arma em +0, +4, +7, +9 e
    /// +10 (o brilho da melhoria) e depois todos os itens como aparecem no chão. Tira as capturas e fecha.
    /// </summary>
    public sealed class GearShowcaseTest : MonoBehaviour
    {
        private string _folder = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-geartest");
            if (index < 0) return;
            GameMode.SetOffline();
            var test = new GameObject("Vitrine do equipamento").AddComponent<GearShowcaseTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "geartest");
            Directory.CreateDirectory(test._folder);
        }

        private IEnumerator Start()
        {
            yield return null;
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                var ns = behaviour.GetType().Namespace ?? "";
                if (behaviour != this && (ns.StartsWith("Game.Client.UI") || ns.StartsWith("Game.Client.Combat") || ns.StartsWith("Game.Client.Player")))
                    behaviour.enabled = false;
            }
            if (Camera.main != null) Camera.main.gameObject.SetActive(false);
            var camera = new GameObject("Câmera da vitrine").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 34;
            var origin = World.Ground.Point(0, 14);

            // 1) Armas por nível e melhoria.
            var weapons = new[] { "espada_ferro", "espada_aco_negro", "espada_astralita", "cajado_ferro", "cajado_aco_negro", "cajado_astralita" };
            var holders = new GameObject[weapons.Length];
            for (var i = 0; i < weapons.Length; i++)
            {
                holders[i] = new GameObject(weapons[i]);
                holders[i].transform.position = World.Ground.Point(origin.x + (i - 2.5f) * 1.6f, origin.z);
                holders[i].transform.rotation = Quaternion.Euler(0, 200, 0);
                VisualCatalog.Spawn(VisualCatalog.Class, weapons[i].StartsWith("cajado") ? "arcanist" : "blade", holders[i].transform);
            }
            camera.transform.position = origin + new Vector3(0, 1.8f, -8.5f);
            camera.transform.LookAt(origin + Vector3.up * 1f);
            foreach (var upgrade in new[] { 0, 4, 7, 9, 10 })
            {
                for (var i = 0; i < weapons.Length; i++) WeaponFx.SetLook(holders[i].transform, weapons[i], upgrade);
                yield return new WaitForSeconds(0.8f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_folder, $"armas_+{upgrade}.png"));
                yield return new WaitForSeconds(0.3f);
            }
            foreach (var holder in holders) Destroy(holder);

            // 1b) Conjunto por grau (sem, Ferro, Aço Negro, Astralita), peças misturadas e algumas cores de cabelo e roupa.
            var looks = new[]
            {
                (CharacterPaint.Gear.Set(0), 0, 0), (CharacterPaint.Gear.Set(1), 0, 0), (CharacterPaint.Gear.Set(2), 0, 0),
                (CharacterPaint.Gear.Set(3), 0, 0), (new CharacterPaint.Gear(3, 2, 1), 1, 1), (new CharacterPaint.Gear(2, 3, 0), 3, 4),
                (new CharacterPaint.Gear(0, 3, 3), 2, 5), (new CharacterPaint.Gear(1, 0, 2), 5, 2),
            };
            var dressed = new GameObject[looks.Length * 2];
            for (var i = 0; i < dressed.Length; i++)
            {
                var (gear, hair, cloth) = looks[i % looks.Length];
                dressed[i] = new GameObject($"aparencia_{i}");
                dressed[i].transform.position = World.Ground.Point(origin.x + (i % looks.Length - 3.5f) * 1.25f, origin.z + (i < looks.Length ? 0f : 2.2f));
                dressed[i].transform.rotation = Quaternion.Euler(0, 180, 0);
                var model = VisualCatalog.Spawn(VisualCatalog.Class, i < looks.Length ? "blade" : "arcanist", dressed[i].transform);
                if (model != null) CharacterPaint.Apply(model.transform, gear, hair, cloth);
            }
            camera.transform.position = origin + new Vector3(0, 2.6f, -9.5f);
            camera.transform.LookAt(origin + new Vector3(0, 0.9f, 1.1f));
            yield return new WaitForSeconds(0.8f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "aparencia.png"));
            yield return new WaitForSeconds(0.3f);
            foreach (var holder in dressed) Destroy(holder);

            // 1c) Os seis penteados sem elmo, cada um com um tom de pele (em cima Lâmina, embaixo Arcanista), de perto.
            var heads = new GameObject[CharacterPaint.HeadModels.Length * 2];
            for (var i = 0; i < heads.Length; i++)
            {
                var head = i % CharacterPaint.HeadModels.Length;
                heads[i] = new GameObject($"penteado_{i}");
                heads[i].transform.position = World.Ground.Point(origin.x + (head - 2.5f) * 1.1f, origin.z + (i < CharacterPaint.HeadModels.Length ? 0f : 2f));
                heads[i].transform.rotation = Quaternion.Euler(0, 180, 0);
                var model = VisualCatalog.Spawn(VisualCatalog.Class, i < CharacterPaint.HeadModels.Length ? "blade" : "arcanist", heads[i].transform);
                if (model != null)
                    CharacterPaint.Apply(model.transform, CharacterPaint.Gear.Set(1), new CharacterPaint.Style(head % 6, 0, head, head % CharacterPaint.SkinColors.Length, true));
            }
            camera.transform.position = origin + new Vector3(0, 2.3f, -6.5f);
            camera.transform.LookAt(origin + new Vector3(0, 1.1f, 1f));
            yield return new WaitForSeconds(0.8f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "penteados.png"));
            yield return new WaitForSeconds(0.3f);
            foreach (var holder in heads) Destroy(holder);

            // 2) Todos os itens no chão, em grade.
            var items = ItemCatalog.All.Select(i => i.Id).Where(id => VisualCatalog.Has(VisualCatalog.Item, id)).ToList();
            var columns = 8;
            for (var i = 0; i < items.Count; i++)
            {
                var row = i / columns;
                var col = i % columns;
                var spot = new GameObject(items[i]).transform;
                spot.position = World.Ground.Point(origin.x + (col - (columns - 1) / 2f) * 1.1f, origin.z + row * 1.2f) + Vector3.up * 0.4f;
                VisualCatalog.Spawn(VisualCatalog.Item, items[i], spot);
            }
            camera.transform.position = origin + new Vector3(0, 5.5f, -5.5f);
            camera.transform.LookAt(origin + new Vector3(0, 0.3f, 2.2f));
            yield return new WaitForSeconds(0.8f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "itens.png"));
            yield return new WaitForSeconds(0.4f);
            File.WriteAllText(Path.Combine(_folder, "relatorio.txt"), $"SUCESSO: {items.Count} itens com modelo de {ItemCatalog.All.Count()}\n");
            Application.Quit();
        }
    }
}
