using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HtmlAgilityPack;
using Avalonia.Platform.Storage;
using APOD.Core;

namespace APOD.UI
{
    public partial class MainWindow : Window
    {
        private string file_name;
        private Bitmap image;
        private string fullResUrl;
        private bool _isVideo;
        private DateTime _currentDate = DateTime.Today;
        private bool _isSettingDate;
        private CancellationTokenSource _loadCts;
        
        public MainWindow()
        {
            InitializeComponent();
            // Enruta los logs de ApodService a la barra de estado de la ventana.
            Program.Logger.StatusWriter = SetStatus;
            // Descarga la imagen inicial al abrir la ventana
            DownloadTodayImage();
        }
        
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            // Añade esta línea para inicializar el control StatusText
            StatusText = this.FindControl<TextBlock>("StatusText");
            PreviewImage = this.FindControl<Image>("PreviewImage");
            LoadingOverlay = this.FindControl<Border>("LoadingOverlay");
            TitleText = this.FindControl<TextBlock>("TitleText");
            InfoText = this.FindControl<TextBlock>("InfoText");
            DownloadButton = this.FindControl<Button>("DownloadButton");
            SetWallpaperButton = this.FindControl<Button>("SetWallpaperButton");
            DatePicker = this.FindControl<DatePicker>("DatePicker");
            PrevDayButton = this.FindControl<Button>("PrevDayButton");
            NextDayButton = this.FindControl<Button>("NextDayButton");
            MoreInfoLink = this.FindControl<HyperlinkButton>("MoreInfoLink");
            VideoWebView = this.FindControl<NativeWebView>("VideoWebView");

            DatePicker.MinYear = new DateTimeOffset(ApodService.APOD_MIN_DATE);
            DatePicker.MaxYear = new DateTimeOffset(DateTime.Today);
            _isSettingDate = true;
            DatePicker.SelectedDate = new DateTimeOffset(DateTime.Today);
            _isSettingDate = false;

            DatePicker.TemplateApplied += (_, e) =>
            {
                if (e.NameScope.Find("PART_Popup") is Popup popup)
                    popup.HorizontalOffset = 64;

                if (e.NameScope.Find("PART_ButtonContentGrid") is Grid grid)
                {
                    grid.ColumnDefinitions[0].Width = new GridLength(40, GridUnitType.Pixel);
                    grid.ColumnDefinitions[2].Width = new GridLength(40, GridUnitType.Pixel);
                    grid.ColumnDefinitions[4].Width = new GridLength(58, GridUnitType.Pixel);
                }
            };
        }
        
        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        private void WriteInfo(string info)
        {
            SetStatus(info);
        }

        private void WriteError(string error)
        {
            SetStatus($"ERROR: {error}");
        }

        private static bool IsMonthFirst =>
            CultureInfo.CurrentCulture.DateTimeFormat.MonthDayPattern
                .StartsWith("M", StringComparison.OrdinalIgnoreCase);

        private void SetDescriptionFromHtml(TextBlock textBlock, HtmlNode htmlNode)
        {
            var inlines = new InlineCollection();

            foreach (var node in htmlNode.ChildNodes)
            {
                if (node.Name == "#text")
                {
                    inlines.Add(new Run(node.InnerText.Replace("\n", " ")));
                }
                else if (node.Name == "b")
                {
                    inlines.Add(new Run { Text = node.InnerText.Replace("\n", " "), FontWeight = FontWeight.Bold });
                }
                else if (node.Name == "i")
                {
                    inlines.Add(new Run { Text = node.InnerText.Replace("\n", " "), FontStyle = FontStyle.Italic });
                }
                else if (node.Name == "a")
                {
                    Inline inline = CreateLinkInline(node);
                    if (inline != null) inlines.Add(inline);
                }
            }

            textBlock.Text = null;
            textBlock.Inlines = inlines;
        }

        private Inline CreateLinkInline(HtmlNode node)
        {
            string href = node.GetAttributeValue("href", "");
            string text = node.InnerText.Replace("\n", " ");
            if (string.IsNullOrEmpty(text)) return null;

            Uri uri = null;
            if (!Uri.TryCreate(href, UriKind.Absolute, out uri))
            {
                Uri.TryCreate(new Uri(ApodService.APOD_URL_BASE), href, out uri);
            }
            if (uri == null) return new Run(text);

            var link = new HyperlinkButton
            {
                Content = text,
                NavigateUri = uri,
                Classes = { "link" }
            };
            ToolTip.SetTip(link, uri);
            return new InlineUIContainer(link)
            {
                BaselineAlignment = BaselineAlignment.Bottom
            };
        }
        
        private void DownloadTodayImage()
        {
            LoadByDate(DateTime.Today);
        }

        private void UpdateControls(bool loading)
        {
            LoadingOverlay.IsVisible = loading;
            DownloadButton.IsEnabled = !loading && !_isVideo;
            SetWallpaperButton.IsEnabled = !loading && !_isVideo;
            DatePicker.IsEnabled = !loading;
            PrevDayButton.IsEnabled = !loading && _currentDate > ApodService.APOD_MIN_DATE;
            NextDayButton.IsEnabled = !loading && _currentDate < DateTime.Today;
        }

