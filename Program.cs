using HtmlAgilityPack;
using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace APOD_wallpapers
{
    class Program
    {
        private static bool DEBUG = false;

        private static string APOD_URL_BASE = "https://apod.nasa.gov/apod/";
        private static string APOD_MAIN_PAGE = "astropix.html";
        private static string IMAGE_URL_SEARCH_XPATH = "//center//a[starts-with(@href,'image')]";
        private static string DEFAULT_DOWNLOAD_DIR = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        static void WriteLine(string line)
        {
            Console.WriteLine(line);
        }

        static void WriteInfo(string line)
        {
            Console.WriteLine($"INFO\t=> {line}");
        }

        static void WriteError(string line)
        {
            Console.WriteLine($"ERROR\t=> {line}");
        }

        static void WriteDebug(string line)
        {
            if (DEBUG)
            {
                Console.WriteLine($"DEBUG\t=> {line}");
            }
        }

        static void Pause()
        {
            Console.Write("\nPulse cualquier tecla para continuar.");
            Console.ReadLine();
        }

        static void ShowHelp()
        {
            WriteLine("APOD Wallpapers:");
            WriteLine("\tDescarga la imagen del día de Astronomy Picture of Day (si está disponible) y establece el fondo de pantalla.");
            WriteLine("Opciones:\n");
            WriteLine("-h, --help\t\tMostrar ayuda");
            WriteLine($"--outputDir\t\tEstablece el directorio donde se guardarán las imágenes. Por defecto es: {DEFAULT_DOWNLOAD_DIR}");
            WriteLine("--fileName\t\tGuarda la imagen con el nombre de fichero dado. Por defecto obtiene el nombre de la web.");
            WriteLine("-skip-wallpaper\t\tSolo descarga la imagen, no establece el fondo de pantalla.");
            WriteLine("-d, --debug\t\tIncrementar información de la ejecución y no cerrar la ventana auotmáticamente.");
        }

        static void Main(string[] args)
        {
            string download_dir = DEFAULT_DOWNLOAD_DIR;
            string file_name = null;
            bool set_wallpaper = true;
            try
            {
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
                        default:
                            throw new ArgumentException($"Parámetro '{args[i]}' no reconocido");
                    }
                }
                WriteInfo("APOD wallpaper downloader started...");
                MainAsync(download_dir, file_name, set_wallpaper).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch(ArgumentException e)
            {
                WriteError(e.Message);
                ShowHelp();
                Environment.Exit(0);
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
        async static Task MainAsync(string download_dir, string file_name, bool set_wallpaper)
        {
            string doc_url = APOD_URL_BASE + APOD_MAIN_PAGE;
            try
            {
                if (IsValidURL(doc_url))
                {
                    WriteInfo($"Conectando con '{doc_url}' para determinar la imagen del día");

                    HtmlDocument page = GetHTMLDocument(doc_url).GetAwaiter().GetResult();
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
                            DownloadFile(image_url, file_path, null);
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
            catch (Exception e)
            {
                WriteError(e.Message);
                Pause();
                Environment.Exit(1);
            }
        }

        public static bool IsValidURL(string URL)
        {
            Uri uri_result;
            return Uri.TryCreate(URL, UriKind.Absolute, out uri_result) && ( uri_result.Scheme == Uri.UriSchemeHttp || uri_result.Scheme == Uri.UriSchemeHttps);
        }

        public static bool IsValidFilePath(string file_path)
        {
            Uri uri_result;
            return Uri.TryCreate(file_path, UriKind.RelativeOrAbsolute, out uri_result) && (uri_result.Scheme == Uri.UriSchemeFile);
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

        static async Task<HtmlDocument> GetHTMLDocument(string url)
        {
            HttpClient client = new HttpClient();
            var response = await client.GetAsync(url);
            var page_contents = await response.Content.ReadAsStringAsync();
            WriteDebug($"Contenido del HTML:\n{page_contents}");
            HtmlDocument page_document = new HtmlDocument();
            page_document.LoadHtml(page_contents);
            return page_document;
        }

        static string GetImageURLFromAPOD(HtmlDocument page_document)
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

        static string GetImagefileNameFromURL(string image_url)
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

        static string GetFileNameExtension(string file_name)
        {
            return file_name.Substring(file_name.LastIndexOf('.'));
        }

        static void DownloadFile(string url, string file, AsyncCompletedEventHandler completed_handler)
        {
            using (var client = new WebClient())
            {
                if (completed_handler != null) 
                { 
                    client.DownloadFileCompleted += completed_handler; 
                }
                client.DownloadFile(url, file);
            }
        }

        [DllImport("User32", CharSet = CharSet.Auto)]
        public static extern int SystemParametersInfo(int uiAction, int uiParam, string pvParam, uint fWinIni);

        static void SetWallpaper(string image_path)
        {
            WriteDebug($"Ruta wallpaper: '{image_path}'");
            SystemParametersInfo(0x0014, 0, new Uri(image_path).LocalPath, 0x0001);
        }
    }
}
