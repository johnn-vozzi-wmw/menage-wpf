using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Controles;
using Menage.Core.Banco;
using Menage.Core.Projetos;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>Cartões da barra lateral: grupos (marcação em bloco), modo beta, banco e o estado recolhido.</summary>
public sealed partial class BarraLateralViewModel : ObservableObject, IDisposable
{
    readonly IProjetos _projetos;
    readonly IConfiguracaoDeProjetos _configuracao;
    readonly IServicoDeBanco _banco;
    readonly PreferenciasDaJanelaServico _preferencias;

    [ObservableProperty]
    bool _recolhida;

    [ObservableProperty]
    string _estadoDoBanco = "";

    public BarraLateralViewModel(
        IProjetos projetos, IConfiguracaoDeProjetos configuracao, IServicoDeBanco banco, PreferenciasDaJanelaServico preferencias)
    {
        (_projetos, _configuracao, _banco, _preferencias) = (projetos, configuracao, banco, preferencias);
        _recolhida = preferencias.Valor.BarraRecolhida;
        _estadoDoBanco = banco.Estado;
        Reconstruir();
        _projetos.Alterado += AoAlterarProjetos;
        _configuracao.Alterado += AoAlterarProjetos;
    }

    public ObservableCollection<ItemDeGrupo> Grupos { get; } = [];

    public IReadOnlyList<string> PerfisDeBanco => _banco.Perfis;

    public string PerfilDeBanco
    {
        get => _banco.PerfilAtual;
        set
        {
            if (value is null || value == _banco.PerfilAtual) return;
            _banco.Escolher(value);
            OnPropertyChanged();
            EstadoDoBanco = _banco.Estado;
        }
    }

    public bool ModoBeta
    {
        get => _configuracao.Ligado;
        set
        {
            if (value == _configuracao.Ligado) return;
            _configuracao.SalvarModoBeta(value);
            OnPropertyChanged();
        }
    }

    public int TotalMarcados => _projetos.Marcados.Count;

    public event Action<string>? Falhou;

    public void Dispose()
    {
        _projetos.Alterado -= AoAlterarProjetos;
        _configuracao.Alterado -= AoAlterarProjetos;
    }

    /// <summary>
    /// Clique no grupo deixa a seleção só com ele (ou limpa, se já era só ele); Shift soma o grupo à seleção
    /// ou o tira dela quando já está inteiro marcado.
    /// </summary>
    public void AlternarGrupo(ItemDeGrupo grupo, bool somar)
    {
        if (grupo.Total == 0) return;
        if (somar)
        {
            _projetos.MarcarGrupo(grupo.Id, !grupo.Completo);
            return;
        }

        var soEle = grupo.Completo && TotalMarcados == grupo.Total;
        _projetos.MarcarGrupo(GruposEspeciais.Todos, false);
        if (!soEle) _projetos.MarcarGrupo(grupo.Id, true);
    }

    [RelayCommand]
    void LimparSelecao() => _projetos.MarcarGrupo(GruposEspeciais.Todos, false);

    [RelayCommand]
    public void Alternar()
    {
        Recolhida = !Recolhida;
        _preferencias.Alterar(p => p with { BarraRecolhida = Recolhida });
    }

    [RelayCommand]
    async Task AplicarBancoAsync()
    {
        var resultado = await _banco.AplicarAsync();
        EstadoDoBanco = _banco.Estado;
        if (!resultado.EhOk) Falhou?.Invoke(resultado.Erro.ToString());
    }

    void AoAlterarProjetos(object? origem, EventArgs e)
    {
        if (Grupos.Count == 0 || Grupos.Any(g => g.Total != Contar(g.Id)) || NomesDiferentes()) Reconstruir();
        else Atualizar();
        OnPropertyChanged(nameof(TotalMarcados));
        OnPropertyChanged(nameof(ModoBeta));
    }

    bool NomesDiferentes() => !Grupos.Select(g => g.Id).SequenceEqual(IdsVisiveis());

    IEnumerable<string> IdsVisiveis()
    {
        foreach (var grupo in _configuracao.Catalogo.Grupos)
            if (Contar(grupo.Id) > 0) yield return grupo.Id;
        if (Contar(GruposEspeciais.SemGrupo) > 0) yield return GruposEspeciais.SemGrupo;
        yield return GruposEspeciais.Todos;
    }

    void Reconstruir()
    {
        Grupos.Clear();
        foreach (var id in IdsVisiveis())
        {
            var familia = _configuracao.Catalogo.Obter(id)?.Familia ?? "";
            var tom = id == GruposEspeciais.Todos ? TomDeCor.Destaque : id == GruposEspeciais.SemGrupo ? TomDeCor.Neutro : GradeDeProjetosViewModel.TomDaFamilia(familia);
            Grupos.Add(new ItemDeGrupo(id, id == GruposEspeciais.Todos ? "Todos os projetos" : _configuracao.Catalogo.NomeDoGrupo(id), tom));
        }

        Atualizar();
    }

    void Atualizar()
    {
        var marcados = _projetos.Marcados.Select(p => p.Caminho).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var grupo in Grupos)
        {
            var doGrupo = _projetos.DoGrupo(grupo.Id);
            (grupo.Total, grupo.Marcados) = (doGrupo.Count, doGrupo.Count(p => marcados.Contains(p.Caminho)));
        }
    }

    int Contar(string id) => _projetos.DoGrupo(id).Count;
}
