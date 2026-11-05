using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core;
using Menage.Core.Atualizacao;

namespace Menage.App.Modulos.Instalacoes;

/// <summary>Painel de repouso da tela (a transição troca por <see cref="PainelDeProgresso"/> durante o download).</summary>
public sealed class PainelDeRepouso;

public sealed partial class PainelDeProgresso : ObservableObject
{
    [ObservableProperty] int _percentual;
    [ObservableProperty] string _texto = "";
}

/// <summary>Tela Sobre / Atualizações. Só liga o <see cref="ServicoDeInstalacoes"/> à tela; as regras moram no Core.</summary>
public sealed partial class SobreViewModel : ObservableObject
{
    readonly ServicoDeInstalacoes _servico;
    readonly DetectorDoAppAntigo _detector;
    readonly PainelDeRepouso _repouso = new();
    string? _alvo;
    string? _versaoAnterior;
    bool _recarregando;

    public SobreViewModel(ServicoDeInstalacoes servico, DetectorDoAppAntigo detector)
    {
        (_servico, _detector) = (servico, detector);
        _painel = _repouso;
        Recarregar();
    }

    public ObservableCollection<VersaoDoFeed> Versoes { get; } = [];

    public PainelDeProgresso Progresso { get; } = new();

    [ObservableProperty] object _painel;
    [ObservableProperty] int _indicePainel;
    [ObservableProperty] string _versaoAtual = "";
    [ObservableProperty] string _modo = "";
    [ObservableProperty] string _origem = "";
    [ObservableProperty] string _avisoDeOrigem = "";
    [ObservableProperty] bool _canalBeta;
    [ObservableProperty] string _status = "Ainda não verificado.";
    [ObservableProperty] string _notas = "";
    [ObservableProperty] string _antigo = "";
    [ObservableProperty] VersaoDoFeed? _selecionada;
    [ObservableProperty] bool _ocupado;
    [ObservableProperty] bool _confirmando;
    [ObservableProperty] string _textoDaConfirmacao = "";
    [ObservableProperty] string _erro = "";

    public bool PodeVerificar => !Ocupado;

    public bool PodeAtualizar => !Ocupado && _alvo is not null;

    public bool PodeVoltar => !Ocupado && _versaoAnterior is not null;

    public string TextoAtualizar => _alvo is null ? "Atualizar" : $"Atualizar para {_alvo}";

    public string TextoVoltar => _versaoAnterior is null ? "Voltar para a versão anterior" : $"Voltar para {_versaoAnterior}";

    partial void OnOcupadoChanged(bool value) => AtualizarBotoes();

    partial void OnSelecionadaChanged(VersaoDoFeed? value) => Notas = value?.Notas ?? "Sem notas para esta versão.";

    partial void OnCanalBetaChanged(bool value)
    {
        if (_recarregando) return;
        _servico.DefinirCanal(value ? CanalDeAtualizacao.Beta : CanalDeAtualizacao.Estavel);
        _ = VerificarAsync();
    }

    void Recarregar()
    {
        _recarregando = true;
        var s = _servico.Situacao;
        VersaoAtual = s.VersaoAtual;
        Modo = s.Instalado ? "Instalado pelo instalador" : "Executando fora do instalador (sem atualização automática)";
        Origem = s.Origem ?? "";
        AvisoDeOrigem = s.OrigemDoAmbiente is { } e ? $"A variável MENAGE_ORIGEM_ATUALIZACAO está em uso e tem prioridade: {e}" : "";
        CanalBeta = s.Canal == CanalDeAtualizacao.Beta;
        var antigas = _detector.Detectar();
        Antigo = antigas.Count == 0
            ? "Nenhuma instalação do Git Manager antigo encontrada."
            : "Git Manager antigo encontrado (só informativo, o Menage não altera nem desinstala): " + string.Join("; ", antigas.Select(a => Path.GetFileName(a.Pasta)));
        _recarregando = false;
    }

    [RelayCommand]
    public async Task VerificarAsync()
    {
        Ocupado = true;
        Erro = "";
        try
        {
            var r = await _servico.VerificarAsync();
            Status = r.Estado switch
            {
                EstadoDaVerificacao.SemOrigem => "Sem origem de atualização. Informe a pasta de rede, a URL ou github:dono/repo abaixo.",
                EstadoDaVerificacao.ForaDoInstalador => "Fora do instalador: não há o que atualizar.",
                EstadoDaVerificacao.EmDia => "Você está na versão mais recente.",
                EstadoDaVerificacao.NovaVersao => $"Versão nova disponível: {r.Versao}.",
                _ => "Não foi possível verificar.",
            };
            if (r.Estado == EstadoDaVerificacao.Falhou) Erro = r.Mensagem ?? "";
            _alvo = r.Estado == EstadoDaVerificacao.NovaVersao ? r.Versao : null;
            await CarregarListaAsync();
        }
        finally
        {
            Ocupado = false;
            AtualizarBotoes();
        }
    }

    async Task CarregarListaAsync()
    {
        Versoes.Clear();
        _versaoAnterior = null;
        var lista = await _servico.ListarAsync();
        if (!lista.EhOk)
        {
            Notas = "";
            return;
        }

        foreach (var v in lista.Valor) Versoes.Add(v);
        _versaoAnterior = _servico.VersaoAnterior(lista.Valor);
        Selecionada = lista.Valor.Count == 0 ? null : lista.Valor.FirstOrDefault(v => v.EhAtual) ?? lista.Valor[0];
    }

    [RelayCommand]
    void SalvarOrigem()
    {
        _servico.DefinirOrigem(Origem);
        Recarregar();
        _ = VerificarAsync();
    }

    [RelayCommand]
    async Task AtualizarAsync()
    {
        if (_alvo is not null) await InstalarAsync(_alvo);
    }

    /// <summary>Voltar pede confirmação na própria tela (um clique a mais), porque reinicia o app.</summary>
    [RelayCommand]
    void PedirVoltar()
    {
        if (_versaoAnterior is null) return;
        TextoDaConfirmacao = $"Voltar do {VersaoAtual} para o {_versaoAnterior}? O Menage vai baixar essa versão e reiniciar.";
        Confirmando = true;
    }

    [RelayCommand]
    void CancelarVoltar() => Confirmando = false;

    [RelayCommand]
    async Task ConfirmarVoltarAsync()
    {
        Confirmando = false;
        if (_versaoAnterior is not null) await InstalarAsync(_versaoAnterior);
    }

    async Task InstalarAsync(string versao)
    {
        Ocupado = true;
        Erro = "";
        Progresso.Percentual = 0;
        Progresso.Texto = $"Baixando a versão {versao}...";
        (Painel, IndicePainel) = (Progresso, 1);
        var progresso = new Progress<int>(p => (Progresso.Percentual, Progresso.Texto) = (p, $"Baixando a versão {versao}... {p}%"));
        var r = await _servico.InstalarAsync(versao, progresso);
        if (!r.EhOk) Erro = r.Erro.ToString();
        (Painel, IndicePainel) = (_repouso, 0);
        Ocupado = false;
    }

    void AtualizarBotoes()
    {
        OnPropertyChanged(nameof(PodeVerificar));
        OnPropertyChanged(nameof(PodeAtualizar));
        OnPropertyChanged(nameof(PodeVoltar));
        OnPropertyChanged(nameof(TextoAtualizar));
        OnPropertyChanged(nameof(TextoVoltar));
    }
}
