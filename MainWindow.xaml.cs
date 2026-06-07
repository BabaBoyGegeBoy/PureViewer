using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PureViewer
{
    public partial class MainWindow : Window
    {
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

        public MainWindow()
        {
            InitializeComponent();
            _gifTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _gifTimer.Tick += OnGifFrameTick;
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
            var searchOption = IncludeSubfolders.IsChecked == true
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            _imageFiles = Directory.GetFiles(path, "*.*", searchOption)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            FolderText.Text = _imageFiles.Count > 0
                ? $"共 {_imageFiles.Count} 张图片{(IncludeSubfolders.IsChecked == true ? "（含子文件夹）" : "")} - {path}"
                : $"未找到图片 - {path}";

            if (_imageFiles.Count > 0)
            {
                _currentIndex = 0;
                ShowImage();
            }
            else
            {
                ImageViewer.Source = null;
                _currentIndex = -1;
                Title = "PureViewer";
            }
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
            }
        }

        private void LoadStaticImage(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            ImageViewer.Source = bmp;
        }

        // ── GIF Animation ─────────────────────────────────────

        private void LoadGif(string path)
        {
            var decoder = new GifBitmapDecoder(
                new Uri(path),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.Default);

            _gifFrames = decoder.Frames.ToList();
            _gifFrameIndex = 0;

            if (_gifFrames.Count > 0)
            {
                ImageViewer.Source = _gifFrames[0];
                if (_gifFrames.Count > 1)
                {
                    _gifTimer.Interval = GetFrameDelay(_gifFrames[0]);
                    _gifTimer.Start();
                }
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
            ShowImage();
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
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            StopGif();
            base.OnClosed(e);
        }
    }
}
