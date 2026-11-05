using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Menage.App.Tema;

namespace Menage.App.Controles;

/// <summary>
/// Recolhe/expande a barra lateral sem layout por quadro (medido na prova de conceito: 3 passes por ciclo).
/// A barra tem sempre a largura máxima, dentro de um Canvas, e quem anima é só o RenderTransform do conteúdo,
/// que precisa ter fundo opaco e ficar por cima. A coluna do Grid muda de largura uma vez no início e uma no fim.
/// </summary>
public sealed class AnimadorBarraLateral
{
    readonly ColumnDefinition _coluna;
    readonly double _recolhida, _expandida;
    readonly TranslateTransform _deslocamento = new();

    public AnimadorBarraLateral(Canvas hospedeiro, FrameworkElement barra, FrameworkElement conteudo,
        ColumnDefinition coluna, double larguraRecolhida, double larguraExpandida)
    {
        (_coluna, _recolhida, _expandida) = (coluna, larguraRecolhida, larguraExpandida);
        barra.Width = _expandida;
        Canvas.SetLeft(barra, 0);
        hospedeiro.SizeChanged += (_, e) => barra.Height = e.NewSize.Height;
        conteudo.RenderTransform = _deslocamento;
        coluna.Width = new GridLength(_expandida);
    }

    public bool Expandida { get; private set; } = true;

    public bool Animando { get; private set; }

    /// <summary>Estado inicial sem animar (preferência salva): ajusta a coluna e o estado de uma vez.</summary>
    public void DefinirSemAnimar(bool expandida)
    {
        Expandida = expandida;
        _coluna.Width = new GridLength(expandida ? _expandida : _recolhida);
    }

    public void Alternar()
    {
        if (Animando) return;
        var expandir = !Expandida;
        var distancia = _expandida - _recolhida;
        _coluna.Width = new GridLength(_recolhida);
        Animando = true;

        var deslize = new DoubleAnimation(expandir ? 0 : distancia, expandir ? distancia : 0, Animacao.Movimento(Animacao.Media)) { EasingFunction = Animacao.Suave };
        deslize.Completed += (_, _) => Terminar(expandir);
        _deslocamento.BeginAnimation(TranslateTransform.XProperty, deslize);
    }

    void Terminar(bool expandir)
    {
        _deslocamento.BeginAnimation(TranslateTransform.XProperty, null);
        _deslocamento.X = 0;
        _coluna.Width = new GridLength(expandir ? _expandida : _recolhida);
        Expandida = expandir;
        Animando = false;
    }
}
