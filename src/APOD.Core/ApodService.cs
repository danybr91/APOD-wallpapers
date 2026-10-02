using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace APOD.Core
{
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Los métodos se mantienen como miembros de instancia del servicio.")]
    public class ApodService
    {
        // Desde 2026 apod.nasa.gov redirige todo (páginas e imágenes) a science.nasa.gov/apod/.
        // Raíz del sitio: base de la API y de la resolución de URLs relativas (ResolveURL).
        public const string APOD_URL_BASE = "https://science.nasa.gov/";
        // Portada de APOD (la del día), relativa a APOD_URL_BASE.
        public const string APOD_MAIN_PAGE = "apod/";
        // API REST de WordPress: cada APOD es un "image-article" de la categoría APOD.
        // Se usa para averiguar la URL de la página de una fecha, ya que lleva el título en el slug.
        public const string APOD_API_URL = APOD_URL_BASE + "wp-json/wp/v2/image-article";
        // Id de la categoría APOD en WordPress; filtra los image-article que no son APOD.
        public const int APOD_CATEGORY_ID = 22766;
        // Las imágenes se sirven redimensionadas desde "dynamicimage" (sin soporte de rangos);
        // el original está en "content/dam" con la misma ruta.
        // Prefijo de la ruta de la imagen redimensionada que aparece en el <img> de la página.
        public const string DYNAMIC_IMAGE_PATH = "/dynamicimage/assets/";
        // Prefijo por el que se sustituye DYNAMIC_IMAGE_PATH para descargar la imagen original.
        public const string ORIGINAL_IMAGE_PATH = "/content/dam/";
        // Parámetros de "dynamicimage" para la vista previa: máx. 1600x1600 conservando proporción.
        public const string IMAGE_PREVIEW_QUERY = "?w=1600&h=1600&fit=clip";
        // Bloque de cabecera de la página de una APOD; contiene imagen, título y descripción.
        public const string HERO_SEARCH_XPATH = "//div[contains(@class,'wp-block-nasa-blocks-media-detail-hero')]";
        // Contenedor del medio (imagen o vídeo) dentro de la cabecera.
        public const string MEDIA_SEARCH_XPATH = HERO_SEARCH_XPATH + "//div[contains(@class,'media-detail-hero__media')]";
        // <img> de la APOD. Si no existe, ese día es un vídeo (ver HasImage).
        public const string IMAGE_URL_SEARCH_XPATH = MEDIA_SEARCH_XPATH + "//img[@src]";
        // Título de la APOD: primer h1 o h2 dentro de la cabecera.
        public const string IMAGE_TITLE_SEARCH_XPATH = HERO_SEARCH_XPATH + "//*[self::h1 or self::h2]";
        // Párrafo con la explicación de la APOD.
        public const string IMAGE_DESCRIPTION_SEARCH_XPATH = HERO_SEARCH_XPATH + "//p[contains(@class,'media-detail-hero__description')]";
        // Conexiones simultáneas por defecto en la descarga por rangos (DownloadImageParallelToBytes).
        public const int DEFAULT_PARALLEL_CONNECTIONS = 4;

        // Primera APOD publicada; límite inferior de las fechas válidas.
        public static readonly DateTime APOD_MIN_DATE = new(1995, 6, 16);

        public static string DefaultDownloadDir { get; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        private readonly ILog _log;

        private readonly IOsService _osService;

        public ApodService(ILog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _osService = Util.GetOsService();
        }

        public static bool IsValidURL(string URL)
        {
            return Uri.TryCreate(URL, UriKind.Absolute, out Uri uri_result) && (uri_result.Scheme == Uri.UriSchemeHttp || uri_result.Scheme == Uri.UriSchemeHttps);
        }

        public string GetAPODMainPageURL()
        {
            return APOD_URL_BASE + APOD_MAIN_PAGE;
        }

        /// <summary>
        /// Resuelve la URL de la página de una fecha. Las páginas nuevas llevan el título en la URL
        /// (p. ej. image-article/apod-2026-august-3-...), así que hay que consultarla en la API.
        /// </summary>
        public async Task<string> GetAPODPageURL(HttpClient client, DateTime date, int timeout = 30000, CancellationToken token = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);

            // "after" y "before" son exclusivos en la API de WordPress.
            string url = $"{APOD_API_URL}?categories={APOD_CATEGORY_ID}" +
                         $"&after={date.Date.AddSeconds(-1):yyyy-MM-ddTHH:mm:ss}" +
                         $"&before={date.Date.AddDays(1):yyyy-MM-ddTHH:mm:ss}" +
                         "&_fields=slug,link&per_page=10";

            var response = await client.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));

            string expectedSlug = $"apod-{date:yyyy}-{date.ToString("MMMM", CultureInfo.InvariantCulture).ToLowerInvariant()}-{date.Day}-";
            string fallback = null;
            foreach (var post in json.RootElement.EnumerateArray())
            {
                string link = post.GetProperty("link").GetString();
                if (post.GetProperty("slug").GetString()?.StartsWith(expectedSlug) is true)
                    return link;
                fallback ??= link;
            }

            return fallback ?? throw new NodeNotFoundException($"No hay APOD publicada para el {date:dd/MM/yyyy}.");
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

        public async Task<HtmlDocument> GetHTMLDocument(HttpClient client, string url, int timeout = 30000, CancellationToken token = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);

            var response = await client.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();
            var page_contents = await response.Content.ReadAsStringAsync(cts.Token);
            HtmlDocument page_document = new ();
            page_document.LoadHtml(page_contents);
            return page_document;
        }

        public string GetImageURLFromAPOD(HtmlDocument page_document)
        {
            string src = GetImageSourceFromAPOD(page_document);
            var uri = new Uri(src);
            string path = uri.AbsolutePath;
            if (path.StartsWith(DYNAMIC_IMAGE_PATH))
            {
                path = string.Concat(ORIGINAL_IMAGE_PATH, path.AsSpan(DYNAMIC_IMAGE_PATH.Length));
            }
            return uri.GetLeftPart(UriPartial.Authority) + path;
        }

        public string GetImagePreviewURLFromAPOD(HtmlDocument page_document)
        {
            string src = GetImageSourceFromAPOD(page_document);
            var uri = new Uri(src);
            if (uri.AbsolutePath.StartsWith(DYNAMIC_IMAGE_PATH))
            {
                return uri.GetLeftPart(UriPartial.Path) + IMAGE_PREVIEW_QUERY;
            }
            return src;
        }

        private string GetImageSourceFromAPOD(HtmlDocument page_document)
        {
            var node = page_document.DocumentNode.SelectSingleNode(IMAGE_URL_SEARCH_XPATH);
            if (node != null)
            {
                return ResolveURL(HtmlEntity.DeEntitize(node.GetAttributeValue("src", "")));
            }
            else
            {
                throw new NodeNotFoundException("No se ha encontrado la imagen de hoy en el sitio web.");
            }
        }

        public string ResolveURL(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;
            return Uri.TryCreate(new Uri(APOD_URL_BASE), url, out Uri result) ? result.ToString() : null;
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

        /// <summary>
        /// Indica si la APOD es una imagen. Algunos días es un vídeo (MP4 o YouTube), que la app no muestra.
        /// </summary>
        public bool HasImage(HtmlDocument page_document)
        {
            return page_document.DocumentNode.SelectSingleNode(IMAGE_URL_SEARCH_XPATH) != null;
        }

        public string GetImagefileNameFromURL(string image_url)
        {
            if (Uri.TryCreate(image_url, UriKind.Absolute, out Uri uri))
                image_url = uri.AbsolutePath;
            string[] tokens = image_url.Split("/");
            if (tokens.Length > 0)
            {
                return tokens[^1];
            }
            else
            {
                throw new Exception("No valid URL");
            }
        }

        public async Task<byte[]> DownloadImageToBytes(HttpClient client, string url, int timeout = 30000, CancellationToken token = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);

            var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(cts.Token);
        }

        public async Task<byte[]> DownloadImageParallelToBytes(HttpClient client, string url, int timeout = 30000, int maxConnections = DEFAULT_PARALLEL_CONNECTIONS, CancellationToken token = default)
        {
            long total;
            using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                probeCts.CancelAfter(timeout);

                using var probeRequest = new HttpRequestMessage(HttpMethod.Get, url);
                probeRequest.Headers.Range = new RangeHeaderValue(0, 0);
                using var probeResponse = await client.SendAsync(probeRequest, HttpCompletionOption.ResponseHeadersRead, probeCts.Token);
                probeResponse.EnsureSuccessStatusCode();

                // Sin soporte de rangos (p. ej. imágenes redimensionadas) la respuesta ya trae la imagen completa.
                if (probeResponse.StatusCode != HttpStatusCode.PartialContent)
                {
                    _log.Info($"El servidor no soporta descargas por rango (HTTP {(int)probeResponse.StatusCode}); descarga secuencial.");
                    return await probeResponse.Content.ReadAsByteArrayAsync(probeCts.Token);
                }

                var contentRange = probeResponse.Content.Headers.ContentRange;
                if (contentRange?.Length.HasValue is not true)
                {
                    throw new HttpRequestException("El servidor no indicó el tamaño total de la imagen.");
                }

                total = contentRange.Length.Value;
            }

            string tempPath = Path.GetTempFileName();
            try
            {
                await using (var setupStream = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
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
                    tasks.Add(DownloadRangeToFileAsync(client, url, chunkStart, chunkEnd, tempPath, timeout, cancellation.Token));
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

        private async Task DownloadRangeToFileAsync(HttpClient client, string url, long start, long end, string tempPath, int timeout, CancellationToken token)
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

            await using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
            await using var fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 81920, useAsync: true);
            fileStream.Seek(start, SeekOrigin.Begin);
            await contentStream.CopyToAsync(fileStream, cts.Token);
        }

        public async Task SetWallpaperAsync(string image_path)
        {
            string path = new Uri(image_path).LocalPath;
            await _osService.SetWallpaperAsync(path);
        }
    }
}
