using System.Text.RegularExpressions;

namespace Menage.Core.Protecao;

/// <summary>Proteção local de branches e convenção de commit. Vale só dentro do Menage: não instala hooks nos repositórios.</summary>
public sealed record ConfigDeProtecao
{
    public static readonly IReadOnlyList<string> BranchesPadrao =
        ["main", "master", "develop", "development", "dev", "producao", "production", "homologacao", "release*"];

    public static readonly IReadOnlyList<string> TiposPadrao =
        ["feat", "fix", "docs", "style", "refactor", "perf", "test", "build", "ci", "chore", "revert"];

    public bool BloquearPush { get; init; } = true;

    public bool BloquearExclusao { get; init; } = true;

    public IReadOnlyList<string> Branches { get; init; } = BranchesPadrao;

    /// <summary>Desligada por padrão: cada equipe liga quando quiser.</summary>
    public bool ValidarCommit { get; init; }

    public IReadOnlyList<string> Tipos { get; init; } = TiposPadrao;

    /// <summary>Aceita também "VENDAS-123: mensagem".</summary>
    public bool AceitarTicket { get; init; } = true;

    /// <summary>Cada item da lista é um nome ou um padrão com <c>*</c>, sem diferenciar maiúsculas.</summary>
    public bool EhProtegida(string branch) =>
        !string.IsNullOrEmpty(branch) && new Operacoes.Decoradores.RegrasDeProtecao(Branches).Protegida(branch);

    public string DescricaoDoPadrao() =>
        string.Join(", ", Tipos.Select(t => t + ":")) + (AceitarTicket ? " ou chave de ticket (ex: VENDAS-123: ...)" : "");

    /// <summary>Nulo se a mensagem está ok (ou se a validação está desligada); senão, o motivo.</summary>
    public string? ValidarMensagem(string? mensagem)
    {
        if (!ValidarCommit) return null;

        var primeira = (mensagem ?? "").Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        if (primeira.Length == 0) return "A mensagem do commit está vazia.";

        var tipos = string.Join('|', Tipos.Select(Regex.Escape));
        if (Regex.IsMatch(primeira, "^(" + tipos + @")(\([^)\r\n]+\))?!?: \S.*", RegexOptions.IgnoreCase)) return null;
        if (AceitarTicket && Regex.IsMatch(primeira, @"^[A-Za-z][A-Za-z0-9]*-\d+\b.*\S")) return null;

        return $"A mensagem não segue o padrão da equipe. Use {DescricaoDoPadrao()}. Ex: \"feat(vendas): nova tela de pedidos\".";
    }
}
