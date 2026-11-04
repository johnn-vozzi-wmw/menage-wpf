using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace Menage.Pocs.Comum;

public record ResultadoAnimacao(int Quadros, double MediaMs, double P95Ms, double MaxMs, int PassesLayout, double DuracaoMs);

/// <summary>Mede o intervalo entre quadros renderizados e quantos passes de layout ocorreram.</summary>
public sealed class MedidorAnimacao
{
    private readonly List<double> _intervalos = [];
    private readonly Stopwatch _relogio = new();
    private TimeSpan _ultimo;
    private int _layouts;

    public void Iniciar()
    {
        _intervalos.Clear();
        _layouts = 0;
        _ultimo = TimeSpan.Zero;
        _relogio.Restart();
        CompositionTarget.Rendering += AoQuadro;
        Application.Current.MainWindow!.LayoutUpdated += AoLayout;
    }

    public ResultadoAnimacao Parar()
    {
        CompositionTarget.Rendering -= AoQuadro;
        Application.Current.MainWindow!.LayoutUpdated -= AoLayout;
        _relogio.Stop();
        var ord = _intervalos.OrderBy(x => x).ToList();
        if (ord.Count == 0) return new(0, 0, 0, 0, _layouts, _relogio.Elapsed.TotalMilliseconds);
        return new(ord.Count, Math.Round(ord.Average(), 2), Math.Round(ord[(int)(ord.Count * 0.95)], 2),
            Math.Round(ord[^1], 2), _layouts, Math.Round(_relogio.Elapsed.TotalMilliseconds, 0));
    }

    private void AoLayout(object? s, EventArgs e) => _layouts++;

    private void AoQuadro(object? s, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        if (_ultimo != TimeSpan.Zero && t != _ultimo) _intervalos.Add((t - _ultimo).TotalMilliseconds);
        _ultimo = t;
    }
}
