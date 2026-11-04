using System.Windows;
using System.Windows.Media;
using Menage.Core.Diff;

namespace Menage.App.Modulos.Diff.Rolagem;

/// <summary>Faixas curvas entre os painéis ligando cada mudança. Um DrawingVisual refeito no mesmo tique da rolagem, sem layout.</summary>
public sealed class FaixasLigacao : FrameworkElement
{
    readonly DrawingVisual _desenho = new();
    readonly Dictionary<(Color, Color, byte), Brush> _gradientes = [];
    IReadOnlyList<BlocoVista> _blocos = [];
    CoresDiff? _cores;
    (double Esq, double Dir, double Altura) _ultima;

    public FaixasLigacao()
    {
        AddVisualChild(_desenho);
        ClipToBounds = true;
        SizeChanged += (_, _) => Desenhar();
    }

    /// <summary>Índice do bloco atual (contorno na cor de destaque); -1 sem realce.</summary>
    public int Atual { get; set; } = -1;

    protected override int VisualChildrenCount => 1;

    public void Configurar(CoresDiff cores, IReadOnlyList<BlocoVista> blocos)
    {
        (_cores, _blocos) = (cores, blocos);
        _gradientes.Clear();
        Atual = -1;
    }

    public void Redesenhar(double yEsquerda, double yDireita, double alturaLinha)
    {
        _ultima = (yEsquerda, yDireita, alturaLinha);
        Desenhar();
    }

    protected override Visual GetVisualChild(int index) => _desenho;

    void Desenhar()
    {
        if (_cores is null) return;
        using var dc = _desenho.RenderOpen();
        dc.DrawRectangle(_cores.Fundo, null, new Rect(RenderSize));
        var (yE, yD, h) = _ultima;
        var altura = ActualHeight;
        for (var i = Primeiro(yE, yD, h); i < _blocos.Count; i++)
        {
            var b = _blocos[i];
            var (e1, d1) = ((b.ERow * h) - yE, (b.DRow * h) - yD);
            if (Math.Min(e1, d1) > altura) break;
            var (e2, d2) = (e1 + Math.Max(b.ERows * h, 1), d1 + Math.Max(b.DRows * h, 1));
            if (Math.Max(e2, d2) >= 0) Faixa(dc, b, i, e1, e2, d1, d2);
        }
    }

    /// <summary>Busca binária do primeiro bloco que ainda não saiu por cima da tela.</summary>
    int Primeiro(double yE, double yD, double h)
    {
        var (baixo, alto) = (0, _blocos.Count);
        while (baixo < alto)
        {
            var meio = (baixo + alto) / 2;
            var b = _blocos[meio];
            var fim = Math.Max(((b.ERow + b.ERows) * h) - yE, ((b.DRow + b.DRows) * h) - yD);
            if (fim < -1) baixo = meio + 1;
            else alto = meio;
        }
        return baixo;
    }

    void Faixa(DrawingContext dc, BlocoVista b, int indice, double e1, double e2, double d1, double d2)
    {
        var cores = _cores!;
        var w = ActualWidth;
        var meio = w / 2;
        var geometria = new StreamGeometry();
        using (var g = geometria.Open())
        {
            g.BeginFigure(new Point(0, e1), true, true);
            g.BezierTo(new Point(meio, e1), new Point(meio, d1), new Point(w, d1), true, false);
            g.LineTo(new Point(w, d2), true, false);
            g.BezierTo(new Point(meio, d2), new Point(meio, e2), new Point(0, e2), true, false);
        }
        geometria.Freeze();
        var (ce, cd) = b.Motivo is not null
            ? (cores.Ambar, cores.Ambar)
            : (b.ERows == 0 ? cores.Sucesso : cores.Perigo, b.DRows == 0 ? cores.Perigo : cores.Sucesso);
        var contorno = indice == Atual ? new Pen(cores.Pincel(cores.Destaque), 1.5) : new Pen(Gradiente(ce, cd, 170), 1);
        contorno.Freeze();
        dc.DrawGeometry(Gradiente(ce, cd, 64), contorno, geometria);
    }

    Brush Gradiente(Color esquerda, Color direita, byte alfa)
    {
        if (_gradientes.TryGetValue((esquerda, direita, alfa), out var pronto)) return pronto;
        var pincel = new LinearGradientBrush(Color.FromArgb(alfa, esquerda.R, esquerda.G, esquerda.B), Color.FromArgb(alfa, direita.R, direita.G, direita.B), 0);
        pincel.Freeze();
        return _gradientes[(esquerda, direita, alfa)] = pincel;
    }
}
