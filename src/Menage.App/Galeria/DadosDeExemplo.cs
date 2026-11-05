using Menage.Core;
using Menage.Core.Estado;
using Menage.Core.Projetos;

namespace Menage.App.Galeria;

/// <summary>Catalogo e situacoes de mentira da galeria da janela principal: nenhum repositorio real e lido.</summary>
public sealed class ConfiguracaoDeExemplo : IConfiguracaoDeProjetos
{
    static readonly (string Nome, string Familia)[] _projetos =
    [
        ("lavenderecore", Familias.Web), ("lavendereweb", Familias.Web), ("lavenderedados", Familias.Web),
        ("framework-core", Familias.Framework), ("framework-web", Familias.Framework), ("framework-util", Familias.Framework),
        ("lavenderepda", Familias.App), ("framework-tc", Familias.FrameworkTc), ("projeto-avulso", ""),
    ];

    public ConfiguracaoDeExemplo()
    {
        var padrao = CatalogoDeProjetos.Padrao();
        Catalogo = padrao;
        Projetos =
        [
            .. _projetos.Select(p =>
            {
                var grupo = padrao.Grupos.FirstOrDefault(g => g.Familia == p.Familia && p.Familia.Length > 0);
                return new Projeto(p.Nome, @"C:\exemplo\" + p.Nome, grupo is null ? [] : [grupo.Id])
                {
                    GrupoPrincipal = grupo?.Id ?? GruposEspeciais.SemGrupo,
                    Familia = p.Familia,
                };
            }),
        ];
    }

    public event EventHandler? Alterado { add { } remove { } }

    public bool Ligado { get; private set; }

    public CatalogoDeProjetos Catalogo { get; }

    public IReadOnlyList<Projeto> Projetos { get; }

    public string Aviso => "";

    public Resultado<Nada> Salvar(CatalogoDeProjetos catalogo) => Resultado.Ok();

    public Resultado<Nada> SalvarModoBeta(bool ligado)
    {
        Ligado = ligado;
        return Resultado.Ok();
    }

    public Resultado<Nada> SalvarRaiz(string raiz) => Resultado.Ok();
}

/// <summary>Uma situacao para cada tipo de linha que a grade sabe mostrar.</summary>
public sealed class LeitorDeExemplo : ILeitorDeSituacao
{
    public Task<IReadOnlyList<SituacaoDoProjeto>> LerTodosAsync(IReadOnlyList<Projeto> projetos, CancellationToken cancelar = default)
    {
        EstadoDoProjeto[] estados =
        [
            Base(projetos, 0) with { Branch = "develop", TemUpstream = true },
            Base(projetos, 1) with { Branch = "feature/TKT-1234-filtro", Alteracoes = 3, NaoVersionados = 1, TemUpstream = true, Ahead = 2, Behind = 1 },
            Base(projetos, 2) with { Branch = "main", TemUpstream = true, Behind = 4 },
            Base(projetos, 3) with { Branch = "", Detached = true, Head = "a1b2c3d", NaoVersionados = 2 },
            Base(projetos, 4) with { Branch = "release-9.4", TemUpstream = true },
            Base(projetos, 5) with { Branch = "feature/TKT-77", Operacao = "rebase em andamento", Alteracoes = 1, TemUpstream = true, Ahead = 1 },
            Base(projetos, 6) with { Branch = "develop", TemUpstream = true },
            Base(projetos, 7) with { Branch = "develop", Falha = "pasta não encontrada" },
            Base(projetos, 8) with { Branch = "master" },
        ];
        string[] versoes = ["9.4.1", "9.4.1", "9.4.0", "9.3.8", "9.4.1", "9.4.1", "2.1.0", "", "1.0.0"];
        return Task.FromResult<IReadOnlyList<SituacaoDoProjeto>>([.. estados.Select((e, i) => new SituacaoDoProjeto(e, versoes[i]))]);
    }

    static EstadoDoProjeto Base(IReadOnlyList<Projeto> projetos, int i) => new(projetos[i]);
}
