using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.BetaAutomatica;

namespace Menage.App.Modulos.BetaAutomatica;

public sealed record LinhaDoConsole(RegistroDoConsole Registro)
{
    public NivelDoConsole Nivel => Registro.Nivel;

    public string Linha => $"{Registro.Quando:HH:mm:ss}  {Registro.Nivel.ToString().ToUpperInvariant(),-7} {Registro.Origem,-15} {(Registro.Atividade.Length > 0 ? Registro.Atividade : "-"),-12} {(Registro.Projeto.Length > 0 ? Registro.Projeto : "-"),-18} {Registro.Texto.Replace('\n', ' ')}";
}

public sealed record OpcaoDoConsole<T>(string Rotulo, T Valor);

/// <summary>
/// Console ancorado embaixo da janela: tudo o que a geração da beta faz, ao vivo, com filtros (nível, origem, atividade, busca),
/// Acompanhar e Pausar. A regra de filtro é o <see cref="FiltroDoConsole"/>; o histórico fica em logs-beta-auto\console-beta.log.
/// </summary>
public sealed partial class ConsoleDoDockViewModel : ObservableObject, IDisposable
{
    public const double AlturaMinima = 120, AlturaMaxima = 520;

    readonly ConsoleDaBeta _console;
    readonly List<LinhaDoConsole> _todas = [];
    readonly List<LinhaDoConsole> _pausadas = [];

    [ObservableProperty]
    bool _expandido = true;

    [ObservableProperty]
    double _altura = 190;

    [ObservableProperty]
    NivelDeFiltro _nivel = NivelDeFiltro.SemSaida;

    [ObservableProperty]
    string _origem = "";

    [ObservableProperty]
    string _atividade = "";

    [ObservableProperty]
    string _busca = "";

    [ObservableProperty]
    bool _acompanhar = true;

    [ObservableProperty]
    bool _pausado;

    [ObservableProperty]
    ObservableCollection<LinhaDoConsole> _visiveis = [];

    [ObservableProperty]
    int _erros;

    [ObservableProperty]
    string _rodape = "";

    public ConsoleDoDockViewModel(ConsoleDaBeta console)
    {
        _console = console;
        _todas.AddRange(console.Registros.Select(r => new LinhaDoConsole(r)));
        Erros = _todas.Count(l => l.Nivel == NivelDoConsole.Erro);
        Refiltrar();
        console.Registrado += AoRegistrar;
        console.Limpo += AoLimpar;
    }

    /// <summary>A tela rola até a última linha (quando Acompanhar está ligado).</summary>
    public event Action? RolarParaOFim;

    public IReadOnlyList<OpcaoDoConsole<NivelDeFiltro>> Niveis { get; } =
    [
        new("Tudo", NivelDeFiltro.Tudo), new("Só avisos e erros", NivelDeFiltro.Problemas),
        new("Só comandos", NivelDeFiltro.Comandos), new("Sem a saída do Maven/git", NivelDeFiltro.SemSaida),
    ];

    public IReadOnlyList<OpcaoDoConsole<string>> Origens { get; } =
    [
        new("Todas as origens", ""), new("Beta automática", FiltroDoConsole.OrigemBeta), new("Monitor", FiltroDoConsole.OrigemMonitor),
    ];

    public FiltroDoConsole Filtro => new(Nivel, Origem, Atividade, Busca);

    public bool TemErros => Erros > 0;

    public string TextoDoErro => Erros.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string TextoDoCabecalho => Expandido ? "recolher ▾" : "expandir ▴";

    public string TextoDaPausa => Pausado ? "Retomar" : "Pausar";

    public string ArquivoDoLog => _console.Arquivo ?? "";

    public string TextoNaTela() => string.Join(Environment.NewLine, Visiveis.Select(l => l.Linha));

    public void ExpandirDock() => Expandido = true;

    public void Redimensionar(double delta) => Altura = Math.Clamp(Altura - delta, AlturaMinima, AlturaMaxima);

    [RelayCommand]
    void AlternarDock() => Expandido = !Expandido;

    [RelayCommand]
    void LimparTela() => _console.LimparTela();

    partial void OnExpandidoChanged(bool value) => OnPropertyChanged(nameof(TextoDoCabecalho));

    partial void OnErrosChanged(int value)
    {
        OnPropertyChanged(nameof(TemErros));
        OnPropertyChanged(nameof(TextoDoErro));
    }

    partial void OnNivelChanged(NivelDeFiltro value) => Refiltrar();

    partial void OnOrigemChanged(string value) => Refiltrar();

    partial void OnAtividadeChanged(string value) => Refiltrar();

    partial void OnBuscaChanged(string value) => Refiltrar();

    partial void OnPausadoChanged(bool value)
    {
        OnPropertyChanged(nameof(TextoDaPausa));
        if (value) { AtualizarRodape(); return; }
        _todas.AddRange(_pausadas);
        Erros += _pausadas.Count(l => l.Nivel == NivelDoConsole.Erro);
        _pausadas.Clear();
        Aparar();
        Refiltrar();
    }

    /// <summary>Recebe um registro já na thread da tela. Pausado, guarda à parte até retomar.</summary>
    public void Registrar(RegistroDoConsole registro)
    {
        var linha = new LinhaDoConsole(registro);
        if (Pausado) { _pausadas.Add(linha); AtualizarRodape(); return; }

        _todas.Add(linha);
        if (linha.Nivel == NivelDoConsole.Erro) Erros++;
        Aparar();
        if (Filtro.Passa(registro))
        {
            Visiveis.Add(linha);
            if (Acompanhar) RolarParaOFim?.Invoke();
        }

        AtualizarRodape();
    }

    void Aparar()
    {
        var sobra = _todas.Count - ConsoleDaBeta.LimiteNaMemoria;
        if (sobra <= 0) return;
        Erros -= _todas.Take(sobra).Count(l => l.Nivel == NivelDoConsole.Erro);
        _todas.RemoveRange(0, sobra);
    }

    void Refiltrar()
    {
        var filtro = Filtro;
        Visiveis = new ObservableCollection<LinhaDoConsole>(_todas.Where(l => filtro.Passa(l.Registro)));
        AtualizarRodape();
        if (Acompanhar) RolarParaOFim?.Invoke();
    }

    void AtualizarRodape() =>
        Rodape = $"{Visiveis.Count} linha(s) na tela de {_todas.Count} na memória" + (_pausadas.Count > 0 ? $" · pausado: {_pausadas.Count} nova(s) esperando" : "");

    void AoRegistrar(RegistroDoConsole r) => NaTela(() => Registrar(r));

    void AoLimpar() => NaTela(() =>
    {
        _todas.Clear();
        _pausadas.Clear();
        Erros = 0;
        Refiltrar();
    });

    static void NaTela(Action acao)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) acao();
        else dispatcher.BeginInvoke(acao);
    }

    public void Dispose()
    {
        _console.Registrado -= AoRegistrar;
        _console.Limpo -= AoLimpar;
    }
}
