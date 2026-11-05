using CommunityToolkit.Mvvm.ComponentModel;
using Menage.App.Controles;
using Menage.Core.Projetos;
using Menage.Core.Estado;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>Etiqueta pronta para a célula: texto e tom do tema.</summary>
public sealed record EtiquetaVisual(string Texto, TomDeCor Tom)
{
    public static EtiquetaVisual De(EtiquetaDoEstado etiqueta) => new(etiqueta.Texto, etiqueta.Tom switch
    {
        TomDaEtiqueta.Amarelo => TomDeCor.Amarelo,
        TomDaEtiqueta.Vermelho => TomDeCor.Vermelho,
        TomDaEtiqueta.Azul => TomDeCor.Azul,
        TomDaEtiqueta.Verde => TomDeCor.Verde,
        _ => TomDeCor.Neutro,
    });
}

/// <summary>Uma linha da grade: o projeto, se está marcado e o que foi lido dele por último.</summary>
public sealed partial class ItemDeProjeto(Projeto projeto, TomDeCor tomDoGrupo, Action<ItemDeProjeto, bool> aoMarcar) : ObservableObject
{
    [ObservableProperty]
    bool _marcado;

    [ObservableProperty]
    SituacaoDoProjeto? _situacao;

    bool _silencioso;

    public Projeto Projeto { get; } = projeto;

    public string Nome => Projeto.Nome;

    public string Caminho => Projeto.Caminho;

    public TomDeCor TomDoGrupo { get; } = tomDoGrupo;

    /// <summary>Falso até a primeira leitura (ou se ela falhou): a grade mostra a bolinha do grupo.</summary>
    public bool Lido => Situacao is { Estado.Falha.Length: 0 };

    public bool TemAlteracoes => Situacao is { } s && ApresentacaoDoEstado.TemAlteracoes(s.Estado);

    public bool Limpo => Lido && !TemAlteracoes;

    public string Branch => Situacao is { } s ? ApresentacaoDoEstado.TextoDaBranch(s.Estado) : "";

    public TipoDeBranch TipoDaBranch => Situacao is { } s ? ApresentacaoDoEstado.TipoDaBranch(s.Estado) : TipoDeBranch.Desconhecida;

    public string Versao => Situacao?.Versao ?? "";

    public IReadOnlyList<EtiquetaVisual> EtiquetasDeSituacao => Situacao is { } s ? [.. ApresentacaoDoEstado.Situacao(s.Estado).Select(EtiquetaVisual.De)] : [];

    public IReadOnlyList<EtiquetaVisual> EtiquetasDeOrigin => Situacao is { } s ? [.. ApresentacaoDoEstado.Origin(s.Estado).Select(EtiquetaVisual.De)] : [];

    public int Arquivos => Situacao is { } s ? ApresentacaoDoEstado.Arquivos(s.Estado) : 0;

    /// <summary>Vazio quando limpo.</summary>
    public string ArquivosTexto => Arquivos > 0 ? Arquivos.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";

    public string ArquivosDica => Situacao is { } s
        ? $"{s.Estado.Alteracoes} versionados alterados + {s.Estado.NaoVersionados} não versionados = {Arquivos}"
        : "";

    public string Dica => Caminho;

    public bool Corresponde(string busca) =>
        busca.Length == 0 || Contem(Nome, busca) || Contem(Branch, busca) || Contem(Caminho, busca) || Contem(Versao, busca);

    /// <summary>Muda a marca sem avisar o dono (quando é ele quem está sincronizando).</summary>
    public void DefinirMarcado(bool valor)
    {
        _silencioso = true;
        Marcado = valor;
        _silencioso = false;
    }

    partial void OnMarcadoChanged(bool value)
    {
        if (!_silencioso) aoMarcar(this, value);
    }

    partial void OnSituacaoChanged(SituacaoDoProjeto? value) => OnPropertyChanged(string.Empty);

    static bool Contem(string texto, string busca) =>
        BuscaDeComandos.Normalizar(texto).Contains(BuscaDeComandos.Normalizar(busca), StringComparison.Ordinal);
}
