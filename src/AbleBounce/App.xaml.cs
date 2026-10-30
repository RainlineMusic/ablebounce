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
        window.Show();
        int capture = Array.IndexOf(e.Args, "--screenshot");
        if (capture >= 0 && capture + 1 < e.Args.Length)
            window.ContentRendered += async (_, _) =>
            {
                await Task.Delay(500);
                // Hosted runners can have a smaller work area than the target
                // viewport. Render the actual root visual at an explicit size,
                // excluding nonclient chrome and any offscreen black edge.
                var root = (FrameworkElement)window.Content;
                int sizeIndex = Array.IndexOf(e.Args, "--preview-size");
                var size = sizeIndex >= 0 && sizeIndex + 1 < e.Args.Length ? e.Args[sizeIndex + 1].Split('x') : ["1280", "860"];
                int width = int.Parse(size[0]), height = int.Parse(size[1]);
                root.Width = width; root.Height = height;
                root.Measure(new Size(width, height));
                root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var path = Path.GetFullPath(e.Args[capture + 1]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = File.Create(path)) encoder.Save(stream);
                Shutdown();
            };
    }
}
