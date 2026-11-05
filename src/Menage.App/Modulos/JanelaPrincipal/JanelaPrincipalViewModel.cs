using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Operacoes.Fetch;
using Menage.Core.Projetos;
using Menage.Core.Repos;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>De onde vêm os projetos, em uma frase para a barra de status ("--projetos &lt;pasta&gt;", raiz ou lista própria).</summary>
public sealed record OrigemDosProjetos(string? Texto = null);

/// <summary>
/// Junta as partes da janela principal e cuida da barra de status: mensagem, progresso, "Parar após o projeto atual", versão do git,
/// modo SSH e fetch automático. A situação é relida ao terminar cada operação.
/// </summary>
public sealed partial class JanelaPrincipalViewModel : ObservableObject, IDisposable
{
    static readonly TimeSpan _intervaloDoFetchAutomatico = TimeSpan.FromMinutes(5);

    readonly CoordenadorDeOperacoes _coordenador;
    readonly IProjetos _projetos;
    readonly FetchSilencioso _fetchSilencioso;
    readonly PreferenciasDaJanelaServico _preferencias;
    readonly TimeProvider _relogio;
    readonly SynchronizationContext? _tela = SynchronizationContext.Current;
    bool _emOperacaoAnterior;
    ITimer? _temporizador;

    [ObservableProperty]
    string _mensagem = "Pronto";

    [ObservableProperty]
    string _versaoDoGit = "";

    [ObservableProperty]
    string _modoSsh = "";

    [ObservableProperty]
    bool _emOperacao;

    [ObservableProperty]
    string _textoDoProgresso = "";

    [ObservableProperty]
    double _fracaoDoProgresso;

    [ObservableProperty]
    bool _pararSolicitado;

    public JanelaPrincipalViewModel(
        GradeDeProjetosViewModel grade, BarraLateralViewModel barraLateral, SaidaViewModel saida, BarraDeAcoesViewModel acoes,
        CoordenadorDeOperacoes coordenador, IProjetos projetos, IConfiguracaoDeProjetos configuracao, FetchSilencioso fetchSilencioso,
        InformacoesDoAmbiente ambiente, PreferenciasDaJanelaServico preferencias, OrigemDosProjetos origem, TimeProvider? relogio = null)
    {
        (Grade, BarraLateral, Saida, Acoes) = (grade, barraLateral, saida, acoes);
        (_coordenador, _projetos, _fetchSilencioso, _preferencias) = (coordenador, projetos, fetchSilencioso, preferencias);
        _relogio = relogio ?? TimeProvider.System;
        OrigemDosProjetos = origem.Texto ?? (configuracao.Catalogo.Raiz.Length > 0 ? $"Projetos: raiz {configuracao.Catalogo.Raiz}" : "Projetos: lista própria");
        FetchAutomatico = preferencias.Valor.FetchAutomatico;
        _coordenador.Alterado += AoMudarOperacao;
        _projetos.Alterado += AoMudarSelecao;
        Grade.Visiveis.CollectionChanged += (_, _) => Saida.LinhasDaGrade = Grade.Visiveis.Count;
        Saida.LinhasDaGrade = Grade.Visiveis.Count;
        AtualizarMensagemDeSelecao();
        _ = CarregarAmbienteAsync(ambiente);
        ConfigurarTemporizador();
    }

    public GradeDeProjetosViewModel Grade { get; }

    public BarraLateralViewModel BarraLateral { get; }

    public SaidaViewModel Saida { get; }

    public BarraDeAcoesViewModel Acoes { get; }

    public string OrigemDosProjetos { get; }

    public bool FetchAutomatico
    {
        get => _preferencias.Valor.FetchAutomatico;
        set
        {
            if (value == _preferencias.Valor.FetchAutomatico) return;
            _preferencias.Alterar(p => p with { FetchAutomatico = value });
            OnPropertyChanged();
            ConfigurarTemporizador();
        }
    }

    public void Dispose()
    {
        _coordenador.Alterado -= AoMudarOperacao;
        _projetos.Alterado -= AoMudarSelecao;
        _temporizador?.Dispose();
    }

    [RelayCommand]
    void PararAposOAtual() => _coordenador.PararApos();

    /// <summary>Mensagem pontual da barra de status (aviso do canto também passa por aqui).</summary>
    public void Informar(string texto) => Mensagem = texto;

    /// <summary>Um passo do fetch automático: em segundo plano, sem diálogo, nunca durante operação.</summary>
    public async Task FetchAutomaticoAgoraAsync()
    {
        if (_coordenador.EmAndamento || _projetos.Todos.Count == 0) return;
        Mensagem = "Fetch automático...";
        var falhas = await _fetchSilencioso.ExecutarAsync(_projetos.Todos);
        await Grade.LerAsync();
        Mensagem = falhas == 0 ? $"Fetch automático às {_relogio.GetLocalNow():HH:mm}" : $"Fetch automático às {_relogio.GetLocalNow():HH:mm}: {falhas} projeto(s) falharam";
    }

    void AoMudarOperacao() => NaTela(() =>
    {
        EmOperacao = _coordenador.EmAndamento;
        PararSolicitado = _coordenador.PararSolicitado;
        var passo = _coordenador.Passo;
        FracaoDoProgresso = passo is null || passo.Total == 0 ? 0 : (double)(passo.Fase == FaseDoProjeto.Terminou ? passo.Indice : passo.Indice - 1) / passo.Total;
        TextoDoProgresso = passo is null ? _coordenador.Operacao : $"{_coordenador.Operacao}: {passo.Indice}/{passo.Total} {passo.Projeto}";
        if (EmOperacao) Mensagem = PararSolicitado ? $"{_coordenador.Operacao}: parando após o projeto atual..." : TextoDoProgresso;
        else if (_emOperacaoAnterior) _ = TerminouAsync();
        _emOperacaoAnterior = EmOperacao;
    });

    async Task TerminouAsync()
    {
        Mensagem = "Atualizando situação dos projetos...";
        await Grade.LerAsync();
        AtualizarMensagemDeSelecao();
    }

    void AoMudarSelecao(object? origem, EventArgs e) => NaTela(() =>
    {
        if (!_coordenador.EmAndamento && !Grade.Lendo) AtualizarMensagemDeSelecao();
    });

    void AtualizarMensagemDeSelecao() =>
        Mensagem = _projetos.Marcados.Count switch
        {
            0 => "Pronto. Marque os projetos para operar.",
            1 => "1 projeto marcado",
            var n => $"{n} projetos marcados",
        };

    async Task CarregarAmbienteAsync(InformacoesDoAmbiente ambiente)
    {
        VersaoDoGit = await ambiente.VersaoDoGitAsync();
        ModoSsh = await ambiente.ModoSshAsync();
    }

    void ConfigurarTemporizador()
    {
        _temporizador?.Dispose();
        _temporizador = FetchAutomatico
            ? _relogio.CreateTimer(_ => NaTela(() => _ = FetchAutomaticoAgoraAsync()), null, _intervaloDoFetchAutomatico, _intervaloDoFetchAutomatico)
            : null;
    }

    void NaTela(Action acao)
    {
        if (_tela is null || SynchronizationContext.Current == _tela) acao();
        else _tela.Post(_ => acao(), null);
    }
}
