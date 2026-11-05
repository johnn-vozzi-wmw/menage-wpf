using System.Text.RegularExpressions;

namespace Menage.Core.CherryPick;

/// <summary>
/// Regra do script de banco da atividade: ele fica na branch da atividade e não vai para a develop. <c>*</c> vale como curinga,
/// e <c>%NOME_BRANCH%</c> no padrão também.
/// </summary>
public sealed record ConfigDoScript
{
    public const string PadraoDoScript = "docs/modelo-er/scripts/script_lavendere_alteracoes_%NOME_BRANCH%*";

    public const string Nome = "merge";

    public bool ExcluirScript { get; init; } = true;

    public string Padrao { get; init; } = PadraoDoScript;

    public List<string> Destinos { get; init; } = ["develop"];

    /// <summary>O commit está indo para uma branch em que o script fica de fora?</summary>
    public bool DestinoVale(string branch) =>
        !string.IsNullOrEmpty(branch) && Destinos.Any(d => Curinga(d).IsMatch(branch));

    /// <summary>O caminho (relativo ao repositório) é o script da atividade?</summary>
    public bool EhScript(string caminho) =>
        !string.IsNullOrWhiteSpace(Padrao) && !string.IsNullOrEmpty(caminho) && Curinga(Padrao).IsMatch(caminho.Trim().Replace('\\', '/'));

    static Regex Curinga(string padrao)
    {
        var p = (padrao ?? "").Trim().Replace('\\', '/').Replace("%NOME_BRANCH%", "*");
        return new Regex("^" + Regex.Escape(p).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
