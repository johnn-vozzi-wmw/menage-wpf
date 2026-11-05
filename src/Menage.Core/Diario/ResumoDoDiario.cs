using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.Diario;

/// <summary>Texto do worklog a partir do que foi lido nos commits (nada é inventado) e o texto pronto para colar no Jira.</summary>
public static class ResumoDoDiario
{
    /// <summary>Mensagem sem o id, sem prefixos tipo "fix:" e com a primeira letra maiúscula.</summary>
    public static string LimparMensagem(string assunto, string issue)
    {
        var s = assunto;
        if (issue.Length > 0) s = Regex.Replace(s, Regex.Escape(issue), "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"^\s*[\[\(]?\s*[-:–—,.]*\s*", "").Trim();
        s = s.TrimStart('-', ':', ' ', '.', ',', ']', ')').Trim();
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    }

    /// <summary>
    /// Resumo em texto corrido: mensagens, métodos/classes/tabelas encontrados nas linhas adicionadas, arquivos e projetos.
    /// A IA, quando ligada e pedida, descreve melhor o comportamento.
    /// </summary>
    public static string PorRegras(GrupoDeIssue g)
    {
        var frases = new List<string>();
        var msgs = new List<string>();
        foreach (var c in g.Commits)
        {
            var m = LimparMensagem(c.Assunto, g.Issue).TrimEnd('.');
            if (m.Length == 0 || msgs.Exists(x => string.Equals(x, m, StringComparison.OrdinalIgnoreCase))) continue;
            msgs.Add(m);
        }

        frases.Add(msgs.Count == 0
            ? $"{g.Commits.Count} commit(s) sem mensagem descritiva."
            : string.Join("; ", msgs.Take(5)) + (msgs.Count > 5 ? $"; e mais {msgs.Count - 5} ajuste(s)" : "") + ".");
        var simbolos = g.Commits.SelectMany(c => c.Simbolos).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (simbolos.Count > 0)
            frases.Add("Código alterado/criado: " + string.Join(", ", simbolos.Take(8)) + (simbolos.Count > 8 ? $" (+{simbolos.Count - 8})" : "") + ".");
        var arqs = g.Commits.SelectMany(c => c.Arquivos).Select(Path.GetFileName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (arqs.Count > 0)
            frases.Add($"Arquivos alterados ({arqs.Count}): " + string.Join(", ", arqs.Take(6)) + (arqs.Count > 6 ? $" (+{arqs.Count - 6})" : "") + ".");
        var projs = string.Join(", ", g.Projetos);
        if (projs.Length > 0) frases.Add($"Projetos: {projs}.");
        return string.Join(' ', frases);
    }

    public static string Continuacao(string texto) => texto.Length > 0 ? texto + " (continuação do worklog anterior)" : "";

    /// <summary>O grupo de commits que explica um worklog: o commit dele (modo por commit) ou todos os commits da issue.</summary>
    public static GrupoDeIssue? GrupoDoBloco(ResultadoDoDiario r, BlocoDeWorklog b)
    {
        if (b.Hash.Length > 0)
        {
            var c = r.Commits.Find(x => x.Hash == b.Hash && string.Equals(x.Projeto, b.Projeto, StringComparison.OrdinalIgnoreCase));
            if (c is null) return null;
            var g = new GrupoDeIssue { Issue = c.Issue, Commits = [c], Peso = DistribuidorDeJornada.PesoDoCommit(c) };
            g.Resumo = PorRegras(g);
            return g;
        }

        return r.Grupos.Find(x => string.Equals(x.Issue, b.Issue, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Texto pronto para copiar e colar no Jira (um cabeçalho por worklog).</summary>
    public static string Texto(ResultadoDoDiario r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Diário de {r.Dia:dd/MM/yyyy}  -  total {ConfiguracaoDoDiario.FormatarMin(r.Total)}");
        foreach (var b in r.Blocos)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"{b.Issue}  {b.Inicio:HH:mm}  {ConfiguracaoDoDiario.FormatarMin(b.Minutos)}");
            sb.AppendLine(b.Comentario.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }

        return sb.ToString();
    }
}
