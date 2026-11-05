using System.Text;
using System.Windows;
using Menage.App.Controles;
using Menage.Core.BetaAutomatica;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Janela não modal: mostra a simulação linha a linha enquanto o resto do programa segue livre.</summary>
public partial class JanelaDeDiagnostico : JanelaBase
{
    public const string Id = "beta-auto.diagnostico";

    readonly ServicoDeBetaAutomatica _servico;
    readonly StringBuilder _buffer = new();
    readonly System.Windows.Threading.DispatcherTimer _descarga = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public JanelaDeDiagnostico(ContextoDeJanela contexto, ServicoDeBetaAutomatica servico) : base(contexto)
    {
        _servico = servico;
        InitializeComponent();
        _descarga.Tick += (_, _) => Descarregar();
        Closed += (_, _) => _descarga.Stop();
    }

    void Descarregar()
    {
        string pedaco;
        lock (_buffer)
        {
            if (_buffer.Length == 0) return;
            pedaco = _buffer.ToString();
            _buffer.Clear();
        }

        Texto.AppendText(pedaco);
        Texto.ScrollToEnd();
    }

    async void AoExecutar(object sender, RoutedEventArgs e)
    {
        Executar.IsEnabled = false;
        Texto.Clear();
        _descarga.Start();
        try { await _servico.DiagnosticarAsync(linha => { lock (_buffer) _buffer.Append(linha).Append("\r\n"); }); }
        finally
        {
            _descarga.Stop();
            Descarregar();
            Executar.IsEnabled = true;
        }
    }

    void AoCopiar(object sender, RoutedEventArgs e)
    {
        if (Texto.Text.Length > 0) Clipboard.SetText(Texto.Text);
    }
}
