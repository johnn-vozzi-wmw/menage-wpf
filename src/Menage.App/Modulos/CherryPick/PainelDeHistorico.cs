using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Perfis;
using Menage.Core.Projetos;
using Menage.Core.Repos;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.Core.CherryPick;
using Menage.Core.Historico;

namespace Menage.App.Modulos.CherryPick;

/// <summary>Uma linha da grade. A marcação vive no painel (por hash) para sobreviver a filtros e recargas.</summary>
public sealed partial class LinhaDeCommit(CommitLinha commit, int indice, bool marcado) : ObservableObject
{
    [ObservableProperty]
    bool _marcado = marcado;

    public CommitLinha Commit { get; } = commit;

    public int Indice { get; } = indice;

    public string Curto => Commit.Curto;

    public string Autor => Commit.Autor;

    public DateTime Data => Commit.Data;

    public string Assunto => Commit.Assunto;

    public string Refs => Commit.Refs;

    public bool EhMerge => Commit.Merge;

    internal Action<LinhaDeCommit>? AoMarcar { get; set; }

    partial void OnMarcadoChanged(bool value) => AoMarcar?.Invoke(this);
}

/// <summary>
/// O histórico de um projeto: lê o git log uma vez, monta o índice em memória e filtra sem tocar no git. A grade só vê as linhas
/// do filtro (virtualizadas pelo DataGrid).
/// </summary>
public sealed partial class PainelDeHistorico(Projeto projeto, LeitorDeHistorico leitor, LeitorDeDetalhes detalhes) : ObservableObject, IDisposable
{
    readonly HashSet<string> _marcados = [];
    List<CommitLinha> _commits = [];
    IndiceCommits _indice = new([]);
    FiltroHistorico _filtro = new();
    CancellationTokenSource? _detalheEmCurso;

    [ObservableProperty]
    ICollectionView? _visao;

    [ObservableProperty]
    string _status = "Carregando...";

    [ObservableProperty]
    LinhaDeCommit? _selecionada;

    [ObservableProperty]
    string _detalheTexto = "";

    [ObservableProperty]
    string _branchesTexto = "";

    [ObservableProperty]
    bool _visivel = true;

    public Projeto Projeto { get; } = projeto;

    public int QuantidadeMarcados => _marcados.Count;

    public event Action? MarcacaoMudou;

    /// <summary>Marcados do mais antigo para o mais novo, que é a ordem de aplicar.</summary>
    public IReadOnlyList<CommitLinha> Marcados => [.. _commits.Where(c => _marcados.Contains(c.Hash)).Reverse()];

    public async Task CarregarAsync(string escopo, bool merges)
    {
        Status = "Carregando...";
        var lido = await leitor.LerAsync(Projeto.Caminho, escopo, merges);
        if (!lido.EhOk)
        {
            (_commits, Status) = ([], lido.Erro.Causa);
            Visao = null;
            return;
        }

        _commits = [.. lido.Valor];
        _indice = await Task.Run(() => new IndiceCommits(_commits));
        _marcados.IntersectWith(_commits.Select(c => c.Hash));
        Aplicar(_filtro);
    }

    public void Aplicar(FiltroHistorico filtro)
    {
        _filtro = filtro;
        var linhas = _indice.Buscar(filtro).Select(Linha).ToList();
        var visao = new ListCollectionView(linhas);
        if (Visao is ListCollectionView antiga) foreach (var s in antiga.SortDescriptions) visao.SortDescriptions.Add(s);
        Visao = visao;
        Status = linhas.Count == _commits.Count ? $"{_commits.Count} commits" : $"{linhas.Count} de {_commits.Count} commits";
    }

    /// <summary>Marca ou desmarca todas as linhas dadas (Espaço com várias selecionadas).</summary>
    public static void Alternar(IEnumerable<LinhaDeCommit> linhas)
    {
        var lista = linhas.ToList();
        if (lista.Count == 0) return;
        var valor = !lista[0].Marcado;
        foreach (var l in lista) l.Marcado = valor;
    }

    public void DesmarcarTudo()
    {
        _marcados.Clear();
        if (Visao is not null) foreach (LinhaDeCommit l in Visao.SourceCollection) l.Marcado = false;
        MarcacaoMudou?.Invoke();
    }

    public void Dispose() => _detalheEmCurso?.Cancel();

    public async Task VerBranchesAsync()
    {
        if (Selecionada is not { } linha) return;
        BranchesTexto = "Procurando...";
        var limpas = LeitorDeHistorico.BranchesLimpas(await leitor.BranchesDoCommitAsync(Projeto.Caminho, linha.Commit.Hash));
        BranchesTexto = limpas.Count == 0 ? "Nenhuma branch contém este commit (só tags ou stash)." : $"Branches que contêm ({limpas.Count}):\n" + string.Join("\n", limpas);
    }

    partial void OnSelecionadaChanged(LinhaDeCommit? value) => _ = CarregarDetalheAsync(value);

    LinhaDeCommit Linha(int indice)
    {
        var commit = _commits[indice];
        return new LinhaDeCommit(commit, indice, _marcados.Contains(commit.Hash)) { AoMarcar = AoMarcar };
    }

    void AoMarcar(LinhaDeCommit linha)
    {
        if (linha.Marcado) _marcados.Add(linha.Commit.Hash);
        else _marcados.Remove(linha.Commit.Hash);
        MarcacaoMudou?.Invoke();
    }

    async Task CarregarDetalheAsync(LinhaDeCommit? linha)
    {
        _detalheEmCurso?.Cancel();
        BranchesTexto = "";
        if (linha is null)
        {
            DetalheTexto = "";
            return;
        }

        var cancelar = _detalheEmCurso = new CancellationTokenSource();
        try
        {
            var d = await detalhes.LerAsync(Projeto.Caminho, linha.Commit.Hash, cancelar.Token);
            if (cancelar.IsCancellationRequested) return;
            var cabecalho = $"{linha.Commit.Hash}\n{linha.Autor} <{linha.Commit.Email}> · {linha.Data:dd/MM/yyyy HH:mm}\n\n";
            DetalheTexto = cabecalho + d.Mensagem + "\n\nArquivos (" + d.Arquivos.Count + "):\n" + string.Join("\n", d.Arquivos);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
