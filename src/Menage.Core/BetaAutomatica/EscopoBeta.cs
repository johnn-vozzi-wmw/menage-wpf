namespace Menage.Core.BetaAutomatica;

/// <summary>Resultado da pergunta "esta atividade entra na Beta automática?".</summary>
public sealed record EscopoBeta(bool Entra, string Motivo, ComentarioBeta? Comentario = null)
{
    static bool EhMeu(string usuario, string eu) => usuario.Length > 0 && usuario.Equals(eu, StringComparison.OrdinalIgnoreCase);

    /// <summary>Comentário mais recente, preferindo os que têm linha explícita (BRANCH / PROJETOS ALTERADOS / link de PR).</summary>
    static ComentarioBeta? Achar(IEnumerable<ComentarioJira> comentarios)
    {
        ComentarioBeta? fraco = null;
        foreach (var c in comentarios.OrderByDescending(c => c.Quando))
        {
            var p = LeitorDeComentario.Ler(c.Corpo);
            if (!p.Valido) continue;
            p.Quando = c.Quando;
            if (p.Forte) return p;
            fraco ??= p;
        }

        return fraco;
    }

    /// <summary>Se nada indicou a branch, vale o nome da atividade (a subtask usa a da história).</summary>
    static ComentarioBeta Escolher(IssueJira i, IEnumerable<ComentarioJira> comentarios)
    {
        var c = Achar(comentarios) ?? new ComentarioBeta();
        if (c.Branch.Length == 0) { c.Branch = i.EhSubtask && i.Pai.Length > 0 ? i.Pai : i.Chave; c.PelaChave = true; }
        return c;
    }

    /// <summary>
    /// Tester: toda atividade que PASSOU de "In Code Review" para o status alvo. Escopo "atribuídas": só as do tester atual.
    /// A branch vem do comentário mais recente de QUALQUER pessoa (o dev escreve).
    /// </summary>
    public static EscopoBeta AvaliarTester(IssueJira i, string eu, ConfigBetaAuto cfg)
    {
        var t = i.UltimaPassagemPara(cfg.StatusAlvo);
        if (t is null || !t.DeNome.Equals(cfg.StatusCodeReview, StringComparison.OrdinalIgnoreCase))
            return new(false, $"fora do escopo (não veio de {cfg.StatusCodeReview} para {cfg.StatusAlvo})");

        var minha = EhMeu(i.Responsavel, eu);
        if (!cfg.TesterEscopo.Equals("todas", StringComparison.OrdinalIgnoreCase) && !minha)
            return new(false, "fora do escopo (atribuída a outra pessoa; o escopo está em \"somente atribuídas a mim\")");

        var dono = minha ? " (atribuída a você)" : i.Responsavel.Length > 0 ? $" (atribuída a {i.Responsavel})" : " (sem responsável)";
        return new(true, $"passou de {cfg.StatusCodeReview} para {cfg.StatusAlvo}{dono}", Escolher(i, i.Comentarios));
    }

    /// <summary>
    /// Desenvolvedor (nesta ordem): subtask Coding atribuída a mim; atividade com comentário MEU no formato PROJETOS ALTERADOS/BRANCH;
    /// atividade em que eu era o responsável quando ela saiu de In Development para Waiting for Code Review.
    /// </summary>
    public static EscopoBeta Avaliar(IssueJira i, string eu, ConfigBetaAuto cfg)
    {
        var meusComentarios = i.Comentarios.Where(c => EhMeu(c.Autor, eu)).ToList();
        var meu = Achar(meusComentarios);
        if (meu is { Forte: false }) meu = null;
        var lido = Escolher(i, meusComentarios);

        if (i.EhSubtask && cfg.Tipos.Contains(LeitorDeComentario.Norm(i.Tipo)) && EhMeu(i.Responsavel, eu))
            return new(true, $"subtask {i.Tipo} atribuída a você", lido);
        if (meu is { } m && (m.Projetos.Count > 0 || m.Branch.Length > 0))
            return new(true, "comentário seu com PROJETOS ALTERADOS/BRANCH", lido);

        var devParaRevisao = i.Historico
            .Where(h => h.Campo == "status" && h.DeNome.Equals(cfg.StatusDev, StringComparison.OrdinalIgnoreCase) && h.ParaNome.Equals(cfg.StatusRevisao, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(h => h.Quando).FirstOrDefault();
        if (devParaRevisao is not null && EhMeu(i.ResponsavelEm(devParaRevisao.Quando), eu))
            return new(true, $"você era o responsável quando saiu de {cfg.StatusDev} para {cfg.StatusRevisao}", lido);

        return new(false, "fora do escopo (não é sua subtask de coding, sem comentário seu no formato e sem passagem sua por " + $"{cfg.StatusDev} > {cfg.StatusRevisao})", lido);
    }
}
