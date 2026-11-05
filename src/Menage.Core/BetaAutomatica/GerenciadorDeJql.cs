using System.Text.RegularExpressions;

namespace Menage.Core.BetaAutomatica;

/// <summary>Resultado da conferência de uma JQL: erro impede salvar; aviso só chama atenção; ambos nulos = ok.</summary>
public sealed record AvaliacaoDeJql(string? Mensagem, bool Erro);

/// <summary>
/// Consultas JQL da busca: padrões (editáveis, com "Restaurar") + personalizadas, marcadores {projeto}/{status}/{escopo}, validação.
/// Só se guarda o que difere do padrão.
/// </summary>
public static partial class GerenciadorDeJql
{
    public const string TrechoNova = "{projeto} AND status = \"{status}\" AND updated >= -14d";

    public static List<JqlBeta> Padrao(bool tester) => tester
        ?
        [
            new() { Id = "tester-status", Nome = "Atividades que passaram para o status", Padrao = true, Jql = "{projeto} AND status = \"{status}\" AND updated >= -14d{escopo}" },
        ]
        :
        [
            new() { Id = "dev-minhas", Nome = "Minhas atividades no status", Padrao = true, Jql = "{projeto} AND status = \"{status}\" AND (assignee = currentUser() OR assignee WAS currentUser()) AND updated >= -14d" },
            new() { Id = "dev-comentario", Nome = "Com comentário PROJETOS ALTERADOS", Padrao = true, Jql = "{projeto} AND status = \"{status}\" AND comment ~ \"PROJETOS ALTERADOS\" AND updated >= -14d" },
        ];

    /// <summary>Padrão (com o que o usuário editou/desativou) + personalizadas, na ordem de execução.</summary>
    public static List<JqlBeta> Efetivas(ConfigBetaAuto cfg, bool tester)
    {
        var res = new List<JqlBeta>();
        foreach (var p in Padrao(tester))
        {
            if (cfg.Jqls.FirstOrDefault(x => x.Id == p.Id) is { } o)
            {
                if (!string.IsNullOrWhiteSpace(o.Jql)) p.Jql = o.Jql;
                p.Ativa = o.Ativa;
            }

            res.Add(p);
        }

        res.AddRange(cfg.Jqls.Where(c => !c.Padrao && !string.IsNullOrWhiteSpace(c.Jql))
            .Select(c => new JqlBeta { Id = c.Id, Nome = c.Nome, Jql = c.Jql, Ativa = c.Ativa }));
        return res;
    }

    /// <summary>O que gravar: só as padrão que diferem (ou desligadas) e as personalizadas; as padrão do outro papel são mantidas.</summary>
    public static List<JqlBeta> ParaGravar(ConfigBetaAuto cfg, IEnumerable<JqlBeta> efetivas, bool tester)
    {
        var padrao = Padrao(tester);
        var novo = new List<JqlBeta>();
        foreach (var j in efetivas)
        {
            if (padrao.FirstOrDefault(x => x.Id == j.Id) is { } p)
            {
                if (j.Jql.Trim() != p.Jql.Trim() || !j.Ativa)
                    novo.Add(new JqlBeta { Id = p.Id, Nome = p.Nome, Padrao = true, Ativa = j.Ativa, Jql = j.Jql.Trim() == p.Jql.Trim() ? "" : j.Jql.Trim() });
            }
            else if (!j.Padrao)
            {
                novo.Add(new JqlBeta { Id = j.Id.Length > 0 ? j.Id : "c-" + Guid.NewGuid().ToString("N")[..8], Nome = j.Nome, Jql = j.Jql.Trim(), Ativa = j.Ativa });
            }
        }

        novo.AddRange(cfg.Jqls.Where(o => o.Padrao && !padrao.Any(x => x.Id == o.Id)));
        return novo;
    }

    /// <summary>Troca os marcadores pelos valores da configuração.</summary>
    public static string Expandir(string? texto, ConfigBetaAuto cfg, bool tester)
    {
        var chaves = string.IsNullOrWhiteSpace(cfg.ChavesJira)
            ? ""
            : "project in (" + string.Join(",", cfg.ChavesJira.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)) + ")";
        var alvo = cfg.StatusAlvo.Replace("\"", "");
        var soMinhas = tester && !cfg.TesterEscopo.Equals("todas", StringComparison.OrdinalIgnoreCase);
        var t = (texto ?? "").Trim();
        t = chaves.Length == 0 ? t.Replace("{projeto} AND ", "").Replace("{projeto}", "") : t.Replace("{projeto}", chaves);
        return t.Replace("{status}", alvo).Replace("{escopo}", soMinhas ? " AND assignee = currentUser()" : "").Trim();
    }

    /// <summary>As consultas do ciclo (as mesmas no ciclo real e no diagnóstico): as ativas, expandidas. Nenhuma ativa = as padrão.</summary>
    public static List<string> Consultas(ConfigBetaAuto cfg, bool tester)
    {
        var ativas = Efetivas(cfg, tester).Where(j => j.Ativa && !string.IsNullOrWhiteSpace(j.Jql)).ToList();
        if (ativas.Count == 0) ativas = Padrao(tester);
        return [.. ativas.Select(j => Expandir(j.Jql, cfg, tester))];
    }

    /// <summary>Confere o texto antes de salvar/testar.</summary>
    public static AvaliacaoDeJql Avaliar(string? texto)
    {
        var t = (texto ?? "").Trim();
        if (t.Length == 0) return new("A JQL está vazia.", true);
        int abre = t.Count(c => c == '('), fecha = t.Count(c => c == ')');
        if (abre != fecha) return new($"Parênteses desbalanceados: {abre} \"(\" e {fecha} \")\".", true);
        if (t.Count(c => c == '"') % 2 != 0) return new("Aspas desbalanceadas (falta fechar um texto entre aspas).", true);
        foreach (Match m in Marcador().Matches(t))
            if (m.Groups[1].Value is not ("projeto" or "status" or "escopo"))
                return new($"Marcador desconhecido: {{{m.Groups[1].Value}}} (use {{projeto}}, {{status}} ou {{escopo}}).", true);
        if (OrderByNoMeio().IsMatch(t)) return new("\"order by\" precisa ficar no fim da consulta.", true);
        if (!TemStatus().IsMatch(t)) return new("Sem filtro de status: a busca traz atividades de qualquer status e o programa descarta as que não estão no status monitorado (consulta mais pesada).", false);
        if (!TemData().IsMatch(t)) return new("Sem limite de data (ex.: updated >= -14d): pode trazer muitas atividades antigas (a busca lê até 200 por consulta).", false);
        return new(null, false);
    }

    [GeneratedRegex(@"\{([^}]*)\}")]
    private static partial Regex Marcador();

    [GeneratedRegex(@"(?i)\border\s+by\b.*\b(and|or)\b")]
    private static partial Regex OrderByNoMeio();

    [GeneratedRegex(@"(?i)\bstatus\b|\{status\}")]
    private static partial Regex TemStatus();

    [GeneratedRegex(@"(?i)\bupdated\b|\bcreated\b|\bresolved\b")]
    private static partial Regex TemData();
}
