using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using HtmlAgilityPack;
using APOD.Core;

namespace APOD.UI
{
    class Program
    {
        private static readonly UiLogger _logger = new UiLogger();
        private static readonly ApodService _service = new ApodService(_logger);

        public static ApodService Service => _service;

        public static UiLogger Logger => _logger;

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                return LaunchGui(args);
            }

            return RunHosted(args);
        }

        private static int LaunchGui(string[] args)
        {
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
            return 0;
        }

        private static int RunHosted(string[] args)
        {
            try
            {
                (string to_file, bool set_wallpaper, DateTime? date) = ParseArguments(args);

                // Sin destino accionable: se muestra la ayuda ajustada a la plataforma.
                if (to_file == null)
                {
                    _logger.Line(BuildHelp());
                    return 0;
                }

                using (var client = new HttpClient())
                {
                    DownloadAndSaveAsync(client, date, to_file, set_wallpaper).GetAwaiter().GetResult();
                }
            }
            catch (ArgumentException e)
            {
                _logger.Error(e.Message);
                _logger.Line(BuildHelp());
                return 1;
            }
            catch (Exception e)
            {
                _logger.Error(e.Message);
                return 1;
            }
            return 0;
        }

        // La UI procesa únicamente las opciones que no requieren salida de consola.
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
                            if (_service.IsValidFilePath(path))
                            {
                                to_file = path;
                                break;
                            }
                            throw new ArgumentException("Parámetro no válido");
                        }
                        // Sin valor: directorio por defecto
                        to_file = ApodService.DefaultDownloadDir;
                        break;
                    case "--date":
                        // Get next
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            i = i + 1;
                            DateTime? parsed = _service.ParseAPODDate(args[i]);
                            if (parsed.HasValue && _service.IsValidAPODDate(parsed.Value))
                            {
                                date = parsed;
                                break;
                            }
                            throw new ArgumentException(
                                $"Fecha '{args[i]}' no válida o fuera del rango de APOD (desde {ApodService.APOD_MIN_DATE:dd/MM/yyyy} hasta hoy). Formato esperado: aammdd (ej. 260814 = 14/08/2026).");
                        }
                        throw new ArgumentException("El parámetro --date requiere un valor en formato aammdd (ej. 260814 = 14/08/2026).");
                    case "--help":
                    case "-h":
                        return (null, set_wallpaper, date);
                    default:
                        throw new ArgumentException($"Parámetro '{args[i]}' no reconocido");
                }
            }

            if ((set_wallpaper || date.HasValue) && to_file == null)
            {
                to_file = ApodService.DefaultDownloadDir;
            }

            return (to_file, set_wallpaper, date);
        }

        private static string BuildHelp()
        {
            return $"APOD Wallpapers {Util.GetVersion()}:" + Environment.NewLine +
                "\tDescarga la imagen del día de Astronomy Picture of the Day y, si se indica, la establece como fondo de pantalla." + Environment.NewLine +
                "Opciones:" + Environment.NewLine +
                "-h, --help\t\tMostrar ayuda" + Environment.NewLine +
                "--to-file [<ruta>]\tGuarda la imagen en <ruta>. Si <ruta> es un directorio, guarda la" + Environment.NewLine +
                "\t\t\timagen en ese directorio con el nombre que le da la web. Sin <ruta> usa el" + Environment.NewLine +
                $"\t\t\tdirectorio por defecto ({ApodService.DefaultDownloadDir})." + Environment.NewLine +
                "--set-wallpaper\t\tEstablece la imagen como fondo de pantalla tras descargarla." + Environment.NewLine +
                "--date <aammdd>\t\tDescarga la imagen de la fecha indicada (formato aammdd, ej. 260814 =" + Environment.NewLine +
                $"\t\t\t14/08/2026). Rango válido: desde {ApodService.APOD_MIN_DATE:dd/MM/yyyy} hasta hoy." + Environment.NewLine +
                Environment.NewLine + "Si se indica alguna opción se ejecuta sin interfaz gráfica (sin salida de consola)." + Environment.NewLine +
                $"Con --set-wallpaper o --date sin --to-file se guarda en {ApodService.DefaultDownloadDir}.";
        }

        private static async Task DownloadAndSaveAsync(HttpClient client, DateTime? date, string to_file, bool set_wallpaper)
        {
            _logger.Info("APOD wallpaper downloader started...");
            string doc_url = date.HasValue ? _service.GetAPODPageURL(date.Value) : ApodService.APOD_URL_BASE + ApodService.APOD_MAIN_PAGE;
            _logger.Info($"Conectando con '{doc_url}' para determinar la imagen del día");

            HtmlDocument page = await _service.GetHTMLDocument(client, doc_url);
            string image_url = ApodService.APOD_URL_BASE + _service.GetImageURLFromAPOD(page);
            if (!_service.IsValidURL(image_url))
            {
                throw new Exception($"La URL de la imagen '{image_url}' no es válida");
            }

            _logger.Info($"Conectando con '{image_url}' para descargar la imagen del día");
            _logger.Info("Comenzando la descarga de la imagen del día...");
            byte[] image_bytes = await _service.DownloadImageParallelToBytes(client, image_url);
            string file_path = to_file;
            if (Directory.Exists(file_path))
            {
                string file_name = _service.GetImagefileNameFromURL(image_url);
                file_path = Path.Combine(file_path, file_name);
            }

            if (!_service.IsValidFilePath(file_path))
            {
                throw new Exception($"La ruta de descarga '{file_path}' no es válida");
            }

            _logger.Info($"Guardando la imagen del día en '{file_path}'");
            File.WriteAllBytes(file_path, image_bytes);
            if (!_service.CheckFileAccess(file_path, FileMode.Open, FileAccess.Read) || !_service.IsImageFile(file_path))
            {
                throw new Exception($"El archivo '{file_path}' no es una imagen válida.");
            }

            if (set_wallpaper)
            {
                _logger.Info($"Cambiando el wallpaper por '{file_path}'");
                _service.SetWallpaper(file_path);
            }
        }

        #region Bitmap wrappers

        public static async Task<Bitmap> DownloadImage(HttpClient client, string url, CancellationToken token = default, int timeout = 30000)
        {
            var bytes = await _service.DownloadImageToBytes(client, url, token, timeout);
            using var memoryStream = new MemoryStream(bytes, writable: false);

            return new Bitmap(memoryStream);
        }

        public static async Task<Bitmap> DownloadImageParallel(HttpClient client, string url, CancellationToken token = default, int timeout = 30000, int maxConnections = ApodService.DEFAULT_PARALLEL_CONNECTIONS)
        {
            var bytes = await _service.DownloadImageParallelToBytes(client, url, token, timeout, maxConnections);
            using var memoryStream = new MemoryStream(bytes, writable: false);

            return new Bitmap(memoryStream);
        }

        #endregion
    }
}
