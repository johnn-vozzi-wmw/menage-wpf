using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Controles;
using Menage.Core.Estado;
using Menage.Core.Operacoes;
using Menage.Core.Projetos;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>
/// Linhas da grade: marcação (ligada a <see cref="IProjetos"/>), busca e leitura da situação. A leitura é local,
/// em segundo plano, nunca durante uma operação e, ao voltar o foco, no máximo a cada 5 s.
/// </summary>
public sealed partial class GradeDeProjetosViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan IntervaloEntreLeituras = TimeSpan.FromSeconds(5);

    readonly IProjetos _projetos;
    readonly IConfiguracaoDeProjetos _configuracao;
    readonly ILeitorDeSituacao _leitor;
    readonly CoordenadorDeOperacoes _coordenador;
    readonly TimeProvider _relogio;
    readonly Dictionary<string, SituacaoDoProjeto> _lidas = new(StringComparer.OrdinalIgnoreCase);
    List<ItemDeProjeto> _todos = [];
    DateTimeOffset _ultimaLeitura = DateTimeOffset.MinValue;
    bool _sincronizando;

    [ObservableProperty]
    string _busca = "";

    [ObservableProperty]
    bool _lendo;

    public GradeDeProjetosViewModel(
        IProjetos projetos, IConfiguracaoDeProjetos configuracao, ILeitorDeSituacao leitor, CoordenadorDeOperacoes coordenador, TimeProvider? relogio = null)
    {
        (_projetos, _configuracao, _leitor, _coordenador) = (projetos, configuracao, leitor, coordenador);
        _relogio = relogio ?? TimeProvider.System;
        Reconstruir();
        _projetos.Alterado += AoAlterarProjetos;
    }

    public ObservableCollection<ItemDeProjeto> Visiveis { get; } = [];

    public int Total => _todos.Count;

    public int QuantidadeMarcada => _todos.Count(i => i.Marcado);

    public bool SemLinhas => Visiveis.Count == 0;

    public string TituloVazio => Busca.Trim().Length > 0 ? $"Nenhum projeto combina com \"{Busca.Trim()}\"" : "Nenhum projeto configurado";

    public string DescricaoVazia => Busca.Trim().Length > 0
        ? "Limpe a busca para ver todos os projetos."
        : "Use Gerenciar projetos (barra lateral) para adicionar repositórios.";

    /// <summary>Verdadeiro se todos os visíveis estão marcados; nulo se só alguns; falso se nenhum.</summary>
    public bool? TodosMarcados
    {
        get
        {
            var marcados = Visiveis.Count(i => i.Marcado);
            return Visiveis.Count > 0 && marcados == Visiveis.Count ? true : marcados == 0 ? false : null;
        }
    }

    public event Action? Lida;

    public void Dispose() => _projetos.Alterado -= AoAlterarProjetos;

    public void MarcarVisiveis(bool marcado)
    {
        _sincronizando = true;
        try
        {
            foreach (var item in Visiveis) _projetos.Marcar(item.Caminho, marcado);
        }
        finally
        {
            _sincronizando = false;
        }

        SincronizarMarcas();
    }

    [RelayCommand]
    void AlternarTodos() => MarcarVisiveis(TodosMarcados != true);

    [RelayCommand]
    public void LimparBusca() => Busca = "";

    /// <summary>F5 ou botão Atualizar: lê de novo, mesmo dentro do intervalo.</summary>
    [RelayCommand]
    public Task AtualizarAsync() => LerAsync();

    /// <summary>Janela voltou ao foco: só lê se passou o intervalo e não há operação nem leitura em curso.</summary>
    public Task AtualizarAoFocarAsync() =>
        _relogio.GetUtcNow() - _ultimaLeitura < IntervaloEntreLeituras ? Task.CompletedTask : LerAsync();

    /// <summary>Lê o estado de todos os projetos. Não faz nada durante uma operação (ela mesma pede a leitura ao terminar).</summary>
    public async Task LerAsync()
    {
        if (Lendo || _coordenador.EmAndamento) return;
        Lendo = true;
        try
        {
            foreach (var situacao in await _leitor.LerTodosAsync([.. _todos.Select(i => i.Projeto)]))
                _lidas[situacao.Projeto.Caminho] = situacao;
            foreach (var item in _todos)
                if (_lidas.TryGetValue(item.Caminho, out var lida)) item.Situacao = lida;
            _ultimaLeitura = _relogio.GetUtcNow();
            Lida?.Invoke();
        }
        finally
        {
            Lendo = false;
        }
    }

    partial void OnBuscaChanged(string value) => Filtrar();

    void AoAlterarProjetos(object? origem, EventArgs e)
    {
        if (_sincronizando) return;
        if (_todos.Select(i => i.Caminho).SequenceEqual(_projetos.Todos.Select(p => p.Caminho), StringComparer.OrdinalIgnoreCase)) SincronizarMarcas();
        else
        {
            Reconstruir();
            _ = LerAsync();
        }
    }

    void Reconstruir()
    {
        var grupos = _configuracao.Catalogo.Grupos.ToDictionary(g => g.Id);
        _todos =
        [
            .. _projetos.Todos.Select(p =>
            {
                var item = new ItemDeProjeto(p, TomDoGrupo(p, grupos), AoMarcarItem);
                if (_lidas.TryGetValue(p.Caminho, out var lida)) item.Situacao = lida;
                return item;
            }),
        ];
        SincronizarMarcas();
        Filtrar();
    }

    void AoMarcarItem(ItemDeProjeto item, bool marcado)
    {
        _sincronizando = true;
        try
        {
            _projetos.Marcar(item.Caminho, marcado);
        }
        finally
        {
            _sincronizando = false;
        }

        OnPropertyChanged(nameof(TodosMarcados));
        OnPropertyChanged(nameof(QuantidadeMarcada));
    }

    void SincronizarMarcas()
    {
        var marcados = _projetos.Marcados.Select(p => p.Caminho).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _todos) item.DefinirMarcado(marcados.Contains(item.Caminho));
        OnPropertyChanged(nameof(TodosMarcados));
        OnPropertyChanged(nameof(QuantidadeMarcada));
    }

    void Filtrar()
    {
        var busca = Busca.Trim();
        var alvo = _todos.Where(i => i.Corresponde(busca)).ToList();
        if (!alvo.SequenceEqual(Visiveis))
        {
            Visiveis.Clear();
            foreach (var item in alvo) Visiveis.Add(item);
        }

        OnPropertyChanged(nameof(SemLinhas));
        OnPropertyChanged(nameof(TituloVazio));
        OnPropertyChanged(nameof(DescricaoVazia));
        OnPropertyChanged(nameof(TodosMarcados));
        OnPropertyChanged(nameof(Total));
    }

    public static TomDeCor TomDaFamilia(string familia) => familia switch
    {
        Familias.Web => TomDeCor.GrupoWeb,
        Familias.Framework => TomDeCor.GrupoFw,
        Familias.App => TomDeCor.GrupoApp,
        Familias.FrameworkTc => TomDeCor.GrupoTc,
        _ => TomDeCor.Destaque,
    };

    static TomDeCor TomDoGrupo(Projeto projeto, Dictionary<string, GrupoDeProjetos> grupos) =>
        grupos.TryGetValue(projeto.GrupoPrincipal, out var grupo) ? TomDaFamilia(grupo.Familia) : TomDeCor.Neutro;
}
