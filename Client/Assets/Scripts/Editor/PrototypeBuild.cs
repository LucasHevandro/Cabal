using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Gera o executável Windows do protótipo em Client/Builds/Windows.
    /// No Editor: menu Protótipo &gt; Gerar build Windows.
    /// Em linha de comando: -executeMethod Game.Client.Editor.PrototypeBuild.BuildWindows
    /// </summary>
    public static class PrototypeBuild
    {
        private const string OutputPath = "Builds/Windows/Prototipo.exe";

        [MenuItem("Protótipo/Gerar build Windows")]
        public static void BuildWindows()
        {
            // Jogo online: continua rodando sem foco (várias janelas abertas para teste, alt-tab durante a luta).
            PlayerSettings.runInBackground = true;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Treino.unity" },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[Protótipo] Build {summary.result}: {summary.outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} erros)");

            if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
