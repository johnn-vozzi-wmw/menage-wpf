using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.BetaAutomatica;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.BetaAutomatica;

public enum AbaDaBeta { Betas, Configurar, Jql, Diagnostico }

/// <summary>Pedido para a janela abrir numa aba (nula = fica onde está), numa atividade e/ou com o console expandido. Os comandos e atalhos usam isto.</summary>
public sealed record PedidoDeAba(AbaDaBeta? Aba = AbaDaBeta.Betas, string? Chave = null, bool Console = false);

/// <summary>Cabeçalho de uma aba, com o contador (só na aba Betas; vermelho se houver beta com problema).</summary>
public sealed partial class ItemDeAba(AbaDaBeta aba, string titulo) : ObservableObject
{
    public AbaDaBeta Aba { get; } = aba;

    public string Titulo { get; } = titulo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemContador))]
    int? _contador;

    [ObservableProperty]
    bool _alerta;

    public bool TemContador => Contador is not null;
}

/// <summary>
/// Estado da janela "Betas habilitadas": abas (Betas, Configurar, JQL, Diagnóstico), a lista de betas com o detalhe da selecionada e o
/// console ancorado. Toda regra está no <see cref="ServicoDeBetaAutomatica"/>; aqui só se apresenta e se pede confirmação.
/// </summary>
public sealed partial class BetaAutomaticaViewModel : ObservableObject, IDisposable
{
    readonly ServicoDeBetaAutomatica _servico;
    readonly IServicoDeDialogos _dialogos;
    bool _pendente;
    string? _chaveSelecionada;

    [ObservableProperty]
    bool _monitorAtivo;

    [ObservableProperty]
    string _status = "";

