using System.Globalization;

namespace Menage.Core.Diario;

/// <summary>Reparte a jornada entre as issues (ou os commits) e monta os worklogs em sequência, com o intervalo do almoço.</summary>
public static class DistribuidorDeJornada
{
    public static double PesoDoCommit(CommitDoDia c) => 1.0 + Math.Min(c.Linhas, 400) / 200.0 + 0.1 * Math.Min(c.Arquivos.Count, 20);

    public static List<GrupoDeIssue> Agrupar(IEnumerable<CommitDoDia> commits)
    {
        var grupos = new List<GrupoDeIssue>();
        foreach (var g in commits.GroupBy(c => c.Issue.Length == 0 ? GrupoDeIssue.SemIssue : c.Issue, StringComparer.OrdinalIgnoreCase))
        {
            var gi = new GrupoDeIssue { Issue = g.Key, Commits = [.. g.OrderBy(c => c.Quando)] };
            gi.Peso = gi.Commits.Sum(PesoDoCommit);
            gi.Resumo = ResumoDoDiario.PorRegras(gi);
            grupos.Add(gi);
        }

        return [.. grupos.OrderBy(g => g.Issue == GrupoDeIssue.SemIssue ? 1 : 0).ThenByDescending(g => g.Peso)];
    }

    public static TimeSpan ParseHora(string? s) => TimeSpan.TryParse(s ?? "", CultureInfo.InvariantCulture, out var t) ? t : new TimeSpan(9, 0, 0);

    /// <summary>Proporcional à atividade; sem sinal (ou sem issue), blocos de 4h. A soma dos minutos é sempre a jornada.</summary>
    public static ResultadoDoDiario Distribuir(DateTime dia, IReadOnlyList<CommitDoDia> commits, ConfiguracaoDoDiario cfg, string? issuePadrao = null)
    {
        var r = new ResultadoDoDiario { Dia = dia.Date, Commits = [.. commits] };
        r.Grupos = Agrupar(commits);
        var comIssue = r.Grupos.Where(g => g.Issue != GrupoDeIssue.SemIssue).ToList();
        if (r.Grupos.Find(g => g.Issue == GrupoDeIssue.SemIssue) is { } sem)
            r.Avisos.Add($"{sem.Commits.Count} commit(s) sem id de issue na mensagem ou na branch: não entraram na distribuição (atribua a uma issue na tela).");

        var soma = comIssue.Sum(g => g.Peso);
        if (cfg.WorklogPorCommit && comIssue.Count > 0 && soma > 0)
        {
            PorCommit(r, cfg);
            return r;
        }

        var minutos = comIssue.Count == 0 || soma <= 0 ? Blocos(r, cfg, comIssue, commits.Count, issuePadrao) : Proporcional(r, cfg, comIssue, soma);
        Sequenciar(r, cfg, minutos.Select(m => (m.Issue, m.Min, "", "", "")).ToList());
        foreach (var b in r.Blocos) b.Comentario = r.Grupos.Find(x => string.Equals(x.Issue, b.Issue, StringComparison.OrdinalIgnoreCase))?.Resumo ?? "";
        foreach (var grupo in r.Blocos.GroupBy(b => b.Issue, StringComparer.OrdinalIgnoreCase))
            foreach (var b in grupo.Skip(1)) b.Comentario = ResumoDoDiario.Continuacao(b.Comentario);
        return r;
    }

    static List<(string Issue, int Min)> Blocos(ResultadoDoDiario r, ConfiguracaoDoDiario cfg, List<GrupoDeIssue> comIssue, int totalCommits, string? issuePadrao)
    {
        r.Modo = "blocos";
        var padrao = ConfiguracaoDoDiario.FormatarMin(cfg.BlocoMin);
        r.Motivo = totalCommits == 0
            ? $"Nenhum commit seu neste dia: usei os blocos padrão de {padrao}."
            : $"Não há como medir proporção (nenhuma issue identificada): usei os blocos padrão de {padrao}.";
        var principal = comIssue.Count > 0 ? comIssue[0].Issue : issuePadrao ?? "";
        var segunda = comIssue.Count > 1 ? comIssue[1].Issue : principal;
        var lista = new List<(string, int)> { (principal, Math.Min(cfg.BlocoMin, cfg.JornadaMin)) };
        if (cfg.JornadaMin > cfg.BlocoMin) lista.Add((segunda, Math.Min(cfg.BlocoMin, cfg.JornadaMin - cfg.BlocoMin)));
        var resto = cfg.JornadaMin - 2 * cfg.BlocoMin;
        if (resto > 0) lista.Add((principal, resto)); // o que sobra vai para a issue de maior atividade
        return lista;
    }

