using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AbleBounce;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) =>
        {
            MessageBox.Show(a.Exception.Message, "AbleBounce", MessageBoxButton.OK, MessageBoxImage.Error);
            a.Handled = true;
        };
        var window = new MainWindow(e.Args.Contains("--demo"));
        MainWindow = window;
        int capture = Array.IndexOf(e.Args, "--screenshot");
        if (capture >= 0 && capture + 1 < e.Args.Length)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = Capture(window, e.Args, capture);
        }
        else window.Show();
    }
    private async Task Capture(MainWindow window, string[] args, int capture)
    {
        try
        {
            // Measure an unhosted root, before Show: the runner's 1024px desktop
            // must not impose the Window's old clip on a larger design capture.
            var root = (FrameworkElement)window.Content;
            root.DataContext = window.DataContext;
            root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, window.Foreground);
            root.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, window.FontFamily);
            root.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, window.FontSize);
            window.Content = null;
            int sizeIndex = Array.IndexOf(args, "--preview-size");
            var size = sizeIndex >= 0 && sizeIndex + 1 < args.Length ? args[sizeIndex + 1].Split('x') : ["1280", "860"];
            int width = int.Parse(size[0]), height = int.Parse(size[1]);
            root.Width = Math.Max(0, width - root.Margin.Left - root.Margin.Right);
            root.Height = Math.Max(0, height - root.Margin.Top - root.Margin.Bottom);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.GetFullPath(args[capture + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = File.Create(path)) encoder.Save(stream);
            Shutdown();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.GetFullPath(args[capture + 1]) + ".error.txt", ex.ToString());
            Shutdown(1);
        }
    }
}
