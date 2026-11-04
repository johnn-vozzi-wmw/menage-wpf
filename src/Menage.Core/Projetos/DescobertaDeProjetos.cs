namespace Menage.Core.Projetos;

/// <summary>Troca da pasta raiz: procura os projetos reconhecidos pelo nome da pasta e atualiza os caminhos no catálogo.</summary>
public static class DescobertaDeProjetos
{
    const int Profundidade = 12;

    /// <summary>Nome da pasta para o id do grupo.</summary>
    public static IReadOnlyDictionary<string, string> NomesConhecidos { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["lavenderecore"] = Familias.Web,
        ["lavendereweb"] = Familias.Web,
        ["lavenderedados"] = Familias.Web,
        ["framework-core"] = Familias.Framework,
        ["framework-web"] = Familias.Framework,
        ["framework-util"] = Familias.Framework,
        ["framework-integrator"] = Familias.Framework,
        ["framework-integrator-tc"] = Familias.Framework,
        ["framework-service"] = Familias.Framework,
        ["lavenderepda"] = Familias.App,
        ["framework-tc"] = Familias.FrameworkTc,
    };

    static readonly HashSet<string> _pastasIgnoradas = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Windows.old", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin",
        "System Volume Information", "Recovery", "PerfLogs",
    };

    /// <summary>Os projetos reconhecidos trocam de caminho; os demais grupos e projetos ficam como estão.</summary>
    public static CatalogoDeProjetos Redescobrir(CatalogoDeProjetos catalogo, string raiz)
    {
        var achados = new List<string>();
        Varrer(raiz, 0, achados);

        var grupos = catalogo.Grupos.Select(g =>
        {
            var mantidos = g.Projetos.Where(p => !NomesConhecidos.ContainsKey(Path.GetFileName(p.TrimEnd('\\'))));
            var novos = achados.Where(c => NomesConhecidos[Path.GetFileName(c)] == g.Id);
            return g with { Projetos = [.. mantidos.Concat(novos).Distinct(StringComparer.OrdinalIgnoreCase)] };
        });
        return catalogo with { Raiz = raiz, Grupos = [.. grupos] };
    }

    static void Varrer(string pasta, int nivel, List<string> achados)
    {
        if (ConsultasGit.EhRepositorio(pasta))
        {
            if (NomesConhecidos.ContainsKey(Path.GetFileName(pasta.TrimEnd('\\')))) achados.Add(ResolvedorDeProjetos.Normalizar(pasta));
            return;
        }

        if (nivel >= Profundidade) return;
        foreach (var filha in Filhas(pasta)) Varrer(filha, nivel + 1, achados);
    }

    static List<string> Filhas(string pasta)
    {
        try
        {
            return Directory.GetDirectories(pasta).Where(Visitavel).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    static bool Visitavel(string pasta)
    {
        var nome = Path.GetFileName(pasta);
        return !nome.StartsWith('.') && !_pastasIgnoradas.Contains(nome) && !File.GetAttributes(pasta).HasFlag(FileAttributes.ReparsePoint);
    }
}
