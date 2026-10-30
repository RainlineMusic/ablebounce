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
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var path = Path.GetFullPath(e.Args[capture + 1]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = File.Create(path)) encoder.Save(stream);
                Shutdown();
            };
    }
}