    static List<(string Issue, int Min)> Proporcional(ResultadoDoDiario r, ConfiguracaoDoDiario cfg, List<GrupoDeIssue> comIssue, double soma)
    {
        r.Modo = "proporcional";
        r.Motivo = "Distribuído pelo tamanho da atividade (commits, linhas e arquivos) de cada issue.";
        var alvo = Alvos(comIssue.Select(g => g.Peso).ToList(), soma, cfg);
        return [.. comIssue.Select((g, i) => (g.Issue, alvo[i]))];
    }

    /// <summary>Minutos de cada peso, arredondados, com mínimo; o que sobra ou falta para fechar a jornada vai para o maior.</summary>
    static int[] Alvos(IReadOnlyList<double> pesos, double soma, ConfiguracaoDoDiario cfg)
    {
        var arred = Math.Max(1, cfg.ArredondarMin);
        var minimo = Math.Max(1, Math.Min(cfg.MinimoPorIssueMin, cfg.JornadaMin / pesos.Count));
        var alvo = new int[pesos.Count];
        for (var i = 0; i < alvo.Length; i++)
            alvo[i] = Math.Max(minimo, (int)(Math.Round(cfg.JornadaMin * pesos[i] / soma / arred) * arred));
        var maior = 0;
        for (var i = 1; i < alvo.Length; i++) if (pesos[i] > pesos[maior]) maior = i;
        alvo[maior] = Math.Max(minimo, alvo[maior] + cfg.JornadaMin - alvo.Sum());
        var ainda = cfg.JornadaMin - alvo.Sum();
        if (ainda != 0)
        {
            var outro = Array.IndexOf(alvo, alvo.Max());
            alvo[outro] = Math.Max(minimo, alvo[outro] + ainda);
        }

        return alvo;
    }

    /// <summary>Um worklog por commit: a jornada é dividida entre os commits (com issue) pelo tamanho, na ordem em que foram feitos.</summary>
    static void PorCommit(ResultadoDoDiario r, ConfiguracaoDoDiario cfg)
    {
        r.Modo = "por-commit";
        r.Motivo = "Um worklog por commit: a jornada é dividida entre os commits pelo tamanho de cada um (linhas e arquivos), na ordem em que foram feitos. Ajuste a hora e os minutos de cada um.";
        var cs = r.Commits.Where(c => c.Issue.Length > 0).OrderBy(c => c.Quando).ToList();
        var pesos = cs.Select(PesoDoCommit).ToList();
        var alvo = Alvos(pesos, pesos.Sum(), cfg);
        var itens = new List<(string, int, string, string, string)>();
        for (var i = 0; i < cs.Count; i++)
        {
            var g = new GrupoDeIssue { Issue = cs[i].Issue, Commits = [cs[i]], Peso = pesos[i] };
            itens.Add((cs[i].Issue, alvo[i], cs[i].Hash, cs[i].Projeto, ResumoDoDiario.PorRegras(g)));
        }

        Sequenciar(r, cfg, itens);
    }

    /// <summary>Cada worklog tem no máximo um bloco; quem tem mais tempo vira vários worklogs seguidos (o seguinte diz "continuação").</summary>
    static void Sequenciar(ResultadoDoDiario r, ConfiguracaoDoDiario cfg, List<(string Issue, int Min, string Hash, string Projeto, string Resumo)> itens)
    {
        var inicio = r.Dia + ParseHora(cfg.InicioDia);
        var acumulado = 0;
        var intervaloFeito = !cfg.Almoco;
        foreach (var (issue, min, hash, projeto, resumo) in itens)
        {
            var falta = min;
            var primeiro = true;
            while (falta > 0)
            {
                var fatia = Math.Min(falta, cfg.BlocoMin);
                if (!intervaloFeito && acumulado >= cfg.BlocoMin) { inicio = inicio.AddMinutes(60); intervaloFeito = true; }
                r.Blocos.Add(new BlocoDeWorklog
                {
                    Issue = issue, Inicio = inicio, Minutos = fatia, Hash = hash, Projeto = projeto,
                    Comentario = primeiro ? resumo : ResumoDoDiario.Continuacao(resumo),
                });
                primeiro = false;
                inicio = inicio.AddMinutes(fatia);
                acumulado += fatia;
                falta -= fatia;
            }
        }
    }
}