    [ObservableProperty]
    string _perfilTexto = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerificarAgoraCommand))]
    bool _verificando;

    [ObservableProperty]
    CartaoDeBeta? _selecionado;

    [ObservableProperty]
    ItemDeAba _abaSelecionada;

    public BetaAutomaticaViewModel(
        ServicoDeBetaAutomatica servico, IServicoDeDialogos dialogos, ConfigBetaViewModel configurar, JqlViewModel jql,
        DiagnosticoViewModel diagnostico, ConsoleDoDockViewModel console)
    {
        (_servico, _dialogos) = (servico, dialogos);
        (Configurar, Jql, Diagnostico, Console) = (configurar, jql, diagnostico, console);
        Abas = [new(AbaDaBeta.Betas, "Betas"), new(AbaDaBeta.Configurar, "Configurar"), new(AbaDaBeta.Jql, "JQL"), new(AbaDaBeta.Diagnostico, "Diagnóstico")];
        _abaSelecionada = Abas[0];
        _servico.Mudou += AoMudar;
        _servico.Mensagem += _ => AoMudar();
        Configurar.Salvo += Atualizar;
        Atualizar();
    }

    public IReadOnlyList<ItemDeAba> Abas { get; }

    public ConfigBetaViewModel Configurar { get; }

    public JqlViewModel Jql { get; }

    public DiagnosticoViewModel Diagnostico { get; }

    public ConsoleDoDockViewModel Console { get; }

    public ObservableCollection<CartaoDeBeta> Cartoes { get; } = [];

    public ObservableCollection<CartaoDeBeta> Ignoradas { get; } = [];

    public ObservableCollection<CartaoDeBeta> Finalizadas { get; } = [];

    public ServicoDeBetaAutomatica Servico => _servico;

    public AbaDaBeta AbaAtual => AbaSelecionada.Aba;

    public int Posicao => (int)AbaAtual;

    public bool Vazio => Cartoes.Count == 0;

    public bool NaoVerificando => !Verificando;

    public string TextoDoMonitor => MonitorAtivo ? "Pausar monitor" : "Retomar monitor";

    public string TextoDaVerificacao => Verificando ? "Consultando..." : "Verificar agora";

    /// <summary>Resumo do monitor para a faixa das abas (o texto completo fica na aba Betas).</summary>
    public string StatusCurto => (MonitorAtivo ? "Monitor ativo" : "Monitor pausado") + $" · a cada {_servico.Config.IntervaloMin} min";

    public string TituloIgnoradas => $"Ignoradas ({Ignoradas.Count})";

    public string TituloFinalizadas => $"Finalizadas ({Finalizadas.Count})";

    public void SelecionarAba(AbaDaBeta aba) => AbaSelecionada = Abas[(int)aba];

    /// <summary>Atende um pedido de comando/atalho: aba certa, atividade em foco e/ou console expandido.</summary>
    public void Abrir(PedidoDeAba pedido)
    {
        if (pedido.Aba is { } aba) SelecionarAba(aba);
        if (pedido.Chave is { Length: > 0 } chave) SelecionarBeta(chave);
        if (pedido.Console) Console.ExpandirDock();
    }

    public void SelecionarBeta(string chave)
    {
        SelecionarAba(AbaDaBeta.Betas);
        Selecionado = Cartoes.FirstOrDefault(c => c.Chave.Equals(chave, StringComparison.OrdinalIgnoreCase)) ?? Selecionado;
    }

    partial void OnAbaSelecionadaChanged(ItemDeAba value)
    {
        OnPropertyChanged(nameof(AbaAtual));
        OnPropertyChanged(nameof(Posicao));
        // Abas de edição releem o que está gravado ao entrar, desde que não haja alteração pendente.
        if (value.Aba == AbaDaBeta.Configurar && !Configurar.Alterado) Configurar.Carregar();
        if (value.Aba == AbaDaBeta.Jql && !Jql.Alterado) Jql.Recarregar();
    }

    partial void OnSelecionadoChanged(CartaoDeBeta? value)
    {
        if (value is not null) _chaveSelecionada = value.Chave;
    }

    partial void OnMonitorAtivoChanged(bool value) => OnPropertyChanged(nameof(TextoDoMonitor));

    partial void OnVerificandoChanged(bool value)
    {
        OnPropertyChanged(nameof(NaoVerificando));
        OnPropertyChanged(nameof(TextoDaVerificacao));
    }

    /// <summary>Pode chegar de qualquer thread (monitor, geração): junta várias mudanças numa atualização só.</summary>
    void AoMudar()
    {
        if (_pendente) return;
        _pendente = true;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) { _pendente = false; Atualizar(); }
        else dispatcher.BeginInvoke(() => { _pendente = false; Atualizar(); });
    }

    public void Atualizar()
    {
        MonitorAtivo = _servico.Config.Ativo;
        Verificando = _servico.Ocupado;
        PerfilTexto = _servico.ModoTester ? "Perfil Tester" : "Perfil Desenvolvedor";
        Preencher(Cartoes, _servico.Itens());
        Preencher(Ignoradas, _servico.Ignoradas());
        Preencher(Finalizadas, _servico.Finalizadas());
        Selecionado = Cartoes.FirstOrDefault(c => c.Chave == _chaveSelecionada) ?? Cartoes.FirstOrDefault();
        var c = _servico.Config;
        Status = (c.Ativo ? "Monitor ativo" : "Monitor pausado") + $" · a cada {c.IntervaloMin} min"
            + (c.UltimaConsulta.Length > 0 ? $" · última consulta {c.UltimaConsulta}" : " · nenhuma consulta ainda")
            + (c.UltimoErro.Length > 0 ? $" · ERRO: {c.UltimoErro}" : _servico.UltimoResumo.Length > 0 ? " · " + _servico.UltimoResumo : "");
        var contador = Abas[0];
        contador.Contador = Cartoes.Count;
        contador.Alerta = Cartoes.Any(x => x.ComProblema);
        OnPropertyChanged(nameof(Vazio));
        OnPropertyChanged(nameof(StatusCurto));
        OnPropertyChanged(nameof(TituloIgnoradas));
        OnPropertyChanged(nameof(TituloFinalizadas));
    }

    void Preencher(ObservableCollection<CartaoDeBeta> destino, IReadOnlyList<ItemBeta> itens)
    {
        destino.Clear();
        foreach (var i in itens) destino.Add(new CartaoDeBeta(i, Acao));
    }

    void Acao(string nome, string chave) => _ = ExecutarAsync(nome, chave);

    public async Task ExecutarAsync(string nome, string chave)
    {
        switch (nome)
        {
            case "gerar": _servico.Gerar(chave); break;
            case "cancelar": _servico.CancelarGeracao(chave); break;
            case "ignorar": _servico.Ignorar(chave); break;
            case "reverter": await _servico.ReverterIgnoradaAsync(chave); break;
            case "reabrir": await _servico.ReabrirFinalizadaAsync(chave); break;
            case "remover" when await _dialogos.ConfirmarAsync(new Previa("Remover da lista", $"Tirar {chave} da lista?", ["Ela volta na próxima verificação se ainda estiver no status monitorado."])):
                _servico.Remover(chave);
                break;
            case "finalizar" when await _dialogos.ConfirmarAsync(new Previa("Finalizar atividade", $"Marcar {chave} como finalizada?", ["Sai da lista principal e fica em Finalizadas, com data e motivo. Dá para reabrir."])):
                _servico.Finalizar(chave);
                break;
        }
    }

    [RelayCommand]
    public void AlternarMonitor()
    {
        var ligar = !_servico.Config.Ativo;
        _servico.Alterar(c => c.Ativo = ligar);
        if (ligar) { _servico.Iniciar(); _servico.VerificarAgora(); }
        Atualizar();
    }

    [RelayCommand(CanExecute = nameof(NaoVerificando))]
    public void VerificarAgora()
    {
        _servico.Iniciar();
        _servico.VerificarAgora();
        if (!_servico.Config.Ativo) _ = _servico.CicloAsync(false, CancellationToken.None);
    }

    [RelayCommand]
    public async Task LimparListaAsync()
    {
        if (await _dialogos.ConfirmarAsync(new Previa("Limpar lista", "Remover todas as atividades da lista?", ["Ficam só as que estão na fila ou gerando; as finalizadas continuam. As removidas voltam na próxima verificação se ainda estiverem no status."])))
            _servico.Limpar();
    }

    [RelayCommand]
    public void CancelarTodas() => _servico.CancelarTodas();

    public void Dispose()
    {
        _servico.Mudou -= AoMudar;
        Configurar.Salvo -= Atualizar;
    }
}
