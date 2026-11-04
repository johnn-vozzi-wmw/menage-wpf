using System.Text.RegularExpressions;

namespace Menage.Core.Projetos;

/// <summary>Famílias técnicas do modo beta. Sobrevivem à troca de nome do grupo.</summary>
public static class Familias
{
    public const string Web = "WEB";
    public const string Framework = "FRAMEWORK";
    public const string App = "APP";
    public const string FrameworkTc = "FRAMEWORK-TC";

    public static IReadOnlyList<string> Todas { get; } = [Web, Framework, App, FrameworkTc];
}

public static class GruposEspeciais
{
    public const string Todos = "TODOS";
    public const string SemGrupo = "SEM-GRUPO";
}

/// <param name="Id">Fixo; o nome pode mudar.</param>
/// <param name="Familia">Família técnica; vazia em grupos criados pelo usuário.</param>
/// <param name="DescobertaAutomatica">Recebe os projetos que a descoberta por pasta raiz associa à família.</param>
public sealed record GrupoDeProjetos(string Id, string Nome)
{
    public string Cor { get; init; } = "";

    public string Jdk { get; init; } = "";

    public string Goals { get; init; } = "clean {modo}";

    /// <summary>Caminhos absolutos ou nomes de projeto.</summary>
    public IReadOnlyList<string> Projetos { get; init; } = [];

    /// <summary>Padrões de nome com <c>*</c> e <c>?</c>.</summary>
    public IReadOnlyList<string> Padroes { get; init; } = [];

    public string Familia { get; init; } = "";

    public bool DescobertaAutomatica { get; init; } = true;
}

/// <summary>Formato próprio do Menage (<c>projetos.json</c>). Nada aqui lê arquivos do Git Manager antigo.</summary>
public sealed record CatalogoDeProjetos
{
    public const string RaizPadrao = @"C:\wmw\dev\dev1";

    /// <summary>Pasta com <c>web</c>, <c>framework</c> e <c>pda</c>; vazia desliga essa descoberta.</summary>
    public string Raiz { get; init; } = "";

    public bool ModoBeta { get; init; }

    public IReadOnlyList<GrupoDeProjetos> Grupos { get; init; } = [];

    public IReadOnlyList<string> PastasDeBusca { get; init; } = [];

    public IReadOnlyList<string> Repositorios { get; init; } = [];

    /// <summary>Caminho do projeto para o id do grupo principal.</summary>
    public IReadOnlyDictionary<string, string> Principais { get; init; } = new Dictionary<string, string>();

    public static CatalogoDeProjetos Padrao() => new()
    {
        Raiz = RaizPadrao,
        Grupos =
        [
            new(Familias.Web, Familias.Web) { Familia = Familias.Web, Jdk = "17" },
            new(Familias.Framework, Familias.Framework) { Familia = Familias.Framework, Jdk = "17" },
            new(Familias.App, "APP / PDA") { Familia = Familias.App, Jdk = "8" },
            new(Familias.FrameworkTc, Familias.FrameworkTc) { Familia = Familias.FrameworkTc, Jdk = "8" },
        ],
    };

    public GrupoDeProjetos? Obter(string id) => Grupos.FirstOrDefault(g => g.Id == id);

    public string NomeDoGrupo(string id) => id == GruposEspeciais.SemGrupo ? "Sem grupo" : Obter(id)?.Nome ?? id;

    /// <summary>Nulo se o catálogo pode ser gravado.</summary>
    public Erro? Validar()
    {
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>();
        foreach (var grupo in Grupos)
        {
            if (string.IsNullOrWhiteSpace(grupo.Nome) || !nomes.Add(grupo.Nome.Trim()) || !IdValido(grupo.Id) || !ids.Add(grupo.Id))
                return new Erro("Grupo inválido", $"Nomes e identificadores de grupos devem ser válidos e únicos: \"{grupo.Nome}\".");
            if (grupo.Projetos.Concat(grupo.Padroes).Any(string.IsNullOrWhiteSpace))
                return new Erro("Grupo inválido", $"O grupo \"{grupo.Nome}\" tem projeto ou padrão vazio.");
            if (grupo.Cor.Length > 0 && !Regex.IsMatch(grupo.Cor, "^#[0-9a-fA-F]{6}$"))
                return new Erro("Cor inválida", $"A cor do grupo \"{grupo.Nome}\" deve ser #RRGGBB, ou vazia para acompanhar o tema.");
        }

        return PastasDeBusca.Concat(Repositorios).Any(p => !Path.IsPathRooted(p))
            ? new Erro("Caminho inválido", "Pastas de busca e repositórios precisam de caminho absoluto.")
            : null;
    }

    static bool IdValido(string id) =>
        Regex.IsMatch(id, "^[A-Za-z0-9-]+$") && id is not GruposEspeciais.Todos and not GruposEspeciais.SemGrupo;
}
