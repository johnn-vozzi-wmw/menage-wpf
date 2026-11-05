using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Menage.App.Tema;

namespace Menage.App.Controles;

/// <summary>
/// Painel responsivo de pílulas (regra em <see cref="CalculoDaBarra"/>). Quando o modo ou a linha de uma pílula
/// muda, ela é colocada de vez no lugar novo e desliza até lá só com <c>RenderTransform</c>: um passe de
/// layout por mudança, nenhum por quadro. Redimensionar sem mudar de modo não anima (seguiria o mouse com atraso).
/// </summary>
public sealed class BarraDePilulas : Panel
{
    static readonly DependencyPropertyKey ModoKey = DependencyProperty.RegisterReadOnly(
        nameof(Modo), typeof(ModoDaBarra), typeof(BarraDePilulas), new PropertyMetadata(ModoDaBarra.Capsula));

    public static readonly DependencyProperty ModoProperty = ModoKey.DependencyProperty;

    public static readonly DependencyProperty EspacoProperty = DependencyProperty.Register(
        nameof(Espaco), typeof(double), typeof(BarraDePilulas),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    readonly Dictionary<UIElement, (double Completa, double Compacta, double Altura)> _medidas = [];
    readonly Dictionary<UIElement, (Point Posicao, int Linha)> _anteriores = [];
    LayoutDaBarra? _layout;
    double _altura;
    bool _mudouDeModo;

    public ModoDaBarra Modo => (ModoDaBarra)GetValue(ModoProperty);

    public double Espaco
    {
        get => (double)GetValue(EspacoProperty);
        set => SetValue(EspacoProperty, value);
    }

    /// <summary>Texto de uma pílula mudou: as larguras guardadas não valem mais.</summary>
    public void EsquecerLarguras()
    {
        _medidas.Clear();
        InvalidateMeasure();
    }

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        _medidas.Clear();
        if (visualRemoved is UIElement removido) _anteriores.Remove(removido);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var filhos = InternalChildren.Cast<UIElement>().ToList();
        foreach (var filho in filhos.Where(f => !_medidas.ContainsKey(f))) _medidas[filho] = MedirNatural(filho);

        _altura = filhos.Count == 0 ? 0 : filhos.Max(f => _medidas[f].Altura);
        var layout = CalculoDaBarra.Calcular(
            [.. filhos.Select(f => _medidas[f].Completa)], [.. filhos.Select(f => _medidas[f].Compacta)],
            availableSize.Width, _altura, Espaco);

        _mudouDeModo |= _layout is not null && _layout.Modo != layout.Modo;
        _layout = layout;
        SetValue(ModoKey, layout.Modo);
        for (var i = 0; i < filhos.Count; i++)
        {
            if (filhos[i] is Pilula pilula) pilula.Compacta = layout.Modo != ModoDaBarra.Capsula;
            filhos[i].Measure(new Size(layout.Posicoes[i].Largura, _altura));
        }

        return new Size(layout.Largura, layout.Altura);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_layout is null) return finalSize;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var filho = InternalChildren[i];
            var posicao = _layout.Posicoes[i];
            var novo = new Point(posicao.X, posicao.Y);
            filho.Arrange(new Rect(novo, new Size(posicao.Largura, _altura)));
            if (_anteriores.TryGetValue(filho, out var antes) && (_mudouDeModo || antes.Linha != posicao.Linha))
                Deslizar(filho, antes.Posicao - novo);
            _anteriores[filho] = (novo, posicao.Linha);
        }

        _mudouDeModo = false;
        return finalSize;
    }

    static (double Completa, double Compacta, double Altura) MedirNatural(UIElement filho)
    {
        var infinito = new Size(double.PositiveInfinity, double.PositiveInfinity);
        if (filho is not Pilula pilula)
        {
            filho.Measure(infinito);
            return (filho.DesiredSize.Width, filho.DesiredSize.Width, filho.DesiredSize.Height);
        }

        pilula.Compacta = false;
        pilula.Measure(infinito);
        var (completa, altura) = (pilula.DesiredSize.Width, pilula.DesiredSize.Height);
        pilula.Compacta = true;
        pilula.Measure(infinito);
        return (completa, pilula.DesiredSize.Width, altura);
    }

    // Parte de onde a pílula estava na tela (inclusive no meio de um deslize anterior) e volta a zero.
    static void Deslizar(UIElement filho, Vector desvio)
    {
        if (filho.RenderTransform is not TranslateTransform transform)
            filho.RenderTransform = transform = new TranslateTransform();
        Animar(transform, TranslateTransform.XProperty, desvio.X + transform.X);
        Animar(transform, TranslateTransform.YProperty, desvio.Y + transform.Y);
    }

    static void Animar(TranslateTransform transform, DependencyProperty eixo, double de) =>
        transform.BeginAnimation(eixo, new DoubleAnimation(de, 0, Animacao.Movimento(Animacao.Media)) { EasingFunction = Animacao.Suave });
}
