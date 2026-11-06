using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.BetaAutomatica;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Aba Diagnóstico: roda a simulação da busca (as mesmas consultas e regras do ciclo real, sem alterar nada) e mostra a decisão por atividade.</summary>
public sealed partial class DiagnosticoViewModel(ServicoDeBetaAutomatica servico) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NaoSimulando), nameof(MostrarOrientacao), nameof(TextoDoBotao))]
    [NotifyCanExecuteChangedFor(nameof(SimularCommand))]
    bool _simulando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemRelatorio), nameof(MostrarOrientacao), nameof(Contagem), nameof(CabecalhoTexto), nameof(RodapeTexto))]
    [NotifyCanExecuteChangedFor(nameof(CopiarCommand))]
    RelatorioDoDiagnostico? _relatorio;

    [ObservableProperty]
    string _texto = "";

    public ObservableCollection<DecisaoDaBusca> Decisoes { get; } = [];

    public bool NaoSimulando => !Simulando;

    public string TextoDoBotao => Simulando ? "Simulando..." : "Simular a busca";

    public bool TemRelatorio => Relatorio is not null;

    public bool MostrarOrientacao => Relatorio is null && !Simulando;

    public string Contagem => Relatorio is { } r ? $"{r.Entram} entram · {r.FicamDeFora} ficam de fora" : "";

    public string CabecalhoTexto => Relatorio is null ? "" : string.Join(Environment.NewLine, Relatorio.Cabecalho);

    public string RodapeTexto => Relatorio is null ? "" : string.Join(Environment.NewLine, Relatorio.ForaDaBusca.Concat(Relatorio.Resumo));

    /// <summary>Quem copia (a tela) lê este texto; é o relatório inteiro, igual ao do motor.</summary>
    public event Action<string>? PedirCopia;

    [RelayCommand(CanExecute = nameof(NaoSimulando))]
    public async Task SimularAsync()
    {
        Simulando = true;
        Relatorio = null;
        Decisoes.Clear();
        var linhas = new List<string>();
        try
        {
            await servico.DiagnosticarAsync(linha => { lock (linhas) linhas.Add(linha); });
            string[] copia;
            lock (linhas) copia = [.. linhas];
            Receber(copia);
        }
        finally { Simulando = false; }
    }

    /// <summary>Monta o relatório a partir das linhas da simulação (também usado pelos testes e pela galeria).</summary>
    public void Receber(IReadOnlyList<string> linhas)
    {
        Texto = string.Join(Environment.NewLine, linhas.SelectMany(l => l.Replace("\r\n", "\n").Split('\n')));
        var relatorio = InterpretadorDoDiagnostico.Interpretar(linhas.SelectMany(l => l.Replace("\r\n", "\n").Split('\n')));
        Decisoes.Clear();
        foreach (var d in relatorio.Decisoes) Decisoes.Add(d);
        Relatorio = relatorio;
    }

    [RelayCommand(CanExecute = nameof(TemRelatorio))]
    void Copiar() => PedirCopia?.Invoke(Texto);
}
