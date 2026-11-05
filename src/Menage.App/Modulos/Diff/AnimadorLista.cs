using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Menage.App.Modulos.Diff;

/// <summary>
/// Recolhe/expande a lista sem layout por quadro: ela tem sempre a largura cheia dentro de um Canvas (que não
/// recorta o filho) e quem anima é só o RenderTransform do conteúdo, de fundo opaco, que cobre ou descobre a lista.
/// A coluna do Grid muda de largura uma vez no início e uma no fim.
/// </summary>
public sealed class AnimadorLista
{
    readonly ColumnDefinition _coluna;
    readonly double _min;
    readonly double _max;
    readonly TranslateTransform _deslocamento = new();
    readonly TimeSpan _duracao = TimeSpan.FromMilliseconds(220);

    public AnimadorLista(Canvas hospedeiro, FrameworkElement lista, FrameworkElement conteudo, ColumnDefinition coluna, double recolhida, double expandida)
    {
        (_coluna, _min, _max) = (coluna, recolhida, expandida);
        lista.Width = _max;
        Canvas.SetLeft(lista, 0);
        hospedeiro.SizeChanged += (_, e) => lista.Height = e.NewSize.Height;
        conteudo.RenderTransform = _deslocamento;
        coluna.Width = new GridLength(_max);
    }

    public event Action<bool>? Terminou;

    public bool Expandida { get; private set; } = true;

    public bool Animando { get; private set; }

    public void Alternar()
    {
        if (Animando) return;
        var expandir = !Expandida;
        var distancia = _max - _min;
        _coluna.Width = new GridLength(_min); // layout único no início
        Animando = true;
        var animacao = new DoubleAnimation(expandir ? 0 : distancia, expandir ? distancia : 0, _duracao)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        animacao.Completed += (_, _) => Fechar(expandir);
        _deslocamento.BeginAnimation(TranslateTransform.XProperty, animacao);
    }

    void Fechar(bool expandir)
    {
        _deslocamento.BeginAnimation(TranslateTransform.XProperty, null);
        _deslocamento.X = 0;
        _coluna.Width = new GridLength(expandir ? _max : _min);
        Expandida = expandir;
        Animando = false;
        Terminou?.Invoke(expandir);
    }
}
