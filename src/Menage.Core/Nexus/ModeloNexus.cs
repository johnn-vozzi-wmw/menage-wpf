using System.Text.RegularExpressions;

namespace Menage.Core.Nexus;

public sealed record ArquivoNexus(string Caminho, string Url, string Extensao, long Tamanho);

public sealed record ComponenteNexus(string Repositorio, string Grupo, string Nome, string Versao, string Formato, DateTime? Atualizado, IReadOnlyList<ArquivoNexus> Arquivos)
{
    public string Chave => $"{Repositorio}|{Grupo}|{Nome}|{Versao}";
}

/// <summary>Conjunto de names salvo (ex.: "Pacote web" = wmwvendas, lavenderecore, wmwdados).</summary>
public sealed record FavoritoNexus
{
    public string Nome { get; init; } = "";

    public IReadOnlyList<string> Names { get; init; } = [];
}

/// <summary>Configuração do servidor, em <c>nexus.json</c> na pasta de dados. A senha só existe protegida (DPAPI do usuário).</summary>
public sealed record ConfiguracaoDoNexus
{
    public const string NomeDoArquivo = "nexus";

    public string Url { get; init; } = "";

    public string Repositorio { get; init; } = "";

    public string Usuario { get; init; } = "";

    public string SenhaProtegida { get; init; } = "";

    public string Destino { get; init; } = "";

    public string Extensoes { get; init; } = "jar,war";

    /// <summary>Cada download vai para uma subpasta <c>beta_Nexus_&lt;versões&gt;</c>.</summary>
    public bool SubpastaAuto { get; init; } = true;

    public IReadOnlyList<FavoritoNexus> Favoritos { get; init; } = [];

    public bool Completa => Url.Length > 0 && Usuario.Length > 0 && SenhaProtegida.Length > 0;

    public IReadOnlySet<string> ExtensoesLista =>
        Extensoes.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.TrimStart('.').ToLowerInvariant()).ToHashSet();

    public static ConfiguracaoDoNexus Padrao() => new();
}

public sealed record ConsultaNexus(string Names = "", string Grupo = "", string Versao = "", string Repositorio = "")
{
    public bool Vazia => Names.Trim().Length == 0 && Grupo.Trim().Length == 0 && Versao.Trim().Length == 0 && Repositorio.Trim().Length == 0;
}

public sealed record PaginaNexus(IReadOnlyList<ComponenteNexus> Componentes, string? ProximoToken);

/// <summary>Versões comparadas pelos números, não pelo texto: 9.060-13 vem depois de 9.060-3.</summary>
public static partial class VersaoNatural
{
    public static string Chave(string? versao)
    {
        var partes = Numeros().Split(versao ?? "");
        return string.Concat(partes.Select(p => long.TryParse(p, out var n) ? n.ToString("D12", System.Globalization.CultureInfo.InvariantCulture) : p));
    }

    public static IComparer<string> Comparador { get; } = Comparer<string>.Create((a, b) => string.CompareOrdinal(Chave(a), Chave(b)));

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex Numeros();
}
