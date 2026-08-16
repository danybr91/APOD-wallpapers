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
using Avalonia;
using Avalonia.Media.Imaging;
using HtmlAgilityPack;

namespace APOD_wallpapers
{
    class Program
    {
        public const string APOD_URL_BASE = "https://apod.nasa.gov/apod/";
        public const string APOD_MAIN_PAGE = "astropix.html";
        public const string IMAGE_URL_SEARCH_XPATH = "//center//a[starts-with(@href,'image')]";
        public const string IMAGE_PREVIEW_URL_SEARCH_XPATH = "//center//a[starts-with(@href,'image')]/img";
        public const string IMAGE_TITLE_SEARCH_XPATH = "//center[2]";
        public const string IMAGE_DESCRIPTION_SEARCH_XPATH = "//body/p[1]";
        public const int DEFAULT_PARALLEL_CONNECTIONS = 4;
        
        public static readonly DateTime APOD_MIN_DATE = new DateTime(1995, 6, 16);
        
        public static string DEFAULT_DOWNLOAD_DIR = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        
        #region P/Invoke declarations
        
        [DllImport("User32", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int uiAction, int uiParam, string pvParam, uint fWinIni);
        
        #endregion
        
        static async Task Main(string[] args)
        {
            try
            {
                (string to_file, bool set_wallpaper, DateTime? date) = ParseArguments(args);

                if (to_file == null)
                {
                    AppBuilder.Configure<App>()
                        .UsePlatformDetect()
                        .LogToTrace()
                        .StartWithClassicDesktopLifetime(args);
                }
                else
                {
                    WriteInfo("APOD wallpaper downloader started...");
                    string doc_url = date.HasValue ? GetAPODPageURL(date.Value) : APOD_URL_BASE + APOD_MAIN_PAGE;
                    using (var client = new HttpClient())
                    {
                        WriteInfo($"Conectando con '{doc_url}' para determinar la imagen del día");

                        HtmlDocument page = await GetHTMLDocument(client, doc_url);
                        string image_url = APOD_URL_BASE + GetImageURLFromAPOD(page);
                        if (!IsValidURL(image_url))
                        {
                            throw new Exception($"La URL de la imagen '{image_url}' no es válida");
                        }

                        WriteInfo($"Conectando con '{image_url}' para descargar la imagen del día");
                        WriteInfo("Comenzando la descarga de la imagen del día...");
                        byte[] image_bytes = await DownloadImageParallelToBytes(client, image_url);
                        string file_path = to_file;
                        if (Directory.Exists(file_path))
                        {
                            string file_name = GetImagefileNameFromURL(image_url);
                            file_path = Path.Combine(file_path, file_name);
                        }

                        if (!IsValidFilePath(file_path))
                        {
                            throw new Exception($"La ruta de descarga '{file_path}' no es válida");
                        }

                        WriteInfo($"Guardando la imagen del día en '{file_path}'");
                        File.WriteAllBytes(file_path, image_bytes);
                        if (!CheckFileAccess(file_path, FileMode.Open, FileAccess.Read) || !IsImageFile(file_path))
                        {
                            throw new Exception($"El archivo '{file_path}' no es una imagen válida.");
                        }

                        if (set_wallpaper)
                        {
                            WriteInfo($"Cambiando el wallpaper por '{file_path}'");
                            SetWallpaper(file_path);
                        }
                    }
                }
            }
            catch(ArgumentException e)
            {
                WriteError(e.Message);
                ShowHelp();
                Environment.Exit(1);
            } 
            catch (Exception e)
            {
                WriteError(e.Message);
                Environment.Exit(1);
            }
        }
        
        #region Public API
        
        public static bool IsValidURL(string URL)
        {
            Uri uri_result;
            return Uri.TryCreate(URL, UriKind.Absolute, out uri_result) && ( uri_result.Scheme == Uri.UriSchemeHttp || uri_result.Scheme == Uri.UriSchemeHttps);
        }

        public static string GetAPODPageURL(DateTime date)
        {
            return $"{APOD_URL_BASE}ap{date:yyMMdd}.html";
        }

        public static bool IsValidAPODDate(DateTime date)
        {
            return date >= APOD_MIN_DATE && date <= DateTime.Today;
        }

        public static DateTime? ParseAPODDate(string value)
        {
            if (DateTime.TryParseExact(value, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                return date;
            }
            return null;
        }

        public static bool IsValidFilePath(string file_path)
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

        public static bool IsImageFile(string file_path)
        {
            string extension = Path.GetExtension(file_path).ToLower();
            return extension.Equals(".bmp") ||
                   extension.Equals(".jpg") ||
                   extension.Equals(".jpeg") ||
                   extension.Equals(".png");
        }

        public static bool CheckFileAccess(string file_name, FileMode open_mode, FileAccess access_mode)
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

        public static async Task<HtmlDocument> GetHTMLDocument(HttpClient client, string url, CancellationToken token = default, int timeout = 30000)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);
            
            var response = await client.GetAsync(url, cts.Token);
            var page_contents = await response.Content.ReadAsStringAsync();
            HtmlDocument page_document = new HtmlDocument();
            page_document.LoadHtml(page_contents);
            return page_document;
        }

        public static string GetImageURLFromAPOD(HtmlDocument page_document)
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

        public static string GetImagePreviewURLFromAPOD(HtmlDocument page_document)
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

        public static HtmlNode GetImageTitleFromAPOD(HtmlDocument page_document)
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
        
