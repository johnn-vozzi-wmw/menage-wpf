using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Menage.Pocs.Comum;

public sealed class Capturador
{
    public void Salvar(FrameworkElement elemento, string caminho)
    {
        var dpi = VisualTreeHelper.GetDpi(elemento);
        var w = (int)Math.Ceiling(elemento.ActualWidth * dpi.DpiScaleX);
        var h = (int)Math.Ceiling(elemento.ActualHeight * dpi.DpiScaleY);
        var rtb = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(elemento);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        using var fs = File.Create(caminho);
        enc.Save(fs);
    }
}
