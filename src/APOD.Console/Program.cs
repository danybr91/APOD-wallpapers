using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using APOD.Core;
using HtmlAgilityPack;

namespace APOD.Console
{
    class Program
    {
        static int Main(string[] args)
        {
            var logger = new ConsoleLogger();
            var service = new ApodService(logger);

            try
            {
                (string to_file, bool set_wallpaper, DateTime? date) = ParseArguments(service, args);

                // En la versión de consola no existe interfaz gráfica.
                // Sin argumentos accionables se muestra la ayuda.
                if (to_file == null)
                {
                    logger.Line(BuildHelp());
                    return 0;
                }

                using (var client = new HttpClient())
                {
                    DownloadAndSaveAsync(service, logger, client, date, to_file, set_wallpaper).GetAwaiter().GetResult();
                }
            }
            catch (ArgumentException e)
            {
                logger.Error(e.Message);
                logger.Line(BuildHelp());
                return 1;
            }
            catch (Exception e)
            {
                logger.Error(e.Message);
                return 1;
            }
            return 0;
        }

        private static (string to_file, bool set_wallpaper, DateTime? date) ParseArguments(ApodService service, string[] args)
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
                            if (service.IsValidFilePath(path))
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
                            DateTime? parsed = service.ParseAPODDate(args[i]);
                            if (parsed.HasValue && service.IsValidAPODDate(parsed.Value))
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
                Environment.NewLine + "Sin argumentos se abre la interfaz gráfica. Cualquiera de las opciones anteriores ejecuta la" + Environment.NewLine +
                $"versión de consola. --set-wallpaper y --date implican --to-file sin valor (guarda en {ApodService.DefaultDownloadDir})" + Environment.NewLine +
                "a no ser que se indique un --to-file explícito.";
        }

        private static async Task DownloadAndSaveAsync(ApodService service, ConsoleLogger logger, HttpClient client, DateTime? date, string to_file, bool set_wallpaper)
        {
            logger.Info("APOD wallpaper downloader started...");
            string doc_url = date.HasValue ? service.GetAPODPageURL(date.Value) : ApodService.APOD_URL_BASE + ApodService.APOD_MAIN_PAGE;
            logger.Info($"Conectando con '{doc_url}' para determinar la imagen del día");

            HtmlDocument page = await service.GetHTMLDocument(client, doc_url);
            string image_url = ApodService.APOD_URL_BASE + service.GetImageURLFromAPOD(page);
            if (!service.IsValidURL(image_url))
            {
                throw new Exception($"La URL de la imagen '{image_url}' no es válida");
            }

            logger.Info($"Conectando con '{image_url}' para descargar la imagen del día");
            logger.Info("Comenzando la descarga de la imagen del día...");
            byte[] image_bytes = await service.DownloadImageParallelToBytes(client, image_url);
            string file_path = to_file;
            if (Directory.Exists(file_path))
            {
                string file_name = service.GetImagefileNameFromURL(image_url);
                file_path = Path.Combine(file_path, file_name);
            }

            if (!service.IsValidFilePath(file_path))
            {
                throw new Exception($"La ruta de descarga '{file_path}' no es válida");
            }

            logger.Info($"Guardando la imagen del día en '{file_path}'");
            File.WriteAllBytes(file_path, image_bytes);
            if (!service.CheckFileAccess(file_path, FileMode.Open, FileAccess.Read) || !service.IsImageFile(file_path))
            {
                throw new Exception($"El archivo '{file_path}' no es una imagen válida.");
            }

            if (set_wallpaper)
            {
                logger.Info($"Cambiando el wallpaper por '{file_path}'");
                service.SetWallpaper(file_path);
            }
        }
    }
}