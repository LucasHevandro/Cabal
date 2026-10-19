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