        private async void LoadByDate(DateTime date)
        {
            if (date < ApodService.APOD_MIN_DATE || date > DateTime.Today)
            {
                WriteError("Fecha fuera del rango de APOD (del 16/06/1995 a hoy).");
                return;
            }

            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            CancellationToken token = _loadCts.Token;

            _currentDate = date;
            _isSettingDate = true;
            DatePicker.SelectedDate = new DateTimeOffset(date);
            _isSettingDate = false;
            MoreInfoLink.NavigateUri = new Uri(Program.Service.GetAPODPageURL(date));

            _isVideo = false;
            fullResUrl = null;
            UpdateControls(true);
            PreviewImage.Source = null;
            PreviewImage.IsVisible = true;
            VideoWebView.IsVisible = false;
            TitleText.Text = "";
            InfoText.Text = "";
            WriteInfo($"Descargando contenido del {date:dd/MM/yyyy}...");

            try
            {
                using var client = new HttpClient();
                string doc_url = Program.Service.GetAPODPageURL(date);
                if (!Program.Service.IsValidURL(doc_url)) return;

                HtmlDocument page = await Program.Service.GetHTMLDocument(client, doc_url, token);
                token.ThrowIfCancellationRequested();

                SetDescriptionFromHtml(TitleText, Program.Service.GetImageTitleFromAPOD(page));
                SetDescriptionFromHtml(InfoText, Program.Service.GetImageDescriptionFromAPOD(page));

                if (Program.Service.HasVideo(page))
                    await LoadVideoAsync(client, page, token);
                else
                    await LoadImageAsync(client, page, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                WriteError(e.Message);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    UpdateControls(false);
                }
            }
        }

        private async Task LoadVideoAsync(HttpClient client, HtmlDocument page, CancellationToken token)
        {
            _isVideo = true;

            string video_url = Program.Service.GetVideoUrl(page);
            string thumbnail_url = Program.Service.GetVideoThumbnailUrl(page);
            fullResUrl = null;
            file_name = Program.Service.GetImagefileNameFromURL(video_url);

            if (Program.Service.IsValidURL(thumbnail_url))
            {
                image = await Program.DownloadImageParallel(client, thumbnail_url, token);
                token.ThrowIfCancellationRequested();
                if (image != null)
                    PreviewImage.Source = image;
                PreviewImage.IsVisible = true;
            }
            else
            {
                PreviewImage.IsVisible = false;
            }

            string html =
                "<!DOCTYPE html><html><head><meta charset='utf-8'>" +
                "<style>html,body{margin:0;padding:0;width:100%;height:100%;background:#000;}" +
                "video{width:100%;height:100%;object-fit:contain;}</style></head><body>" +
                $"<video src='{video_url}' controls autoplay></video></body></html>";

            VideoWebView.NavigateToString(html);
            VideoWebView.IsVisible = true;

            WriteInfo("Reproduciendo vídeo de la fecha seleccionada.");
        }

        private async Task LoadImageAsync(HttpClient client, HtmlDocument page, CancellationToken token)
        {
            string image_url = ApodService.APOD_URL_BASE + Program.Service.GetImageURLFromAPOD(page);
            string preview_url = ApodService.APOD_URL_BASE + Program.Service.GetImagePreviewURLFromAPOD(page);
            if (!Program.Service.IsValidURL(image_url) || !Program.Service.IsValidURL(preview_url)) return;

            image = await Program.DownloadImageParallel(client, preview_url, token);
            token.ThrowIfCancellationRequested();
            fullResUrl = image_url;
            file_name = Program.Service.GetImagefileNameFromURL(image_url);
            if (image != null)
            {
                PreviewImage.Source = image;
                WriteInfo("Listo");
            }
        }

        private void DatePicker_SelectedDateChanged(object sender, DatePickerSelectedValueChangedEventArgs e)
        {
            if (_isSettingDate) return;
            if (e.NewDate.HasValue)
            {
                LoadByDate(e.NewDate.Value.Date);
            }
        }

        private void PrevDayButton_Click(object sender, RoutedEventArgs e)
        {
            LoadByDate(_currentDate.AddDays(-1));
        }

        private void NextDayButton_Click(object sender, RoutedEventArgs e)
        {
            LoadByDate(_currentDate.AddDays(1));
        }
        
        private async Task<bool> SaveImageAsync()
        {
            if (image == null) return false;

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Image",
                SuggestedFileName = file_name,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(ApodService.DefaultDownloadDir),
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Image Files") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png" } }
                }
            });

            if (file != null)
            {
                var path = file.TryGetLocalPath();
                if (path == null)
                {
                    WriteError("No se pudo obtener la ruta del archivo.");
                    return false;
                }

                if (string.IsNullOrEmpty(fullResUrl))
                {
                    WriteError("No hay imagen de resolución completa para guardar.");
                    return false;
                }

                file_name = path;
                UpdateControls(true);
                try
                {
                    WriteInfo("Descargando la imagen...");
                    using var client = new HttpClient();
                    byte[] bytes = await Program.Service.DownloadImageParallelToBytes(client, fullResUrl);
                    File.WriteAllBytes(file_name, bytes);
                    WriteInfo($"Imagen guardada en {file_name}");
                }
                catch (Exception e)
                {
                    WriteError("Error al guardar la imagen: " + e.Message);
                }
                finally
                {
                    UpdateControls(false);
                }

                return true;
            }
            return false;
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveImageAsync();
        }

        private async void SetWallpaperButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(file_name))
            {
                await SaveImageAsync();
                if (!File.Exists(file_name))
                {
                    WriteError("La imagen no está guardada en el disco.");
                    return;
                }
            }
            Program.Service.SetWallpaper(file_name);
            WriteInfo($"Fondo de pantalla establecido");
        }
    }
}