        public static HtmlNode GetImageDescriptionFromAPOD(HtmlDocument page_document)
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

        public static string GetImagefileNameFromURL(string image_url)
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

        public static async Task<Bitmap> DownloadImage(HttpClient client, string url, CancellationToken token = default, int timeout = 30000)
        {
            var bytes = await DownloadImageToBytes(client, url, token, timeout);
            using var memoryStream = new MemoryStream(bytes, writable: false);

            return new Bitmap(memoryStream);
        }

        public static async Task<byte[]> DownloadImageToBytes(HttpClient client, string url, CancellationToken token = default, int timeout = 30000)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);

            var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(cts.Token);
        }

        public static async Task<Bitmap> DownloadImageParallel(HttpClient client, string url, CancellationToken token = default, int timeout = 30000, int maxConnections = DEFAULT_PARALLEL_CONNECTIONS)
        {
            var bytes = await DownloadImageParallelToBytes(client, url, token, timeout, maxConnections);
            using var memoryStream = new MemoryStream(bytes, writable: false);

            return new Bitmap(memoryStream);
        }

        public static async Task<byte[]> DownloadImageParallelToBytes(HttpClient client, string url, CancellationToken token = default, int timeout = 30000, int maxConnections = DEFAULT_PARALLEL_CONNECTIONS)
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

        private static async Task DownloadRangeToFileAsync(HttpClient client, string url, long start, long end, string tempPath, CancellationToken token, int timeout)
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

        public static void SetWallpaper(string image_path)
        {
            string path = new Uri(image_path).LocalPath;

            if (isWindows())
            {
                SystemParametersInfo(0x0014, 0, path, 0x0001);
            }
            else if (isOsx())
            {
                Process.Start("osascript", $"-e 'tell application \"Finder\" to set desktop picture to POSIX file \"{path}\"'");
            }
            else if (isLinux())
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
        
        #region Internal API
        
        private static void WriteLine(string line)
        {
            Console.WriteLine(line);
        }

        private static void WriteInfo(string line)
        {
            Console.Error.WriteLine($"INFO\t=> {line}");
        }

        private static void WriteError(string line)
        {
            Console.Error.WriteLine($"ERROR\t=> {line}");
        }
        
        private static (string to_file, bool set_wallpaper, DateTime? date) ParseArguments(string[] args)
        {
            string to_file = null;
            bool set_wallpaper = false;
            DateTime? date = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--set-wallpaper":
                        set_wallpaper = true;
                        break;
                    case "--to-file":
                        // Get next
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            i = i + 1;
                            string path = args[i];
                            if (IsValidFilePath(path))
                            {
                                to_file = path;
                                break;
                            }
                            throw new ArgumentException("Parámetro no válido");
                        }
                        // Sin valor: directorio por defecto
                        to_file = DEFAULT_DOWNLOAD_DIR;
                        break;
                    case "--date":
                        // Get next
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            i = i + 1;
                            DateTime? parsed = ParseAPODDate(args[i]);
                            if (parsed.HasValue && IsValidAPODDate(parsed.Value))
                            {
                                date = parsed;
                                break;
                            }
                            throw new ArgumentException(
                                $"Fecha '{args[i]}' no válida o fuera del rango de APOD (desde {APOD_MIN_DATE:dd/MM/yyyy} hasta hoy). Formato esperado: aammdd (ej. 260814 = 14/08/2026).");
                        }
                        throw new ArgumentException("El parámetro --date requiere un valor en formato aammdd (ej. 260814 = 14/08/2026).");
                    case "--help":
                    case "-h":
                        ShowHelp();
                        Environment.Exit(0);
                        break;
                    default:
                        throw new ArgumentException($"Parámetro '{args[i]}' no reconocido");
                }
            }

            if ((set_wallpaper || date.HasValue) && to_file == null)
            {
                to_file = DEFAULT_DOWNLOAD_DIR;
            }

            return (to_file, set_wallpaper, date);
        }
        
        private static void ShowHelp()
        {
            WriteLine("APOD Wallpapers:");
            WriteLine("\tDescarga la imagen del día de Astronomy Picture of the Day y, si se indica, la establece como fondo de pantalla.");
            WriteLine("Opciones:\n");
            WriteLine("-h, --help\t\tMostrar ayuda");
            WriteLine("--to-file [<ruta>]\tGuarda la imagen en <ruta>. Si <ruta> es un directorio, guarda la");
            WriteLine("\t\t\timagen en ese directorio con el nombre que le da la web. Sin <ruta> usa el");
            WriteLine($"\t\t\tdirectorio por defecto ({DEFAULT_DOWNLOAD_DIR}).");
            WriteLine("--set-wallpaper\t\tEstablece la imagen como fondo de pantalla tras descargarla.");
            WriteLine("--date <aammdd>\t\tDescarga la imagen de la fecha indicada (formato aammdd, ej. 260814 =");
            WriteLine($"\t\t\t14/08/2026). Rango válido: desde {APOD_MIN_DATE:dd/MM/yyyy} hasta hoy.");
            WriteLine("\nSin argumentos se abre la interfaz gráfica. Cualquiera de las opciones anteriores ejecuta la");
            WriteLine($"versión de consola. --set-wallpaper y --date implican --to-file sin valor (guarda en {DEFAULT_DOWNLOAD_DIR})");
            WriteLine("a no ser que se indique un --to-file explícito.");
        }
        
        private static bool isWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static bool isLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        private static bool isOsx() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        
        #endregion
    }
}
