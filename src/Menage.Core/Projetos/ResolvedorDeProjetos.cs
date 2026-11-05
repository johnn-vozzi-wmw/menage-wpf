using System.Text.RegularExpressions;

namespace Menage.Core.Projetos;

/// <param name="Principais">Grupo principal de cada projeto, já corrigido.</param>
public sealed record ProjetosResolvidos(
    IReadOnlyList<Projeto> Projetos, IReadOnlyDictionary<string, string> Principais, IReadOnlyList<string> Avisos);

/// <summary>Do catálogo para a lista de projetos: pasta raiz, repositórios avulsos, pastas de busca e regras dos grupos.</summary>
public static class ResolvedorDeProjetos
{
    sealed record Achado(string Caminho, string Familia);

    /// <summary>Curingas <c>*</c> e <c>?</c>, sem diferenciar maiúsculas.</summary>
    public static bool Casa(string nome, string padrao) =>
        Regex.IsMatch(nome, "^" + Regex.Escape(padrao).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Normalizar(string caminho) => Path.GetFullPath(caminho).TrimEnd('\\');

    public static ProjetosResolvidos Resolver(CatalogoDeProjetos catalogo)
    {
        var achados = new List<Achado>();
        var avisos = new List<string>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Adicionar(string caminho, string familia)
        {
            try
            {
                var completo = Normalizar(caminho);
                if (ConsultasGit.EhRepositorio(completo) && vistos.Add(completo)) achados.Add(new(completo, familia));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                avisos.Add($"{caminho}: {ex.Message}");
            }
        }

        DescobrirNaRaiz(catalogo.Raiz, Adicionar);
        foreach (var caminho in catalogo.Repositorios.Concat(catalogo.Grupos.SelectMany(g => g.Projetos).Where(Path.IsPathRooted)))
            Adicionar(caminho, "");

        var visitadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raiz in catalogo.PastasDeBusca) Buscar(raiz, c => Adicionar(c, ""), visitadas, avisos);

        var principais = new Dictionary<string, string>(catalogo.Principais, StringComparer.OrdinalIgnoreCase);
        var projetos = achados.ConvertAll(a => Montar(a, catalogo, principais));
        return new(projetos, principais, avisos);
    }

    static Projeto Montar(Achado achado, CatalogoDeProjetos catalogo, Dictionary<string, string> principais)
    {
        var nome = Path.GetFileName(achado.Caminho);
        var porRegra = catalogo.Grupos.Where(g => PorRegra(g, achado.Caminho, nome)).ToList();
        var familia = achado.Familia.Length > 0 ? achado.Familia : porRegra.Find(g => g.Familia.Length > 0)?.Familia ?? "";

        string[] grupos = [.. catalogo.Grupos
            .Where(g => porRegra.Contains(g) || (g.DescobertaAutomatica && g.Familia.Length > 0 && g.Familia == achado.Familia))
            .Select(g => g.Id)];
        var principal = principais.TryGetValue(achado.Caminho, out var salvo) && grupos.Contains(salvo)
            ? salvo
            : grupos.FirstOrDefault() ?? GruposEspeciais.SemGrupo;

        principais[achado.Caminho] = principal;
        return new Projeto(nome, achado.Caminho, grupos) { GrupoPrincipal = principal, Familia = familia };
    }

    static bool PorRegra(GrupoDeProjetos grupo, string caminho, string nome) =>
        grupo.Projetos.Any(p => p.Equals(caminho, StringComparison.OrdinalIgnoreCase) || p.Equals(nome, StringComparison.OrdinalIgnoreCase)) ||
        grupo.Padroes.Any(p => Casa(nome, p));

    // Organização da pasta raiz: web\*, framework\*, pda\lavenderepda e pda\framework-tc.
    static void DescobrirNaRaiz(string raiz, Action<string, string> adicionar)
    {
        if (string.IsNullOrWhiteSpace(raiz)) return;
        foreach (var pasta in Subpastas(Path.Combine(raiz, "web"))) adicionar(pasta, Familias.Web);
        foreach (var pasta in Subpastas(Path.Combine(raiz, "framework"))) adicionar(pasta, Familias.Framework);
        adicionar(Path.Combine(raiz, "pda", "lavenderepda"), Familias.App);
        adicionar(Path.Combine(raiz, "pda", "framework-tc"), Familias.FrameworkTc);
    }

    static IEnumerable<string> Subpastas(string pasta)
    {
        try
        {
            return Directory.Exists(pasta) ? Directory.GetDirectories(pasta).Order(StringComparer.OrdinalIgnoreCase) : [];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    // Continua dentro dos repositórios (aninhados e submódulos); não entra em .git nem segue junções.
    static void Buscar(string pasta, Action<string> adicionar, HashSet<string> visitadas, List<string> avisos)
    {
        try
        {
            var caminho = Normalizar(pasta);
            if (Path.GetFileName(caminho).Equals(".git", StringComparison.OrdinalIgnoreCase) || !visitadas.Add(caminho)) return;
            if (File.GetAttributes(caminho).HasFlag(FileAttributes.ReparsePoint)) return;
            if (ConsultasGit.EhRepositorio(caminho)) adicionar(caminho);
            foreach (var filha in Directory.GetDirectories(caminho)) Buscar(filha, adicionar, visitadas, avisos);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            avisos.Add($"Busca: {pasta}: {ex.Message}");
        }
    }
}
