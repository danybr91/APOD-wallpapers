using System.Threading.Tasks;

namespace APOD.Core
{
    // Operaciones que dependen del sistema operativo. Se obtiene con Util.GetOsService().
    internal interface IOsService
    {
        Task SetWallpaperAsync(string path);
    }
}
