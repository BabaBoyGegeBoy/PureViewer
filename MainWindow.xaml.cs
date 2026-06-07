using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PureViewer
{
    // Thumbnail item for grid view
    public class ThumbItem
    {
        public BitmapImage? Thumbnail { get; set; }
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
    }

    public partial class MainWindow : Window
    {
        private List<string> _allImageFiles = [];
        private List<string> _imageFiles = [];
        private int _currentIndex = -1;

        // GIF animation
        private List<BitmapFrame>? _gifFrames;
        private int _gifFrameIndex;
        private readonly DispatcherTimer _gifTimer;

        private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".ico"];

        // Fullscreen restore state
        private WindowStyle _prevStyle;
        private WindowState _prevState;
        private ResizeMode _prevResize;
        private double _prevWidth, _prevHeight;

        // Author data
        private string _rootPath = "";
        private readonly Dictionary<string, List<string>> _authorImages = [];
        private string? _selectedAuthor;

        // Tag data
        private readonly Dictionary<string, List<string>> _tagImages = [];
        private string? _selectedTag;

        // Zoom / Pan
        private double _zoomLevel = 1.0;
        private const double ZoomStep = 0.2;
        private const double ZoomMin = 0.2;
        private const double ZoomMax = 8.0;
        private bool _isDragging;
        private System.Windows.Point _dragStart;

        // View mode
        private bool _isGridView;

        public MainWindow()
        {
            InitializeComponent();
            _gifTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _gifTimer.Tick += OnGifFrameTick;

            SearchBox.Text = (string)SearchBox.Tag;
        }

        // ── Folder Selection ──────────────────────────────────

        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                ShowNewFolderButton = false,
                Description = "选择包含图片的文件夹"
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                LoadFolder(dialog.SelectedPath);
        }

        private void LoadFolder(string path)
        {
            _rootPath = path;
            _selectedAuthor = null;
            _selectedTag = null;

            var searchOption = IncludeSubfolders.IsChecked == true
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            _allImageFiles = Directory.GetFiles(path, "*.*", searchOption)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Parse authors from subfolder names
            _authorImages.Clear();
            foreach (var file in _allImageFiles)
            {
                var rel = Path.GetRelativePath(path, file);
                var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Length > 1)
                {
                    var authorName = ExtractAuthorName(parts[0]);
                    if (!_authorImages.ContainsKey(authorName))
                        _authorImages[authorName] = [];
                    _authorImages[authorName].Add(file);
                }
                else
                {
                    const string rootLabel = "（根目录）";
                    if (!_authorImages.ContainsKey(rootLabel))
                        _authorImages[rootLabel] = [];
                    _authorImages[rootLabel].Add(file);
                }
            }

            // Parse tags
            ParseTags(path, searchOption);

            // Populate author sidebar
            AuthorList.Items.Clear();
            AuthorList.Items.Add("全部");
            foreach (var author in _authorImages.Keys.OrderBy(a => a, StringComparer.OrdinalIgnoreCase))
                AuthorList.Items.Add(author);
            AuthorList.SelectedIndex = 0;

            AuthorSidebar.Visibility = _authorImages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            // Populate tag filter
            TagFilter.Items.Clear();
            TagFilter.Items.Add("全部标签");
            foreach (var tag in _tagImages.Keys.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                TagFilter.Items.Add($"{tag} ({_tagImages[tag].Count})");
            TagFilter.SelectedIndex = 0;

            FolderText.Text = _allImageFiles.Count > 0
                ? $"共 {_allImageFiles.Count} 张图片{(IncludeSubfolders.IsChecked == true ? "（含子文件夹）" : "")} - {path}"
                : $"未找到图片 - {path}";

            ApplyFilter();
        }

        private static string ExtractAuthorName(string dirName)
        {
            var lastUnderscore = dirName.LastIndexOf('_');
            if (lastUnderscore > 0 && int.TryParse(dirName[(lastUnderscore + 1)..], out _))
                return dirName[..lastUnderscore];
            return dirName;
        }

        // ── Tag Parsing ────────────────────────────────────────

        private void ParseTags(string rootPath, SearchOption option)
        {
            _tagImages.Clear();
            var jsonFiles = Directory.GetFiles(rootPath, "info.json", option);
            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    var json = File.ReadAllText(jsonFile);
                    var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("tags", out var tagsEl)) continue;

                    var dir = Path.GetDirectoryName(jsonFile)!;
                    var images = Directory.GetFiles(dir)
                        .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToList();

                    foreach (var tagEl in tagsEl.EnumerateArray())
                    {
                        var tag = tagEl.GetString() ?? "";
                        if (string.IsNullOrEmpty(tag)) continue;
                        if (!_tagImages.ContainsKey(tag))
                            _tagImages[tag] = [];
                        _tagImages[tag].AddRange(images);
                    }
                }
                catch { }
            }

            foreach (var tag in _tagImages.Keys.ToList())
                _tagImages[tag] = _tagImages[tag].Distinct().ToList();
        }

        // ── Filtering ──────────────────────────────────────────

        private void ApplyFilter()
        {
            IEnumerable<string> filtered = _allImageFiles;

            // Author filter
            if (_selectedAuthor != null && _authorImages.TryGetValue(_selectedAuthor, out var authorFiles))
                filtered = filtered.Intersect(authorFiles);

            // Tag filter
            if (_selectedTag != null && _tagImages.TryGetValue(_selectedTag, out var tagFiles))
                filtered = filtered.Intersect(tagFiles);

            // Search filter
            var keyword = GetSearchText();
            if (!string.IsNullOrEmpty(keyword))
                filtered = filtered.Where(f =>
                    Path.GetFileName(f).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || Path.GetDirectoryName(f)?.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(p => p.Contains(keyword, StringComparison.OrdinalIgnoreCase)) == true);

            _imageFiles = filtered.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

            FolderText.Text = _allImageFiles.Count > 0
                ? $"共 {_imageFiles.Count}/{_allImageFiles.Count} 张{(IncludeSubfolders.IsChecked == true ? "（含子文件夹）" : "")} - {_rootPath}"
                : $"未找到图片 - {_rootPath}";

            if (_imageFiles.Count > 0)
            {
                _currentIndex = 0;
                ResetZoom();
                ShowImage();
                RefreshThumbnails();
            }
            else
            {
                ImageViewer.Source = null;
                LoadingText.Visibility = Visibility.Collapsed;
                _currentIndex = -1;
                Title = "PureViewer";
                ThumbGrid.ItemsSource = null;
            }
        }

        private string GetSearchText()
        {
            var text = SearchBox.Text;
            var placeholder = (string)SearchBox.Tag;
            return text == placeholder ? "" : text.Trim();
        }

        // ── Author Sidebar ─────────────────────────────────────

        private void AuthorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AuthorList.SelectedItem is string selected)
            {
                _selectedAuthor = selected == "全部" ? null : selected;
                ApplyFilter();
            }
        }

        // ── Tag Filter ─────────────────────────────────────────

        private void TagFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TagFilter.SelectedItem is string selected)
            {
                _selectedTag = selected == "全部标签" ? null : selected.Split(" (")[0];
                ApplyFilter();
            }
        }

        // ── Search ─────────────────────────────────────────────

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchBox.IsFocused || string.IsNullOrEmpty(SearchBox.Text))
                ApplyFilter();
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == (string)SearchBox.Tag)
                SearchBox.Text = "";
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
                SearchBox.Text = (string)SearchBox.Tag;
        }

        // ── Image Display ─────────────────────────────────────

        private void ShowImage()
        {
            if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count) return;

            StopGif();

            var path = _imageFiles[_currentIndex];
            Title = $"PureViewer - {_currentIndex + 1}/{_imageFiles.Count} - {Path.GetFileName(path)}";

            try
            {
                if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    LoadGif(path);
                else
                    LoadStaticImage(path);
            }
            catch
            {
                ImageViewer.Source = null;
                LoadingText.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadStaticImage(string path)
        {
            LoadingText.Visibility = Visibility.Visible;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            if (bmp.IsDownloading)
            {
                bmp.DownloadCompleted += (_, _) => LoadingText.Visibility = Visibility.Collapsed;
                bmp.DownloadFailed += (_, _) => LoadingText.Visibility = Visibility.Collapsed;
            }
            else
            {
                LoadingText.Visibility = Visibility.Collapsed;
            }

            ImageViewer.Source = bmp;
        }

        // ── GIF Animation ─────────────────────────────────────

        private void LoadGif(string path)
        {
            LoadingText.Visibility = Visibility.Visible;

            var decoder = new GifBitmapDecoder(
                new Uri(path),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.Default);

            _gifFrames = decoder.Frames.ToList();
            _gifFrameIndex = 0;

            if (_gifFrames.Count > 0)
            {
                LoadingText.Visibility = Visibility.Collapsed;
                ImageViewer.Source = _gifFrames[0];
                if (_gifFrames.Count > 1)
                {
                    _gifTimer.Interval = GetFrameDelay(_gifFrames[0]);
                    _gifTimer.Start();
                }
            }
            else
            {
                LoadingText.Visibility = Visibility.Collapsed;
            }
        }

        private void OnGifFrameTick(object? sender, EventArgs e)
        {
            if (_gifFrames == null || _gifFrames.Count == 0) return;

            _gifFrameIndex = (_gifFrameIndex + 1) % _gifFrames.Count;
            ImageViewer.Source = _gifFrames[_gifFrameIndex];
            _gifTimer.Interval = GetFrameDelay(_gifFrames[_gifFrameIndex]);
        }

        private void StopGif()
        {
            _gifTimer.Stop();
            _gifFrames = null;
        }

        private static TimeSpan GetFrameDelay(BitmapFrame frame)
        {
            int delayMs = 100;
            try
            {
                if (frame.Metadata is BitmapMetadata meta)
                {
                    var delay = meta.GetQuery("/grctlext/Delay") as ushort?;
                    if (delay.HasValue && delay.Value > 0)
                        delayMs = delay.Value * 10;
                }
            }
            catch { }
            return TimeSpan.FromMilliseconds(Math.Max(delayMs, 20));
        }

        // ── Navigation ────────────────────────────────────────

        private void Prev_Click(object sender, RoutedEventArgs e) => Navigate(-1);
        private void Next_Click(object sender, RoutedEventArgs e) => Navigate(1);

        private void Navigate(int offset)
        {
            if (_imageFiles.Count == 0) return;
            _currentIndex = (_currentIndex + offset + _imageFiles.Count) % _imageFiles.Count;
            ResetZoom();
            ShowImage();
        }

        // ── Zoom / Pan ────────────────────────────────────────

        private void ImageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Zoom
                if (e.Delta > 0) ZoomIn();
                else ZoomOut();
                e.Handled = true;
            }
            else
            {
                // Navigate
                if (e.Delta > 0) Navigate(-1);
                else Navigate(1);
                e.Handled = true;
            }
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Fallback: if mouse is not over the ScrollViewer
            if (Keyboard.Modifiers == ModifierKeys.Control) return; // handled by ScrollViewer
            if (e.Delta > 0) Navigate(-1);
            else if (e.Delta < 0) Navigate(1);
        }

        private void ZoomIn()
        {
            _zoomLevel = Math.Min(_zoomLevel + ZoomStep, ZoomMax);
            ApplyZoom();
        }

        private void ZoomOut()
        {
            _zoomLevel = Math.Max(_zoomLevel - ZoomStep, ZoomMin);
            ApplyZoom();
        }

        private void ResetZoom()
        {
            _zoomLevel = 1.0;
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            ImageScale.ScaleX = _zoomLevel;
            ImageScale.ScaleY = _zoomLevel;
            ZoomText.Text = $"{(int)(_zoomLevel * 100)}%";

            // Hide scrollbars when at 1x (fit mode)
            if (_zoomLevel <= 1.0)
            {
                ImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                ImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            else
            {
                ImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                ImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
        }

        // ── Drag Pan ───────────────────────────────────────────

        private void ImageViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_zoomLevel <= 1.0) return;
            _isDragging = true;
            _dragStart = e.GetPosition(ImageScroll);
            ImageViewer.CaptureMouse();
            e.Handled = true;
        }

        private void ImageViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ImageViewer.ReleaseMouseCapture();
        }

        private void ImageViewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var pos = e.GetPosition(ImageScroll);
            var dx = _dragStart.X - pos.X;
            var dy = _dragStart.Y - pos.Y;
            ImageScroll.ScrollToHorizontalOffset(ImageScroll.HorizontalOffset + dx);
            ImageScroll.ScrollToVerticalOffset(ImageScroll.VerticalOffset + dy);
            _dragStart = pos;
        }

        // ── View Toggle ────────────────────────────────────────

        private void ToggleView_Click(object sender, RoutedEventArgs e) => ToggleView();

        private void ToggleView()
        {
            _isGridView = !_isGridView;
            if (_isGridView)
            {
                ThumbGrid.Visibility = Visibility.Visible;
                ImageContainer.Visibility = Visibility.Collapsed;
                ToggleViewBtn.Content = "大图 (G)";
                RefreshThumbnails();
            }
            else
            {
                ThumbGrid.Visibility = Visibility.Collapsed;
                ImageContainer.Visibility = Visibility.Visible;
                ToggleViewBtn.Content = "缩略图 (G)";
            }
        }

        private void RefreshThumbnails()
        {
            if (!_isGridView) return;
            var items = new List<ThumbItem>();
            foreach (var file in _imageFiles)
            {
                try
                {
                    var thumb = new BitmapImage();
                    thumb.BeginInit();
                    thumb.UriSource = new Uri(file);
                    thumb.DecodePixelWidth = 120;
                    thumb.CacheOption = BitmapCacheOption.OnLoad;
                    thumb.EndInit();
                    items.Add(new ThumbItem { Thumbnail = thumb, FileName = Path.GetFileName(file), FilePath = file });
                }
                catch
                {
                    items.Add(new ThumbItem { FileName = Path.GetFileName(file), FilePath = file });
                }
            }
            ThumbGrid.ItemsSource = items;
        }

        private void ThumbGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThumbGrid.SelectedItem is ThumbItem item)
            {
                var idx = _imageFiles.IndexOf(item.FilePath);
                if (idx >= 0)
                {
                    _currentIndex = idx;
                    // Switch to reading view
                    _isGridView = true;
                    ToggleView();  // toggle back to reading view
                    ShowImage();
                }
            }
        }

        // ── Fullscreen ────────────────────────────────────────

        private void Fullscreen_Click(object sender, RoutedEventArgs e) => EnterFullscreen();
        private void ExitFullscreen_Click(object sender, RoutedEventArgs e) => ExitFullscreen();

        private void EnterFullscreen()
        {
            _prevStyle = WindowStyle;
            _prevState = WindowState;
            _prevResize = ResizeMode;
            _prevWidth = Width;
            _prevHeight = Height;

            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
        }

        private void ExitFullscreen()
        {
            WindowStyle = _prevStyle;
            WindowState = _prevState;
            ResizeMode = _prevResize;
            Width = _prevWidth;
            Height = _prevHeight;
        }

        private bool IsFullscreen => WindowStyle == WindowStyle.None;

        // ── Keyboard Shortcuts ────────────────────────────────

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (SearchBox.IsFocused) return;

            switch (e.Key)
            {
                case Key.Left:
                case Key.A:
                    Navigate(-1);
                    break;
                case Key.Right:
                case Key.D:
                    Navigate(1);
                    break;
                case Key.F11:
                case Key.F:
                    if (IsFullscreen) ExitFullscreen();
                    else EnterFullscreen();
                    break;
                case Key.Escape:
                    if (IsFullscreen) ExitFullscreen();
                    break;
                case Key.G:
                    ToggleView();
                    break;
                case Key.OemPlus:
                case Key.Add:
                    if (Keyboard.Modifiers == ModifierKeys.Control) { ZoomIn(); e.Handled = true; }
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    if (Keyboard.Modifiers == ModifierKeys.Control) { ZoomOut(); e.Handled = true; }
                    break;
                case Key.D0:
                    if (Keyboard.Modifiers == ModifierKeys.Control) { ResetZoom(); e.Handled = true; }
                    break;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            StopGif();
            base.OnClosed(e);
        }
    }
}
