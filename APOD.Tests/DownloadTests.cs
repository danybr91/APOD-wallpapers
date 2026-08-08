using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using APOD_wallpapers;
using Xunit;
using Xunit.Abstractions;

namespace APOD.Tests
{
    public class DownloadTests
    {
        private const string ImageUrl = "https://apod.nasa.gov/apod/image/2608/MeteorGecko_Burnett_4944.jpg";
        private const string PageUrl = "https://apod.nasa.gov/apod/ap260803.html";
        private const int Iterations = 1;
        private const int ParallelConnections = 4; //Por cortesia al servidor, mejor dejarlo en un equilibro.

        private readonly ITestOutputHelper _output;

        public DownloadTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Page_ExposesDistinctFullResAndPreviewUrls()
        {
            using var client = new HttpClient();
            HtmlAgilityPack.HtmlDocument page = await Program.GetHTMLDocument(client, PageUrl);

            string fullResUrl = Program.APOD_URL_BASE + Program.GetImageURLFromAPOD(page);
            string previewUrl = Program.APOD_URL_BASE + Program.GetImagePreviewURLFromAPOD(page);

            _output.WriteLine($"Full-res: {fullResUrl}");
            _output.WriteLine($"Preview:  {previewUrl}");

            Assert.True(Program.IsValidURL(fullResUrl));
            Assert.True(Program.IsValidURL(previewUrl));
            Assert.NotEqual(fullResUrl, previewUrl);
        }

        [Fact]
        public async Task BothMethods_ProduceSameBytes()
        {
            using var client = new HttpClient();
            _output.WriteLine($"Imagen: {ImageUrl}");

            byte[] sequential = await Program.DownloadImageToBytes(client, ImageUrl);
            byte[] parallel = await Program.DownloadImageParallelToBytes(client, ImageUrl, maxConnections: ParallelConnections);

            Assert.NotNull(sequential);
            Assert.NotNull(parallel);
            Assert.Equal(sequential.Length, parallel.Length);
            Assert.Equal(sequential, parallel);
            _output.WriteLine($"Tamaño: {sequential.Length} bytes");
        }

        [Fact]
        public async Task Benchmark_SequentialVsParallel()
        {
            using var client = new HttpClient();
            _output.WriteLine($"Imagen: {ImageUrl}");

            var sequentialTimes = new List<TimeSpan>();
            var parallelTimes = new List<TimeSpan>();

            for (int i = 0; i < Iterations; i++)
            {
                sequentialTimes.Add(await TimeAsync(() => Program.DownloadImageToBytes(client, ImageUrl)));
            }

            for (int i = 0; i < Iterations; i++)
            {
                parallelTimes.Add(await TimeAsync(() => Program.DownloadImageParallelToBytes(client, ImageUrl, maxConnections: ParallelConnections)));
            }

            _output.WriteLine("Secuencial (s): " + string.Join(", ", sequentialTimes.Select(t => t.TotalSeconds.ToString("F2"))));
            _output.WriteLine("Paralela (s):   " + string.Join(", ", parallelTimes.Select(t => t.TotalSeconds.ToString("F2"))));
            _output.WriteLine($"Mejor secuencial: {sequentialTimes.Min().TotalSeconds:F2}s | Mejor paralela: {parallelTimes.Min().TotalSeconds:F2}s");
        }

        private static async Task<TimeSpan> TimeAsync(Func<Task> action)
        {
            var stopwatch = Stopwatch.StartNew();
            await action();
            stopwatch.Stop();
            return stopwatch.Elapsed;
        }
    }
}
