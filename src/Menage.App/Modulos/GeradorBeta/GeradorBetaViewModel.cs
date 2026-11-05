using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.Core.Beta;
using Menage.Core.Interacao;
using Menage.Core.Projetos;

namespace Menage.App.Modulos.GeradorBeta;

/// <summary>
/// Tela do Gerador: base, modo, tabela de projetos, seleção e ordem de execução, favoritos. A geração e as entregas
/// ficam em <c>GeradorBetaViewModel.Geracao.cs</c>; toda regra de negócio está no Core.
/// </summary>
public sealed partial class GeradorBetaViewModel : ObservableObject, IDisposable
{
    readonly RepositorioDoGerador _repositorio;
    readonly PreparadorDeGeracao _preparador;
    readonly IProjetos _projetos;
    readonly IServicoDeDialogos _dialogos;
    readonly IServicoDeJanelas _janelas;
    List<string> _ordem = [];
    bool _ordemPersonalizada;
    bool _atualizando;

    [ObservableProperty]
    OpcaoDeBase _baseSelecionada = new(PreparadorDeGeracao.BaseDoMenage, "Projetos do Menage");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoGerar))]
    bool _deploy;

    [ObservableProperty]
    string _resumoDaSelecao = "";

    [ObservableProperty]
    string _mensagem = "Pronto.";

    [ObservableProperty]
    string _textoLimparTarget = "Desabilitado";

    public GeradorBetaViewModel(
        RepositorioDoGerador repositorio, PreparadorDeGeracao preparador, IProjetos projetos, IServicoDeDialogos dialogos,
        IServicoDeJanelas janelas, Core.Beta.ExecutorDoGerador executor, Core.Entregas.ServicoDeEntrega entrega, AcoesDoWindows windows)
    {
        (_repositorio, _preparador, _projetos, _dialogos, _janelas) = (repositorio, preparador, projetos, dialogos, janelas);
        (_executor, _entrega, Windows) = (executor, entrega, windows);
    }

    public ObservableCollection<LinhaDeBeta> Linhas { get; } = [];

    public ObservableCollection<OpcaoDeBase> Bases { get; } = [];

    public ObservableCollection<CartaoDeFavorito> Favoritos { get; } = [];

    public LogDoGeradorViewModel Log { get; } = new();

    public AcoesDoWindows Windows { get; }

    public bool DeployLiberado => _preparador.DeployLiberado;

    public string PastaDosLogs => _executor is null ? "" : _pastaDaGeracao ?? _preparador.PastaDosLogs;

    public IReadOnlyList<string> Grupos => [.. Linhas.Select(l => l.Grupo).Where(g => g.Length > 0).Distinct()];

    public ConfiguracaoDoGerador Configuracao => _repositorio.Configuracao;

    public async Task IniciarAsync()
    {
        var c = Configuracao;
        Deploy = c.UltimoDeploy && DeployLiberado;
        PreencherBases(c);
        await CarregarLinhasAsync(c.UltimaSelecao);
        AplicarMarcadosDaJanelaPrincipal();
    }

    void PreencherBases(ConfiguracaoDoGerador c)
    {
        var antes = _atualizando;
        _atualizando = true;
        Bases.Clear();
        Bases.Add(new OpcaoDeBase(PreparadorDeGeracao.BaseDoMenage, "Projetos do Menage"));
        foreach (var b in c.PastasBase) Bases.Add(new OpcaoDeBase(b, b));
        BaseSelecionada = Bases.FirstOrDefault(b => b.Caminho == c.UltimaBase) ?? Bases[0];
        _atualizando = antes;
    }

    /// <summary>Recarrega pastas e branches (Atualizar branches) mantendo a marcação e a ordem.</summary>
    public async Task CarregarLinhasAsync(IEnumerable<string>? marcar = null)
    {
        var marcados = (marcar ?? Linhas.Where(l => l.Marcado).Select(l => l.Nome)).ToList();
        var dados = await _preparador.LerLinhasAsync(BaseSelecionada.Caminho);
        _atualizando = true;
        foreach (var l in Linhas) l.PropertyChanged -= AoMudarLinha;
        Linhas.Clear();
        foreach (var d in dados)
        {
            var linha = new LinhaDeBeta(d.Projeto, d.Pasta, d.Branch);
            linha.PropertyChanged += AoMudarLinha;
            Linhas.Add(linha);
            linha.Marcado = d.Encontrado && marcados.Contains(d.Projeto.Nome, StringComparer.OrdinalIgnoreCase);
        }

        _ordem = [.. marcados.Where(n => Linhas.Any(l => l.Nome == n && l.Marcado))];
        _ordemPersonalizada = false;
        _atualizando = false;
        AtualizarComandos();
        SincronizarOrdem();
        OnPropertyChanged(nameof(Grupos));
        AtualizarLimparTarget();
        RecarregarFavoritos();
    }

    partial void OnDeployChanged(bool value)
    {
        if (value && !DeployLiberado) { Deploy = false; return; }
        AtualizarComandos();
        RecarregarFavoritos();
    }

    async partial void OnBaseSelecionadaChanged(OpcaoDeBase value)
    {
        if (value is null || _atualizando || Linhas.Count == 0 || Executando) return;
        await CarregarLinhasAsync();
    }

    void AtualizarComandos()
    {
        foreach (var l in Linhas) l.Comando = MontadorDeComando.Completo(l.Projeto, Configuracao, Deploy);
    }

    public async Task AdicionarBaseAsync(string pasta)
    {
        if (Executando) return;
        if (!Configuracao.PastasBase.Contains(pasta, StringComparer.OrdinalIgnoreCase))
        {
            var r = _repositorio.Salvar(Configuracao with { PastasBase = [.. Configuracao.PastasBase, pasta] });
            if (!r.EhOk) { await _dialogos.MostrarErroAsync(r.Erro); return; }
        }

        _atualizando = true;
        PreencherBases(Configuracao);
        BaseSelecionada = Bases.First(b => b.Caminho.Equals(pasta, StringComparison.OrdinalIgnoreCase));
        _atualizando = false;
        await CarregarLinhasAsync();
    }

    /// <summary>Comando digitado na grade: igual ao padrão ou vazio volta ao padrão; qualquer outro vale para o projeto.</summary>
    public async Task EditarComandoAsync(LinhaDeBeta linha, string texto)
    {
        var padrao = $"mvn {MontadorDeComando.Argumentos(linha.Projeto, Configuracao, Deploy)}";
        var r = _repositorio.DefinirComando(linha.Nome, texto.Trim() == padrao ? null : texto);
        if (!r.EhOk) await _dialogos.MostrarErroAsync(r.Erro);
        AtualizarComandos();
    }

    // ---- seleção

    void AoMudarLinha(object? origem, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LinhaDeBeta.Marcado) && !_atualizando) SincronizarOrdem();
    }

    public void Alternar(LinhaDeBeta linha)
    {
        if (!Executando && linha.Encontrado) linha.Marcado = !linha.Marcado;
    }

    public static void MarcarSelecionadas(IEnumerable<LinhaDeBeta> selecionadas)
    {
        var lista = selecionadas.Where(l => l.Encontrado).ToList();
        var marcar = lista.Any(l => !l.Marcado);
        foreach (var l in lista) l.Marcado = marcar;
    }

    public void Selecionar(string rotulo)
    {
        if (Executando) return;
        _atualizando = true;
        foreach (var l in Linhas.Where(l => l.Encontrado))
            l.Marcado = rotulo switch { "Todos" => true, "Nenhum" => false, _ => l.Grupo == rotulo || l.Marcado };
        _atualizando = false;
        SincronizarOrdem();
    }

    /// <summary>Marca só os projetos marcados na janela principal (pelo nome); sem nenhum deles, fica a seleção guardada.</summary>
    public void AplicarMarcadosDaJanelaPrincipal()
    {
        if (Executando) return;
        var nomes = _projetos.Marcados.Select(p => p.Nome).ToList();
        var daJanela = Linhas.Where(l => l.Encontrado && nomes.Contains(l.Nome, StringComparer.OrdinalIgnoreCase)).ToList();
        if (daJanela.Count == 0) return;
        _atualizando = true;
        foreach (var l in Linhas) l.Marcado = daJanela.Contains(l);
        _atualizando = false;
        _ordemPersonalizada = false;
        SincronizarOrdem();
    }

    public IReadOnlyList<string> Ordem => _ordem;

    public void DefinirOrdem(IEnumerable<string> ordem)
    {
        _ordem = [.. ordem.Distinct()];
        _ordemPersonalizada = !_ordem.SequenceEqual(_ordem.OrderBy(IndiceNaConfiguracao));
        SincronizarOrdem();
    }

    int IndiceNaConfiguracao(string nome) => Linhas.ToList().FindIndex(l => l.Nome == nome);

    /// <summary>Mantém a ordem igual ao conjunto de marcados e escreve o número na coluna Ordem.</summary>
    void SincronizarOrdem()
    {
        var marcados = Linhas.Where(l => l.Marcado).Select(l => l.Nome).ToHashSet();
        _ordem.RemoveAll(n => !marcados.Contains(n));
        _ordem.AddRange(Linhas.Where(l => l.Marcado && !_ordem.Contains(l.Nome)).Select(l => l.Nome));
        if (!_ordemPersonalizada) _ordem = [.. _ordem.OrderBy(IndiceNaConfiguracao)];
        foreach (var l in Linhas)
        {
            var i = _ordem.IndexOf(l.Nome);
            l.Ordem = i < 0 ? "" : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var semPasta = Linhas.Count(l => !l.Encontrado);
        ResumoDaSelecao = $"{marcados.Count} de {Linhas.Count} selecionado(s)" + (semPasta > 0 ? $"   •   {semPasta} sem pasta nesta base" : "");
        RecarregarFavoritos();
    }

    public async Task PedirOrdemAsync()
    {
        if (Executando) return;
        if (_ordem.Count < 2)
        {
            await _dialogos.InformarAsync("Ordem de execução", "Marque pelo menos 2 projetos para definir a ordem de execução.");
            return;
        }

        if (await _janelas.AbrirDialogoAsync(JanelaDeOrdem.Id, new List<string>(_ordem)) is List<string> nova) DefinirOrdem(nova);
    }

    // ---- limpar target

    void AtualizarLimparTarget()
    {
        var t = Configuracao.LimparTarget;
        TextoLimparTarget = t.Ativo ? $"Habilitado em {t.Projetos.Count} projeto(s)" : "Desabilitado";
    }

    public async Task EscolherLimparTargetAsync()
    {
        if (Executando) return;
        if (await _janelas.AbrirDialogoAsync(JanelaDeLimparTarget.Id, new PedidoDeLimparTarget(Configuracao.LimparTarget, Configuracao.Projetos)) is not LimparTargetConfig escolha) return;
        var r = _repositorio.DefinirLimparTarget(escolha.Ativo, escolha.Projetos);
        if (!r.EhOk) await _dialogos.MostrarErroAsync(r.Erro);
        AtualizarLimparTarget();
    }
}
