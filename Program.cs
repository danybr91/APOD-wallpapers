using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
        public static string APOD_URL_BASE = "https://apod.nasa.gov/apod/";
        public static string APOD_MAIN_PAGE = "astropix.html";
        public static string IMAGE_URL_SEARCH_XPATH = "//center//a[starts-with(@href,'image')]";
        public static string IMAGE_TITLE_SEARCH_XPATH = "//center[2]";
        public static string IMAGE_DESCRIPTION_SEARCH_XPATH = "//body/p[1]";
        public static string DEFAULT_DOWNLOAD_DIR = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        
        private static bool DEBUG = false;
        
        [DllImport("User32", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int uiAction, int uiParam, string pvParam, uint fWinIni);

        private static void WriteLine(string line)
        {
            Console.WriteLine(line);
        }

        private static void WriteInfo(string line)
        {
            Console.WriteLine($"INFO\t=> {line}");
        }

        private static void WriteError(string line)
        {
            Console.WriteLine($"ERROR\t=> {line}");
        }

        private static void WriteDebug(string line)
        {
            if (DEBUG)
            {
                Console.WriteLine($"DEBUG\t=> {line}");
            }
        }

        private static void Pause()
        {
            Console.Write("\nPulse cualquier tecla para continuar.");
            Console.ReadLine();
        }

        private static void ShowHelp()
        {
            WriteLine("APOD Wallpapers:");
            WriteLine("\tDescarga la imagen del día de Astronomy Picture of Day (si está disponible) y establece el fondo de pantalla.");
            WriteLine("Opciones:\n");
            WriteLine("-h, --help\t\tMostrar ayuda");
            WriteLine($"--outputDir\t\tEstablece el directorio donde se guardarán las imágenes. Por defecto es: {DEFAULT_DOWNLOAD_DIR}");
            WriteLine("--fileName\t\tGuarda la imagen con el nombre de fichero dado. Por defecto obtiene el nombre de la web.");
            WriteLine("-skip-wallpaper\t\tSolo descarga la imagen, no establece el fondo de pantalla.");
            WriteLine("-d, --debug\t\tIncrementar información de la ejecución y no cerrar la ventana auotmáticamente.");
            WriteLine("--no-gui\t\tEjecutar la versión de consola");
        }

        static void Main(string[] args)
        {
            try
            {
                string download_dir = DEFAULT_DOWNLOAD_DIR;
                string file_name = null;
                bool set_wallpaper = true;
                bool is_gui = true;
                
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLower())
                    {
                        case "--outputdir":
                            // Get next
                            if (i + 1 < args.Length)
                            {
                                i = i + 1;
                                download_dir = args[i];
                                if (IsValidFilePath(download_dir))
                                {
                                    break;
                                }
                            }
                            throw new ArgumentException("Parámetro no válido");
                        case "--filename":
                            // Get next
                            if (i + 1 < args.Length)
                            {
                                i = i + 1;
                                file_name = args[i];
                                if (file_name.IndexOfAny(Path.GetInvalidFileNameChars()) == -1)
                                {
                                    break;
                                }
                            }
                            throw new ArgumentException("Parámetro no válido");
                        case "--skip-wallpaper":
                            set_wallpaper = false;
                            break;
                        case "--debug":
                        case "-d":
                            DEBUG = true;
                            break;
                        case "--help":
                        case "-h":
                            ShowHelp();
                            Environment.Exit(0);
                            break;
                        case "--no-gui":
                            is_gui = false;
                            break;
                        default:
                            throw new ArgumentException($"Parámetro '{args[i]}' no reconocido");
                    }
                }
                
                if (is_gui)
                {
                    AppBuilder.Configure<App>()
                        .UsePlatformDetect()
                        .LogToTrace()
                        .StartWithClassicDesktopLifetime(args);
                }
                else
                {
                    DownloadTodayImageAsync(download_dir, file_name, set_wallpaper).ConfigureAwait(false).GetAwaiter()
                        .GetResult();
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
                Pause();
                Environment.Exit(1);
            }
        }

        /*
        1. buscar tag center
        2. buscar tag IMG3
        3. buscar tag a encima de IMG
        4. descargar href en imagenes
        5. comprobar es imagen valida
        5. poner fondo de pantalla
         */
        private async static Task DownloadTodayImageAsync(string download_dir, string file_name, bool set_wallpaper)
        {
            WriteInfo("APOD wallpaper downloader started...");
            
            string doc_url = APOD_URL_BASE + APOD_MAIN_PAGE;
            using (var client = new HttpClient())
            {
                if (IsValidURL(doc_url))
                {
                    WriteInfo($"Conectando con '{doc_url}' para determinar la imagen del día");

                    HtmlDocument page = GetHTMLDocument(client, doc_url).GetAwaiter().GetResult();
                    string image_url = APOD_URL_BASE + GetImageURLFromAPOD(page);
                    if (IsValidURL(image_url))
                    {
                        WriteInfo($"Conectando con '{image_url}' para descargar la imagen del día");
                        string file_path = Path.Combine(download_dir, 
                            file_name == null ? 
                            GetImagefileNameFromURL(image_url) : 
                            file_name + GetFileNameExtension(GetImagefileNameFromURL(image_url)));
                        if (IsValidFilePath(file_path))
                        {
                            WriteInfo($"Descargando imagen del día en '{file_path}'");
                            using var image = await DownloadImage(client, image_url);
                            image.Save(file_path);
                            if (CheckFileAccess(file_path, FileMode.Open, FileAccess.Read) && IsImageFile(file_path))
                            {
                                if (set_wallpaper)
                                {
                                    WriteInfo($"Cambiando el wallpaper por '{file_path}'");
                                    SetWallpaper(file_path);
                                }
                                if (DEBUG) Pause();
                                Environment.Exit(0);
                            }
                            else
                            {
                                throw new Exception($"El archivo '{file_path}' no es una imagen válida.");
                            }
                        }
                        else
                        {
                            throw new Exception($"La ruta de descarga '{file_path}' no es válida");
                        }
                    }
                    else
                    {
                        throw new Exception($"La URL de la imagen '{image_url}' no es válida");
                    }
                }
                else
                {
                    throw new Exception($"La URL de APOD '{doc_url}' no es válida");
                }
            }
        }

        // API
        
        public static bool IsValidURL(string URL)
        {
            Uri uri_result;
            return Uri.TryCreate(URL, UriKind.Absolute, out uri_result) && ( uri_result.Scheme == Uri.UriSchemeHttp || uri_result.Scheme == Uri.UriSchemeHttps);
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
            WriteDebug($"Extensión de archivo: {extension}");
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
            WriteDebug($"Contenido del HTML:\n{page_contents}");
            HtmlDocument page_document = new HtmlDocument();
            page_document.LoadHtml(page_contents);
            return page_document;
        }

        public static string GetImageURLFromAPOD(HtmlDocument page_document)
        {
            WriteDebug($"Comando XPATH: '{IMAGE_URL_SEARCH_XPATH}'");
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

        public static HtmlNode GetImageTitleFromAPOD(HtmlDocument page_document)
        {
            WriteDebug($"Comando XPATH: '{IMAGE_TITLE_SEARCH_XPATH}'");
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
            WriteDebug($"Comando XPATH: '{IMAGE_DESCRIPTION_SEARCH_XPATH}'");
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

        public static string GetFileNameExtension(string file_name)
        {
            return file_name.Substring(file_name.LastIndexOf('.'));
        }

        public static async Task<Bitmap> DownloadImage(HttpClient client, string url, CancellationToken token = default, int timeout = 30000)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);

            var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            using var memoryStream = new MemoryStream(bytes, writable: false);

            return new Bitmap(memoryStream);
        }

        public static void SetWallpaper(string image_path)
        {
            string path = new Uri(image_path).LocalPath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SystemParametersInfo(0x0014, 0, path, 0x0001);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("osascript", $"-e 'tell application \"Finder\" to set desktop picture to POSIX file \"{path}\"'");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
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
    }
}
