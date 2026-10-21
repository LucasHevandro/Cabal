using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Game.Launcher
{
    /// <summary>
    /// Monta uma versão para publicar: o <c>versao.json</c> e a pasta <c>arquivos/</c> com cada arquivo do build
    /// compactado e nomeado pelo SHA-256 (o que já existe de versões anteriores não é refeito nem enviado de novo).
    /// O próprio launcher entra na versão, para se atualizar sozinho.
    /// </summary>
    public static class Publisher
    {
        private static readonly string[] SkippedNames =
        {
            Release.InstalledFileName, Release.FileName, "launcher.log", "unins000.exe", "unins000.dat",
        };

        public static Release Build(string gameFolder, string output, string version, string notes)
        {
            gameFolder = Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var blobs = Path.Combine(output, "arquivos");
            Directory.CreateDirectory(blobs);
            var release = new Release { Version = version, Notes = notes };

            var files = Directory.GetFiles(gameFolder, "*", SearchOption.AllDirectories)
                .Select(full => (Full: full, Relative: full.Substring(gameFolder.Length).Replace(Path.DirectorySeparatorChar, '/')))
                .Where(f => !Skipped(f.Relative))
                .OrderBy(f => f.Relative, StringComparer.Ordinal)
                .ToList();
            var self = typeof(Publisher).Assembly.Location;
            files.RemoveAll(f => f.Relative.Equals(Updater.LauncherFile, StringComparison.OrdinalIgnoreCase));
            files.Add((self, Updater.LauncherFile));

            foreach (var (full, relative) in files)
            {
                var sha = Updater.Hash(full);
                var blob = Path.Combine(blobs, sha + ".gz");
                if (!File.Exists(blob))
                {
                    var temp = blob + ".tmp";
                    using (var input = File.OpenRead(full))
                    using (var target = File.Create(temp))
                    using (var gzip = new GZipStream(target, CompressionLevel.Optimal))
                        input.CopyTo(gzip);
                    File.Move(temp, blob);
                }
                release.Files.Add(new ReleaseFile
                {
                    Path = relative,
                    Size = new FileInfo(full).Length,
                    Sha256 = sha,
                    Packed = new FileInfo(blob).Length,
                });
            }
            release.Save(Path.Combine(output, Release.FileName));
            return release;
        }

        private static bool Skipped(string relative)
        {
            var name = relative.Split('/').Last();
            if (SkippedNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
            if (name.EndsWith(".baixando") || name.EndsWith(".antigo")) return true;
            // Pastas que o Unity gera para depuração e não vão para o jogador.
            return relative.Split('/').Any(part => part.Contains("_BackUpThisFolder_") || part.Contains("DoNotShip"));
        }
    }
}
