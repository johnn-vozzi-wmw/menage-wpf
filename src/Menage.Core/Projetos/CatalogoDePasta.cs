namespace Menage.Core.Projetos;

/// <summary>
/// Catálogo de teste a partir de uma pasta com repositórios (<c>--projetos &lt;pasta&gt;</c>). Aceita os repositórios direto na pasta ou
/// numa subpasta por grupo (<c>web</c>, <c>framework</c>, <c>app</c>/<c>pda</c>); os demais ficam "sem grupo".
/// </summary>
public static class CatalogoDePasta
{
    static readonly Dictionary<string, string> _familiaPorPasta = new(StringComparer.OrdinalIgnoreCase)
    {
        ["web"] = Familias.Web,
        ["framework"] = Familias.Framework,
        ["app"] = Familias.App,
        ["pda"] = Familias.App,
    };

    public static CatalogoDeProjetos Criar(string pasta)
    {
        var raiz = Path.GetFullPath(pasta);
        Dictionary<string, List<string>> porGrupo = [];
        List<string> avulsos = [];
        foreach (var filha in Subpastas(raiz))
        {
            if (ConsultasGit.EhRepositorio(filha)) avulsos.Add(filha);
            else if (_familiaPorPasta.TryGetValue(Path.GetFileName(filha), out var familia))
                foreach (var neta in Subpastas(filha).Where(ConsultasGit.EhRepositorio))
                    (porGrupo.TryGetValue(familia, out var lista) ? lista : porGrupo[familia] = []).Add(neta);
        }

        var grupos = CatalogoDeProjetos.Padrao().Grupos
            .Select(g => g with { Projetos = porGrupo.GetValueOrDefault(g.Familia) ?? [] })
            .ToList();
        return new CatalogoDeProjetos { Grupos = grupos, Repositorios = avulsos };
    }

    static IEnumerable<string> Subpastas(string pasta)
    {
        try
        {
            return Directory.GetDirectories(pasta).Order(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            return [];
        }
    }
}
