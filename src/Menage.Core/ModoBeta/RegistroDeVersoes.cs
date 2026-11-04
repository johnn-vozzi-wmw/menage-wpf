namespace Menage.Core.ModoBeta;

/// <summary>Versões que os projetos principais declaram, por família e por framework, e onde elas discordam.</summary>
public sealed class RegistroDeVersoes
{
    const string RepoApp = "lavenderepda";

    readonly Dictionary<string, string> _familia = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _familiaDe = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _familiasEmConflito = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _dependencia = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _dependenciaDe = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _dependenciasEmConflito = new(StringComparer.OrdinalIgnoreCase);

    public static string FamiliaDe(Projeto principal) =>
        principal.Nome.Equals(RepoApp, StringComparison.OrdinalIgnoreCase) ? Familias.FrameworkTc : Familias.Framework;

    /// <summary>Lê o pom.xml do principal; falso se não há framework.version.</summary>
    public bool Registrar(Projeto principal)
    {
        var versao = LeitorDePom.LerFrameworkVersion(principal.Caminho);
        if (versao is null) return false;

        foreach (var (artefato, declarada) in LeitorDePom.LerDependenciasFramework(principal.Caminho))
            if (declarada is not null) Anotar(_dependencia, _dependenciaDe, _dependenciasEmConflito, artefato, declarada, principal.Nome);
        Anotar(_familia, _familiaDe, _familiasEmConflito, FamiliaDe(principal), versao, principal.Nome);
        return true;
    }

    public string? VersaoDaFamilia(string familia) => _familia.GetValueOrDefault(familia);

    public bool FamiliaEmConflito(string familia) => _familiasEmConflito.Contains(familia);

    public string OrigemDaFamilia(string familia) => _familiaDe.GetValueOrDefault(familia, "");

    public bool DependenciaEmConflito(string artefato) => _dependenciasEmConflito.Contains(artefato);

    public string OrigemDaDependencia(string artefato) => _dependenciaDe.GetValueOrDefault(artefato, "");

    /// <summary>
    /// Versão de cada framework: a declarada na dependência dos principais; o que não é declarado segue os frameworks que dependem
    /// dele, se todos estiverem na mesma versão (framework-integrator vem pelo framework-integrator-tc); senão, o framework.version.
    /// </summary>
    public Dictionary<string, (string Versao, string Motivo)> VersoesDosFrameworks(
        IReadOnlyList<Projeto> repos, string versaoDaFamilia, IReadOnlyDictionary<string, IReadOnlyCollection<string>> usos)
    {
        var versoes = new Dictionary<string, (string Versao, string Motivo)>(StringComparer.OrdinalIgnoreCase);
        foreach (var repo in repos)
            if (!DependenciaEmConflito(repo.Nome) && _dependencia.TryGetValue(repo.Nome, out var declarada))
                versoes[repo.Nome] = (declarada, "declarada no pom.xml: " + _dependenciaDe[repo.Nome]);

        while (repos.Any(r => PropagarDosDependentes(r, repos, usos, versoes))) { }

        foreach (var repo in repos) versoes.TryAdd(repo.Nome, (versaoDaFamilia, ""));
        return versoes;
    }

    bool PropagarDosDependentes(
        Projeto repo, IReadOnlyList<Projeto> repos, IReadOnlyDictionary<string, IReadOnlyCollection<string>> usos,
        Dictionary<string, (string Versao, string Motivo)> versoes)
    {
        if (versoes.ContainsKey(repo.Nome) || DependenciaEmConflito(repo.Nome)) return false;

        string[] quem = [.. repos.Where(x => usos[x.Nome].Contains(repo.Nome, StringComparer.OrdinalIgnoreCase)).Select(x => x.Nome)];
        if (quem.Length == 0 || Array.Exists(quem, q => !versoes.ContainsKey(q))) return false;

        string[] distintas = [.. quem.Select(q => versoes[q].Versao).Distinct()];
        if (distintas.Length != 1) return false;

        versoes[repo.Nome] = (distintas[0], "dependência do " + string.Join(", ", quem));
        return true;
    }

    static void Anotar(
        Dictionary<string, string> versoes, Dictionary<string, string> origens, HashSet<string> conflitos, string chave, string versao, string quem)
    {
        if (!versoes.TryGetValue(chave, out var ja))
        {
            versoes[chave] = versao;
            origens[chave] = $"{quem}={versao}";
            return;
        }

        if (ja != versao) conflitos.Add(chave);
        origens[chave] += $" {quem}={versao}";
    }
}
