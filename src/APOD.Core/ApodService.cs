using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace APOD.Core
{
    public class ApodService
    {
        public const string APOD_URL_BASE = "https://apod.nasa.gov/apod/";
        public const string APOD_MAIN_PAGE = "astropix.html";
        public const string IMAGE_URL_SEARCH_XPATH = "//center//a[starts-with(@href,'image')]";
        public const string IMAGE_PREVIEW_URL_SEARCH_XPATH = "//center//a[starts-with(@href,'image')]/img";
        public const string IMAGE_TITLE_SEARCH_XPATH = "//center[2]";
        public const string IMAGE_DESCRIPTION_SEARCH_XPATH = "//body/p[1]";
        public const string VIDEO_SEARCH_XPATH = "//center//video";
        public const int DEFAULT_PARALLEL_CONNECTIONS = 4;
        
        public static readonly DateTime APOD_MIN_DATE = new DateTime(1995, 6, 16);
        
        public static string DefaultDownloadDir { get; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        private readonly ILog _log;

        public ApodService(ILog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }
        
        #region P/Invoke declarations
        
        [DllImport("User32", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int uiAction, int uiParam, string pvParam, uint fWinIni);
        
        #endregion

        #region Public API

        public bool IsValidURL(string URL)
        {
            Uri uri_result;
            return Uri.TryCreate(URL, UriKind.Absolute, out uri_result) && ( uri_result.Scheme == Uri.UriSchemeHttp || uri_result.Scheme == Uri.UriSchemeHttps);
        }

        public string GetAPODPageURL(DateTime date)
        {
            return $"{APOD_URL_BASE}ap{date:yyMMdd}.html";
        }

        public bool IsValidAPODDate(DateTime date)
        {
            return date >= APOD_MIN_DATE && date <= DateTime.Today;
        }

        public DateTime? ParseAPODDate(string value)
        {
            if (DateTime.TryParseExact(value, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                return date;
            }
            return null;
        }

        public bool IsValidFilePath(string file_path)
        {
            if (string.IsNullOrWhiteSpace(file_path))
                return false;

            try
            {
                string fullPath = Path.GetFullPath(file_path);
                return fullPath.IndexOfAny(Path.GetInvalidPathChars()) == -1;
            }
            catch
            {
                return false;
            }
        }

        public bool IsImageFile(string file_path)
        {
            string extension = Path.GetExtension(file_path).ToLower();
            return extension.Equals(".bmp") ||
                   extension.Equals(".jpg") ||
                   extension.Equals(".jpeg") ||
                   extension.Equals(".gif") ||
                   extension.Equals(".png");
        }

        public bool CheckFileAccess(string file_name, FileMode open_mode, FileAccess access_mode)
        {
            try
            {
                File.Open(file_name, open_mode, access_mode).Dispose();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<HtmlDocument> GetHTMLDocument(HttpClient client, string url, CancellationToken token = default, int timeout = 30000)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);
            
            var response = await client.GetAsync(url, cts.Token);
            var page_contents = await response.Content.ReadAsStringAsync();
            HtmlDocument page_document = new HtmlDocument();
            page_document.LoadHtml(page_contents);
            return page_document;
        }

        public string GetImageURLFromAPOD(HtmlDocument page_document)
        {
            var node = page_document.DocumentNode.SelectSingleNode(IMAGE_URL_SEARCH_XPATH);
            if (node != null)
            {
                return node.GetAttributeValue("href", "");
            }
            else
            {
                throw new NodeNotFoundException("No se ha encontrado la imagen de hoy en el sitio web.");
            }
        }

        public string GetImagePreviewURLFromAPOD(HtmlDocument page_document)
        {
            var node = page_document.DocumentNode.SelectSingleNode(IMAGE_PREVIEW_URL_SEARCH_XPATH);
            if (node != null)
            {
                return node.GetAttributeValue("src", "");
            }
            else
            {
                throw new NodeNotFoundException("No se ha encontrado la imagen de vista previa en el sitio web.");
            }
        }

        public HtmlNode GetImageTitleFromAPOD(HtmlDocument page_document)
        {
            var node = page_document.DocumentNode.SelectSingleNode(IMAGE_TITLE_SEARCH_XPATH);
            if (node != null)
            {
                return node;
            }
            else
            {
                throw new NodeNotFoundException("Fallo al extraer el título de la imagen de hoy en el sitio web.");
            }
        }
        
        public HtmlNode GetImageDescriptionFromAPOD(HtmlDocument page_document)
        {
            var node = page_document.DocumentNode.SelectSingleNode(IMAGE_DESCRIPTION_SEARCH_XPATH);
            if (node != null)
            {
                return node;
            }
            else
            {
                throw new NodeNotFoundException("Fallo al extraer la descripción de la imagen de hoy en el sitio web.");
            }
        }

        public bool HasVideo(HtmlDocument page_document)
        {
            return page_document.DocumentNode.SelectSingleNode(VIDEO_SEARCH_XPATH) != null;
        }

        public string GetVideoUrl(HtmlDocument page_document)
        {
            var node = page_document.DocumentNode.SelectSingleNode(VIDEO_SEARCH_XPATH);
            if (node != null)
            {
                var source = node.SelectSingleNode("source[@src]");
                if (source != null)
                    return APOD_URL_BASE + source.GetAttributeValue("src", "");

                string src = node.GetAttributeValue("src", "");
                if (!string.IsNullOrEmpty(src))
                    return APOD_URL_BASE + src;
            }
            return null;
        }

        public string GetVideoThumbnailUrl(HtmlDocument page_document)
        {
            var node = page_document.DocumentNode.SelectSingleNode(VIDEO_SEARCH_XPATH);
            if (node != null)
            {
                string poster = node.GetAttributeValue("poster", "");
                if (!string.IsNullOrEmpty(poster))
                {
                    if (poster.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        return poster;
                    return APOD_URL_BASE + poster;
                }
            }
            return null;
        }

        public string GetImagefileNameFromURL(string image_url)
        {
            string[] tokens = image_url.ToString().Split("/");
            if (tokens.Length > 0)
            {
                return tokens[tokens.Length - 1];
            }
            else
            {
                throw new Exception("No valid URL");
            }
        }

        public async Task<byte[]> DownloadImageToBytes(HttpClient client, string url, CancellationToken token = default, int timeout = 30000)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);

            var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(cts.Token);
        }

        public async Task<byte[]> DownloadImageParallelToBytes(HttpClient client, string url, CancellationToken token = default, int timeout = 30000, int maxConnections = DEFAULT_PARALLEL_CONNECTIONS)
        {
            long total;
            using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                probeCts.CancelAfter(timeout);

                using (var probeRequest = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    probeRequest.Headers.Range = new RangeHeaderValue(0, 0);
                    using var probeResponse = await client.SendAsync(probeRequest, HttpCompletionOption.ResponseHeadersRead, probeCts.Token);
                    probeResponse.EnsureSuccessStatusCode();

                    if (probeResponse.StatusCode != HttpStatusCode.PartialContent)
                    {
                        throw new HttpRequestException($"El servidor no soporta descargas por rango (HTTP {(int)probeResponse.StatusCode} en vez de 206).");
                    }

                    var contentRange = probeResponse.Content.Headers.ContentRange;
                    if (contentRange == null || !contentRange.Length.HasValue)
                    {
                        throw new HttpRequestException("El servidor no indicó el tamaño total de la imagen.");
                    }

                    total = contentRange.Length.Value;
                }
            }

            string tempPath = Path.GetTempFileName();
            try
            {
                using (var setupStream = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                {
                    setupStream.SetLength(total);
                }

                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                var tasks = new List<Task>();
                long chunkSize = (long)Math.Ceiling(total / (double)maxConnections);
                for (long start = 0; start < total; start += chunkSize)
                {
                    long chunkStart = start;
                    long chunkEnd = Math.Min(start + chunkSize - 1, total - 1);
                    tasks.Add(DownloadRangeToFileAsync(client, url, chunkStart, chunkEnd, tempPath, cancellation.Token, timeout));
                }

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch
                {
                    cancellation.Cancel();
                    throw;
                }

                return await File.ReadAllBytesAsync(tempPath, token);
            }
            finally
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private async Task DownloadRangeToFileAsync(HttpClient client, string url, long start, long end, string tempPath, CancellationToken token, int timeout)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(start, end);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                throw new HttpRequestException($"El servidor no devolvió un rango (206) para el rango {start}-{end}.");
            }

            using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 81920, useAsync: true);
            fileStream.Seek(start, SeekOrigin.Begin);
            await contentStream.CopyToAsync(fileStream, cts.Token);
        }

        public void SetWallpaper(string image_path)
        {
            string path = new Uri(image_path).LocalPath;

            if (Util.IsWindows())
            {
                SystemParametersInfo(0x0014, 0, path, 0x0001);
            }
            else if (Util.IsOsx())
            {
                // Forma de System Events (la de Finder quedó obsoleta): compatible con macOS moderno
                // y sin depender de Xcode Command Line Tools. Requiere permiso de Automatización (una vez).
                var psi = new ProcessStartInfo("osascript")
                {
                    UseShellExecute = false
                };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add($"tell application \"System Events\" to tell every desktop to set picture to \"{path}\"");
                var process = Process.Start(psi);
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        "No se pudo establecer el fondo de pantalla. Verifica el permiso de Automatización (Ajustes del Sistema > Privacidad y seguridad > Automatización).");
                }
            }
            else if (Util.IsLinux())
            {
                string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.ToLower() ?? "";

                if (desktop.Contains("gnome"))
                    Process.Start("gsettings", $"set org.gnome.desktop.background picture-uri file://{path}");
                else if (desktop.Contains("kde"))
                    Process.Start("plasma-apply-wallpaperimage", path);
                else if (desktop.Contains("xfce"))
                    Process.Start("xfconf-query", $"-c xfce4-desktop -p /backdrop/screen0/monitor0/workspace0/last-image -s {path}");
                else if (desktop.Contains("sway"))
                    Process.Start("swaymsg", $"output * bg {path} fill");
                else
                    throw new PlatformNotSupportedException($"Escritorio '{desktop}' no soportado.");
            }
            else
            {
                throw new PlatformNotSupportedException("Sistema operativo no soportado para cambiar el fondo de pantalla.");
            }
        }
        
        #endregion
    }
}
