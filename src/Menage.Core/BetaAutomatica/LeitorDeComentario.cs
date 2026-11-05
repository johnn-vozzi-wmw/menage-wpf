using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.BetaAutomatica;

public sealed class ComentarioBeta
{
    public List<string> Projetos { get; } = [];

    public string Branch { get; set; } = "";

    public DateTime Quando { get; set; }

    /// <summary>Linha explícita "BRANCH:" / "PROJETOS ALTERADOS:" ou link de pull request (um id ou nome solto no texto é fraco).</summary>
    public bool Forte { get; set; }

    /// <summary>Nada no comentário indicou a branch: vale o nome da própria atividade.</summary>
    public bool PelaChave { get; set; }

    public bool Valido => Branch.Length > 0 || Projetos.Count > 0;
}

/// <summary>Lê o comentário do Jira (BRANCH / PROJETOS ALTERADOS / link de PR) e expande curingas e siglas.</summary>
public static partial class LeitorDeComentario
{
    /// <summary>Minúscula, sem acento, sem espaço/hífen/sublinhado ("FRAMEWORK-TC", "framework tc" e "frameworktc" são iguais).</summary>
    public static string Norm(string? s) => ApenasLetras().Replace(Textos.SemAcento((s ?? "").Trim().ToLowerInvariant()), "");

    public static ComentarioBeta Ler(string? texto)
    {
        var c = new ComentarioBeta();
        texto ??= "";
        void Adicionar(string nome)
        {
            if (nome.Length > 0 && !c.Projetos.Any(x => x.Equals(nome, StringComparison.OrdinalIgnoreCase))) c.Projetos.Add(nome);
        }

        var b = ReBranch().Match(texto);
        if (b.Success) { c.Branch = b.Groups[1].Value.Trim().TrimEnd('.', ',', ';'); c.Forte = true; }

        var p = ReProjetos().Match(texto);
        if (p.Success)
        {
            c.Forte = true;
            foreach (var t in Regex.Split(p.Groups[1].Value.Replace("*", "").Replace("`", ""), @"[,;]|\s+e\s+|\s{2,}|\r|\n"))
            {
                var nome = t.Trim().Trim('.', ' ');
                if (ReSoId().IsMatch(nome)) { if (c.Branch.Length == 0) c.Branch = nome.ToUpperInvariant(); continue; }
                Adicionar(nome);
            }
        }

        foreach (Match m in RePr().Matches(texto)) { Adicionar(Regex.Replace(m.Groups[1].Value, @"\.git$", "")); c.Forte = true; }
        foreach (Match m in ReNome().Matches(texto)) Adicionar(m.Groups[1].Value);
        if (c.Branch.Length == 0 && ReId().Match(texto) is { Success: true } id) c.Branch = id.Value.ToUpperInvariant();
        return c;
    }

    /// <summary>wmwvendas = lavendereweb, wmwdados = lavenderedados, core = lavenderecore, fm-X = framework-X.</summary>
    public static string Sigla(string norm)
    {
        if (norm == "wmwvendas") return "lavendereweb";
        if (norm == "wmwdados") return "lavenderedados";
        if (norm == "core") return "lavenderecore";
        if (norm.Length > 2 && norm.StartsWith("fm", StringComparison.Ordinal) && !norm.StartsWith("framework", StringComparison.Ordinal)) return "framework" + norm[2..];
        return norm;
    }

    /// <summary>
    /// Curingas: FRAMEWORK-XXXX / FRAMEWORK-* = todos os frameworks; LAVENDERE-XXXX = todos os lavendere.
    /// Devolve os projetos a conferir (explícito = citado pelo nome).
    /// </summary>
    public static List<ProjetoDoItem> Expandir(IEnumerable<string>? tokens, IReadOnlyList<string> conhecidos)
    {
        var res = new List<ProjetoDoItem>();
        void Adicionar(string nome, bool explicito)
        {
            var ja = res.FirstOrDefault(r => r.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
            if (ja is null) res.Add(new ProjetoDoItem { Nome = nome, Explicito = explicito });
            else if (explicito) ja.Explicito = true;
        }

        foreach (var t in tokens ?? [])
        {
            var n = Norm(t);
            if (n.Length == 0) continue;
            n = Sigla(n);
            var coringa = ReCoringa().Replace(n, "");
            var ehCoringa = (n != coringa && coringa is "framework" or "lavendere") || n is "framework" or "lavendere" or "frameworks";
            if (ehCoringa)
            {
                var prefixo = n.StartsWith("framework", StringComparison.Ordinal) ? "framework" : "lavendere";
                foreach (var c in conhecidos.Where(c => Norm(c).StartsWith(prefixo, StringComparison.Ordinal))) Adicionar(c, false);
                continue;
            }

            var achou = conhecidos.FirstOrDefault(c => Norm(c) == n);
            if (achou is not null) Adicionar(achou, true);
            else if (n != Norm(t) && n.StartsWith("framework", StringComparison.Ordinal)) Adicionar("framework-" + Regex.Replace(t.Trim().ToLowerInvariant(), @"^fm[-_s]*", ""), true);
            else Adicionar(n == Norm(t) ? t.Trim().ToLowerInvariant() : n, true);
        }

        return res;
    }

    [GeneratedRegex(@"[\s\-_]+")]
    private static partial Regex ApenasLetras();

    [GeneratedRegex(@"(?im)^[\s*_#>\-]*branch[\s*_]*[:：=\-][\s*_`'""]*([A-Za-z0-9][A-Za-z0-9._/\-]*)")]
    private static partial Regex ReBranch();

    [GeneratedRegex(@"(?im)^[\s*_#>\-]*projetos?\s+alterados?[\s*_]*[:：=\-][\s*_]*(.*)$")]
    private static partial Regex ReProjetos();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9])VENDAS-\d+(?![A-Za-z0-9])")]
    private static partial Regex ReId();

    [GeneratedRegex(@"(?i)^VENDAS-\d+$")]
    private static partial Regex ReSoId();

    [GeneratedRegex(@"(?i)github\.com/[^/\s]+/([A-Za-z0-9._\-]+)/pull/\d+")]
    private static partial Regex RePr();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9])(lavendere(?:core|web|dados|pda)|wmwvendas|wmwdados|(?:framework|fm)[-_](?:integrator[-_]tc|integrator|service|core|web|util|tc)|fm[-_][a-z0-9]+)(?![A-Za-z0-9])")]
    private static partial Regex ReNome();

    [GeneratedRegex(@"x{2,}$|\*+$|x$")]
    private static partial Regex ReCoringa();
}

/// <summary>Texto sem acento, para buscas e comparações.</summary>
public static class Textos
{
    public static string SemAcento(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
