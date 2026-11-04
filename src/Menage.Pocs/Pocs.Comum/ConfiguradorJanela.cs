using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

namespace Menage.Pocs.Comum;

/// <summary>
/// Janela sem barra de título nativa via WindowChrome: bordas de redimensionar, encaixe (Aero Snap) e
/// Win+setas continuam do Windows. Ao maximizar, o conteúdo é recuado para não vazar da tela.
/// </summary>
public sealed class ConfiguradorJanela
{
    public void Aplicar(Window janela, Border raiz, double alturaBarra = 40)
    {
        WindowChrome.SetWindowChrome(janela, new WindowChrome
        {
            CaptionHeight = alturaBarra,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        janela.StateChanged += (_, _) => Recuar(janela, raiz);
        Recuar(janela, raiz);
    }

    private static void Recuar(Window janela, Border raiz) =>
        raiz.Padding = janela.WindowState == WindowState.Maximized
            ? new Thickness(SystemParameters.WindowResizeBorderThickness.Left + SystemParameters.FixedFrameVerticalBorderWidth + 1)
            : new Thickness(0);
}
