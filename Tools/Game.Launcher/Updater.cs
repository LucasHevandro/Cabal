using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Game.Launcher
{
    /// <summary>
    /// Deixa a pasta do jogo igual à versão publicada: confere cada arquivo pelo SHA-256, baixa só os diferentes (o
    /// <c>.gz</c> é conferido de novo depois de descompactado), apaga os que saíram da versão e troca o próprio launcher
    /// quando ele mudou (o exe em uso pode ser renomeado, não sobrescrito).
    /// </summary>
    public sealed class Updater
    {
        public const string DefaultUrl = "https://teradestralis.duckdns.org/download/";
        public const string LauncherFile = "TerrasDeAstralisLauncher.exe";

        private readonly HttpClient _http;

        public Updater(string folder, string url)
        {
            Folder = folder;
            Url = url.EndsWith("/") ? url : url + "/";
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            {
                Timeout = TimeSpan.FromMinutes(20),
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("TerrasDeAstralisLauncher/1");
        }

        public string Folder { get; }
        public string Url { get; }

        /// <summary>A versão que está na pasta (a última que o launcher terminou de instalar).</summary>
        public Release? Installed => Release.Load(Path.Combine(Folder, Release.InstalledFileName));

        public string GamePath(Release release) => Path.Combine(Folder, release.Game);

        public async Task<Release> FetchAsync()
        {
            // O "?" no fim fura caches no caminho: o versao.json muda a cada publicação.
            using var response = await _http.GetAsync(Url + Release.FileName + "?t=" + DateTime.UtcNow.Ticks).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return Release.Parse(stream);
        }

        /// <summary>Arquivos que precisam ser baixados (mesma versão e tamanhos iguais: confia sem calcular o hash).</summary>
        public List<ReleaseFile> Missing(Release release, IProgress<Report>? progress = null)
        {
            var installed = Installed;
            var trusted = installed != null && installed.Version == release.Version;
            var missing = new List<ReleaseFile>();
            var index = 0;
            foreach (var file in release.Files)
            {
                index++;
                var local = file.LocalPath(Folder);
                var info = new FileInfo(local);
                if (!info.Exists || info.Length != file.Size) missing.Add(file);
                else if (!trusted)
                {
                    progress?.Report(new Report($"Conferindo os arquivos ({index}/{release.Files.Count})...", index, release.Files.Count));
                    if (!string.Equals(Hash(local), file.Sha256, StringComparison.OrdinalIgnoreCase)) missing.Add(file);
                }
            }
            return missing;
        }

        /// <summary>
        /// Baixa e troca os arquivos. Devolve verdadeiro se o launcher se trocou (aí ele precisa abrir de novo).
        /// </summary>
        public async Task<bool> ApplyAsync(Release release, List<ReleaseFile> missing, IProgress<Report>? progress = null)
        {
            var total = Math.Max(1L, missing.Sum(f => f.Packed));
            var done = 0L;
            var selfReplaced = false;
            foreach (var file in missing)
            {
                var local = file.LocalPath(Folder);
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                var temp = local + ".baixando";
                var start = done;
                using (var response = await _http.GetAsync(Url + "arquivos/" + file.Sha256 + ".gz", HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using var packed = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var counted = new CountingStream(packed, read =>
                    {
                        done = start + read;
                        progress?.Report(new Report($"Baixando {file.Path} ({Megabytes(done)} de {Megabytes(total)})", done, total));
                    });
                    using var unpacked = new GZipStream(counted, CompressionMode.Decompress);
                    using var output = File.Create(temp);
                    await unpacked.CopyToAsync(output, 1 << 16).ConfigureAwait(false);
                }
                done = start + file.Packed;
                if (!string.Equals(Hash(temp), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(temp);
                    throw new InvalidDataException($"O arquivo {file.Path} chegou estragado. Tente de novo.");
                }

                if (string.Equals(file.Path, LauncherFile, StringComparison.OrdinalIgnoreCase))
                {
                    var old = local + ".antigo";
                    if (File.Exists(old)) File.Delete(old);
                    if (File.Exists(local)) File.Move(local, old);
                    selfReplaced = true;
                }
                else if (File.Exists(local)) File.Delete(local);
                File.Move(temp, local);
            }

            // Arquivos da versão anterior que saíram desta.
            var keep = new HashSet<string>(release.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var old in Installed?.Files ?? new List<ReleaseFile>())
            {
                if (keep.Contains(old.Path)) continue;
                var local = old.LocalPath(Folder);
                try { if (File.Exists(local)) File.Delete(local); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            release.Save(Path.Combine(Folder, Release.InstalledFileName));
            progress?.Report(new Report("Pronto para jogar.", 1, 1));
            return selfReplaced;
        }

        /// <summary>Apaga o launcher antigo deixado pela troca anterior.</summary>
        public void CleanUp()
        {
            var old = Path.Combine(Folder, LauncherFile + ".antigo");
            try { if (File.Exists(old)) File.Delete(old); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>O jogo está aberto (os arquivos dele ficam travados até fechar).</summary>
        public static bool GameRunning(Release release) =>
            Process.GetProcessesByName(Path.GetFileNameWithoutExtension(release.Game)).Length > 0;

        public static string Hash(string path)
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }

        public static string Megabytes(long bytes) => $"{bytes / (1024.0 * 1024.0):0.0} MB";

        /// <summary>O endereço da atualização: <c>atualizacao = URL</c> no servidor.txt, se houver.</summary>
        public static string UrlFrom(string folder)
        {
            var file = Path.Combine(folder, "servidor.txt");
            if (!File.Exists(file)) return DefaultUrl;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith("#")) continue;
                var equals = line.IndexOf('=');
                if (equals > 0 && line.Substring(0, equals).Trim().Equals("atualizacao", StringComparison.OrdinalIgnoreCase))
                    return line.Substring(equals + 1).Trim();
            }
            return DefaultUrl;
        }

        /// <summary>O primeiro servidor da lista do servidor.txt (para mostrar se está no ar).</summary>
        public static string? HealthUrlFrom(string folder)
        {
            var file = Path.Combine(folder, "servidor.txt");
            if (!File.Exists(file)) return null;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith("#")) continue;
                var equals = line.IndexOf('=');
                if (equals <= 0) continue;
                var key = line.Substring(0, equals).Trim().ToLowerInvariant();
                if (key != "servidor" && key != "jogo") continue;
                var value = line.Substring(equals + 1);
                var address = (value.Contains("|") ? value.Split('|')[1] : value).Trim();
                var host = address.Split(':')[0];
                if (host.Length == 0) continue;
                // Domínio: a API fica no HTTPS do Caddy; IP (teste, Tailscale): direto na 7778.
                return IPAddress.TryParse(host, out _) ? $"http://{host}:7778/api/health" : $"https://{host}/api/health";
            }
            return null;
        }

        public async Task<string?> GetTextAsync(string url)
        {
            try
            {
                using var response = await _http.GetAsync(url).ConfigureAwait(false);
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException)
            {
                return null;
            }
        }

        private sealed class CountingStream : Stream
        {
            private readonly Stream _inner;
            private readonly Action<long> _onRead;
            private long _read;

            public CountingStream(Stream inner, Action<long> onRead)
            {
                _inner = inner;
                _onRead = onRead;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var n = _inner.Read(buffer, offset, count);
                _read += n;
                _onRead(_read);
                return n;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => _read; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    public readonly struct Report
    {
        public Report(string text, long done, long total)
        {
            Text = text;
            Done = done;
            Total = total;
        }

        public string Text { get; }
        public long Done { get; }
        public long Total { get; }
    }
}
