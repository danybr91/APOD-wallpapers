using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APOD.UI
{
    /// <summary>
    /// Preferencias de la interfaz gráfica, guardadas en JSON. El archivo se busca, por este orden, en el
    /// directorio de trabajo, en la carpeta del ejecutable y en la carpeta de configuración del usuario.
    /// </summary>
    public class AppSettings
    {
        public const string FILE_NAME = "settings.json";

        // Ruta en el directorio de trabajo desde el que se abre la aplicación.
        private static string WorkingDirPath => Path.Combine(Directory.GetCurrentDirectory(), FILE_NAME);

        // Ruta junto al ejecutable.
        private static string ExeDirPath => Path.Combine(AppContext.BaseDirectory, FILE_NAME);

        // Ruta en la carpeta de configuración del usuario (~/.config en Linux y macOS, %APPDATA% en Windows).
        private static string UserConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "APOD Wallpapers",
            FILE_NAME);

        /// <summary>
        /// Archivo de ajustes en uso: el primero que existe según el orden de búsqueda. Si no existe
        /// ninguno, el de la carpeta de configuración del usuario, que es donde se crea al guardar.
        /// </summary>
        public static string FilePath
        {
            get
            {
                foreach (string path in new[] { WorkingDirPath, ExeDirPath })
                {
                    if (File.Exists(path)) return path;
                }
                return UserConfigPath;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        // Carpeta donde se guardan las imágenes descargadas. Vacía: se pregunta en cada descarga.
        public string DownloadDir { get; set; } = "";

        // Motivo por el que no se pudo leer el archivo (JSON mal formado, sin permisos...); null si se leyó bien.
        [JsonIgnore]
        public string LoadError { get; private set; }

        /// <summary>
        /// Primer problema de los ajustes cargados (archivo ilegible o carpeta no válida), o null si no hay ninguno.
        /// </summary>
        public string Validate()
        {
            return LoadError ?? ValidateDownloadDir(DownloadDir?.Trim());
        }

        /// <summary>
        /// Valida una carpeta de descarga. Devuelve el motivo del error o null si es válida (o está vacía).
        /// </summary>
        public static string ValidateDownloadDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return null;

            if (!Path.IsPathFullyQualified(dir))
                return "La carpeta de descarga debe ser una ruta absoluta.";

            // Cada tramo de la ruta (sin la raíz, p. ej. "C:\") debe ser un nombre de carpeta válido.
            string root = Path.GetPathRoot(dir) ?? "";
            char[] separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            foreach (string segment in dir[root.Length..].Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
                    return $"La carpeta de descarga contiene caracteres no válidos en '{segment}'.";
            }

            try
            {
                Path.GetFullPath(dir);
            }
            catch (Exception ex)
            {
                return "La carpeta de descarga no es válida: " + ex.Message;
            }

            if (File.Exists(dir))
                return "La ruta de descarga es un archivo, no una carpeta.";

            return null;
        }

        public static AppSettings Load()
        {
            string path = FilePath;
            if (!File.Exists(path)) return new AppSettings();

            try
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                // Un archivo corrupto o ilegible no debe impedir abrir la aplicación; se avisa al usarlo.
                return new AppSettings { LoadError = $"No se pudo leer '{path}': {ex.Message}" };
            }
        }

        public void Save()
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
