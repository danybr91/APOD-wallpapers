using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using APOD.Core;
using Xunit;
using Xunit.Abstractions;

namespace APOD.Tests
{
    public class DownloadTests
    {
        private const string ImageUrl = "https://assets.science.nasa.gov/content/dam/science/cds/apod/apod/2026/august/MeteorGecko_Burnett_4944.jpg";
        private static readonly DateTime ImageDate = new DateTime(2026, 8, 3);
        private static readonly DateTime VideoDate = new DateTime(2026, 9, 9);
        private const int Iterations = 1;
        private const int ParallelConnections = 4; //Por cortesia al servidor, mejor dejarlo en un equilibro.

        private readonly ITestOutputHelper _output;
        private readonly ApodService _service = new ApodService(new TestLogger());

        public DownloadTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Page_ExposesDistinctFullResAndPreviewUrls()
        {
            using var client = new HttpClient();
            string pageUrl = await _service.GetAPODPageURL(client, ImageDate);
            HtmlAgilityPack.HtmlDocument page = await _service.GetHTMLDocument(client, pageUrl);

            string fullResUrl = _service.GetImageURLFromAPOD(page);
            string previewUrl = _service.GetImagePreviewURLFromAPOD(page);

            _output.WriteLine($"Página:   {pageUrl}");
            _output.WriteLine($"Full-res: {fullResUrl}");
            _output.WriteLine($"Preview:  {previewUrl}");

            Assert.Equal(ImageUrl, fullResUrl);
            Assert.True(_service.IsValidURL(previewUrl));
            Assert.NotEqual(fullResUrl, previewUrl);
            Assert.False(_service.HasVideo(page));
            Assert.Equal("Vaporizing Meteor Photobombs the Lacerta Nebula", _service.GetImageTitleFromAPOD(page).InnerText.Trim());
            Assert.StartsWith("Explanation:", _service.GetImageDescriptionFromAPOD(page).InnerText.Trim());
        }

        [Fact]
        public async Task MainPage_ExposesImageOrVideo()
        {
            using var client = new HttpClient();
            HtmlAgilityPack.HtmlDocument page = await _service.GetHTMLDocument(client, _service.GetAPODMainPageURL());

            _output.WriteLine($"Título: {_service.GetImageTitleFromAPOD(page).InnerText.Trim()}");
            Assert.NotNull(_service.GetImageDescriptionFromAPOD(page));
        }

        [Fact]
        public async Task VideoPage_ExposesVideoUrl()
        {
            using var client = new HttpClient();
            string pageUrl = await _service.GetAPODPageURL(client, VideoDate);
            HtmlAgilityPack.HtmlDocument page = await _service.GetHTMLDocument(client, pageUrl);

            Assert.True(_service.HasVideo(page));
            string videoUrl = _service.GetVideoUrl(page);
            _output.WriteLine($"Vídeo: {videoUrl}");
            Assert.True(_service.IsValidURL(videoUrl));
            Assert.Equal("xz_and.mp4", _service.GetImagefileNameFromURL(videoUrl));
        }

        [Fact]
        public async Task FirstApod_ResolvesPage()
        {
            using var client = new HttpClient();
            string pageUrl = await _service.GetAPODPageURL(client, ApodService.APOD_MIN_DATE);
            HtmlAgilityPack.HtmlDocument page = await _service.GetHTMLDocument(client, pageUrl);

            string fullResUrl = _service.GetImageURLFromAPOD(page);
            _output.WriteLine($"Página:   {pageUrl}");
            _output.WriteLine($"Full-res: {fullResUrl}");
            Assert.Contains("apod-1995-june-16-", pageUrl);
            Assert.True(_service.IsValidURL(fullResUrl));
        }

        [Fact]
        public async Task BothMethods_ProduceSameBytes()
        {
            using var client = new HttpClient();
            _output.WriteLine($"Imagen: {ImageUrl}");

            byte[] sequential = await _service.DownloadImageToBytes(client, ImageUrl);
            byte[] parallel = await _service.DownloadImageParallelToBytes(client, ImageUrl, maxConnections: ParallelConnections);

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
                sequentialTimes.Add(await TimeAsync(() => _service.DownloadImageToBytes(client, ImageUrl)));
            }

            for (int i = 0; i < Iterations; i++)
            {
                parallelTimes.Add(await TimeAsync(() => _service.DownloadImageParallelToBytes(client, ImageUrl, maxConnections: ParallelConnections)));
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

        private class TestLogger : ILog
        {
            public void Info(string message) { }
            public void Error(string message) { }
            public void Line(string message) { }
        }
    }
}
