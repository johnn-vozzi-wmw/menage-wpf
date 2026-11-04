using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Menage.Pocs.Comum;

/// <summary>
/// Recolhe/expande a barra lateral sem layout por quadro. A barra tem sempre a largura máxima (dentro
/// de um Canvas, que não recorta o filho) e quem anima é só o RenderTransform do conteúdo, que precisa ter
/// fundo opaco e ficar por cima: ele "descobre" ou "cobre" a barra. A coluna do Grid muda de largura
/// uma vez no início e uma no fim. (Animar o Clip do Canvas custou 1 passe de layout por quadro; medido.)
/// </summary>
public sealed class AnimadorBarraLateral
{
    private readonly ColumnDefinition _coluna;
    private readonly double _min, _max;
    private readonly TranslateTransform _desloc = new();
    private readonly TimeSpan _duracao = TimeSpan.FromMilliseconds(220);

    public bool Expandida { get; private set; } = true;
    public bool Animando { get; private set; }

    public AnimadorBarraLateral(Canvas hospedeiro, FrameworkElement barra, FrameworkElement conteudo,
        ColumnDefinition coluna, double larguraRecolhida, double larguraExpandida)
    {
        (_coluna, _min, _max) = (coluna, larguraRecolhida, larguraExpandida);
        barra.Width = _max;
        Canvas.SetLeft(barra, 0);
        hospedeiro.SizeChanged += (_, e) => barra.Height = e.NewSize.Height;
        conteudo.RenderTransform = _desloc;
        coluna.Width = new GridLength(_max);
    }

    public void Alternar()
    {
        if (Animando) return;
        var expandir = !Expandida;
        var dx = _max - _min;
        _coluna.Width = new GridLength(_min); // layout único no início
        Animando = true;

        var desloc = new DoubleAnimation(expandir ? 0 : dx, expandir ? dx : 0, _duracao)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        desloc.Completed += (_, _) => Terminar(expandir);
        _desloc.BeginAnimation(TranslateTransform.XProperty, desloc);
    }

    private void Terminar(bool expandir)
    {
        _desloc.BeginAnimation(TranslateTransform.XProperty, null);
        _desloc.X = 0;
        _coluna.Width = new GridLength(expandir ? _max : _min);
        Expandida = expandir;
        Animando = false;
    }
}
