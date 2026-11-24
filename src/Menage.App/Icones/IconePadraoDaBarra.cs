using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Menage.App.Icones;

/// <summary>
/// O "M" padrão desenhado na cor de destaque do tema, para a barra de tarefas acompanhar o tema e a cor escolhida.
/// Uma imagem por cor, guardada para não redesenhar a cada janela.
/// </summary>
public static class IconePadraoDaBarra
{
    const int Tamanho = 256;
    static readonly Dictionary<Color, ImageSource> Cache = [];

    public static ImageSource Criar(Color destaque)
    {
        if (Cache.TryGetValue(destaque, out var pronto)) return pronto;

        var luminancia = (0.299 * destaque.R + 0.587 * destaque.G + 0.114 * destaque.B) / 255;
        var texto = luminancia > 0.6 ? Brushes.Black : Brushes.White;
        var letra = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            Tamanho * 0.58, texto, 1.0);

        var visual = new DrawingVisual();
        using (var desenho = visual.RenderOpen())
        {
            desenho.DrawRoundedRectangle(new SolidColorBrush(destaque), null, new Rect(0, 0, Tamanho, Tamanho), Tamanho * 0.27, Tamanho * 0.27);
            desenho.DrawText(letra, new Point((Tamanho - letra.Width) / 2, (Tamanho - letra.Height) / 2));
        }

        var imagem = new RenderTargetBitmap(Tamanho, Tamanho, 96, 96, PixelFormats.Pbgra32);
        imagem.Render(visual);
        imagem.Freeze();
        Cache[destaque] = imagem;
        return imagem;
    }
}
