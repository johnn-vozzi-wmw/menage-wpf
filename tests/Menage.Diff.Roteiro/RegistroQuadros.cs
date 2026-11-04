using System.Diagnostics;
using Menage.App.Modulos.Diff;

namespace Menage.Diff.Roteiro;

/// <summary>Olha cada quadro depois do layout: o que os painéis realmente têm na tela contra o que o mapa manda.</summary>
public sealed class RegistroQuadros(JanelaDiff janela, System.Windows.Controls.Primitives.Thumb? polegar = null)
{
    readonly PainelDiff _p = janela.PainelDoDiff;
    readonly Stopwatch _relogio = Stopwatch.StartNew();
    readonly List<(double Ms, double U, double Alvo, double Esq, double Dir, double X)> _quadros = [];
    readonly List<double> _custos = [];
    int _desalinhados;
    int _linhasAtrasadas;
    bool _aguardando;
    string? _primeiro;
    double _ultimoU;
    int _preso;

    public double UltimoU => Volatile.Read(ref _ultimoU);

    /// <summary>Em quantos quadros o polegar da barra estava preso ao mouse (nulo quando o alvo não é uma barra).</summary>
    public int? PolegarPreso => polegar is null ? null : _preso;

    public void Ligar()
    {
        _p.Rolagem.Moveu += AoMover;
        _p.Rolagem.QuadroAplicado += _custos.Add;
        janela.LayoutUpdated += AoLayout;
    }

    public void Desligar()
    {
        _p.Rolagem.Moveu -= AoMover;
        _p.Rolagem.QuadroAplicado -= _custos.Add;
        janela.LayoutUpdated -= AoLayout;
    }

    public object Resumo(int sentido)
    {
        var intervalos = _quadros.Zip(_quadros.Skip(1), (a, b) => b.Ms - a.Ms).ToList();
        var passos = _quadros.Zip(_quadros.Skip(1), (a, b) => (Esq: b.Esq - a.Esq, Dir: b.Dir - a.Dir, X: b.X - a.X)).ToList();
        var atraso = _quadros.Select(q => Math.Abs(q.Alvo - q.U)).ToList();
        return new
        {
            quadros = _quadros.Count,
            desalinhados = _desalinhados,
            linhasAtrasadas = _linhasAtrasadas,
            primeiroDesalinhado = _primeiro,
            primeiroRecuo = PrimeiroRecuo(sentido),
            recuos = passos.Count(p => (p.Esq * sentido < -0.5) || (p.Dir * sentido < -0.5)),
            intervaloMs = Estatistica.De(intervalos),
            custoQuadroMs = Estatistica.De(_custos),
            maiorPassoPx = new { esq = Math.Round(passos.Select(p => Math.Abs(p.Esq)).DefaultIfEmpty(0).Max()), dir = Math.Round(passos.Select(p => Math.Abs(p.Dir)).DefaultIfEmpty(0).Max()), x = Math.Round(passos.Select(p => Math.Abs(p.X)).DefaultIfEmpty(0).Max()) },
            atrasoDoConteudoPx = Estatistica.De(atraso),
        };
    }

    string? PrimeiroRecuo(int sentido)
    {
        for (var i = 1; i < _quadros.Count; i++)
        {
            var (a, b) = (_quadros[i - 1], _quadros[i]);
            if ((b.Dir - a.Dir) * sentido < -0.5 || (b.Esq - a.Esq) * sentido < -0.5) return $"quadro {i}: dir {a.Dir}->{b.Dir} u {a.U:F0}->{b.U:F0} alvo {a.Alvo:F0}->{b.Alvo:F0}";
        }
        return null;
    }

    void AoMover()
    {
        _aguardando = true;
        Volatile.Write(ref _ultimoU, _p.Rolagem.U);
    }

    void AoLayout(object? sender, EventArgs e)
    {
        if (!_aguardando) return;
        _aguardando = false;
        if (polegar?.IsDragging == true) _preso++;
        var (esq, dir, rolagem) = (_p.Esquerdo.Vista, _p.Direito.Vista, _p.Rolagem);
        var mapa = rolagem.Mapa!;
        var (ye, yd) = (Math.Round(mapa.Y(0, rolagem.U)), Math.Round(mapa.Y(1, rolagem.U)));
        var erro = Math.Max(Math.Abs(esq.VerticalOffset - ye), Math.Abs(dir.VerticalOffset - yd));
        if (erro > 1 || Math.Abs(esq.HorizontalOffset - dir.HorizontalOffset) > 1)
        {
            _desalinhados++;
            _primeiro ??= $"esq={esq.VerticalOffset}/{ye} dir={dir.VerticalOffset}/{yd} x={esq.HorizontalOffset}/{dir.HorizontalOffset}";
        }
        // A linha construída no topo tem que ser a da posição: pega painel que mudou o número mas pintou o quadro velho.
        if (!Coerente(esq) || !Coerente(dir)) _linhasAtrasadas++;
        _quadros.Add((_relogio.Elapsed.TotalMilliseconds, rolagem.U, rolagem.AlvoU, esq.VerticalOffset, dir.VerticalOffset, esq.HorizontalOffset));
    }

    static bool Coerente(ICSharpCode.AvalonEdit.Rendering.TextView vista)
    {
        if (!vista.VisualLinesValid || vista.VisualLines.Count == 0) return false;
        var topo = vista.VisualLines[0];
        return topo.VisualTop <= vista.VerticalOffset + 0.5 && topo.VisualTop + topo.Height > vista.VerticalOffset - 0.5;
    }
}
