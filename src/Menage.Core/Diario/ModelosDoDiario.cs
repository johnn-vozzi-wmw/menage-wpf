namespace Menage.Core.Diario;

/// <summary>Um commit seu do dia. O diff (DiffTexto e Simbolos) só é preenchido por <see cref="LeitorDeDiffs"/>.</summary>
public sealed class CommitDoDia
{
    public string Projeto { get; init; } = "";

    public string Hash { get; init; } = "";

    public string Assunto { get; init; } = "";

    public string Branch { get; init; } = "";

    public string Issue { get; set; } = "";

    public DateTime Quando { get; init; }

    public List<string> Arquivos { get; } = [];

    public int Linhas { get; set; }

    /// <summary>Só linhas +/- dos arquivos de código, sem segredos.</summary>
    public string DiffTexto { get; set; } = "";

    /// <summary>Métodos, classes e tabelas encontrados nas linhas adicionadas.</summary>
    public List<string> Simbolos { get; set; } = [];

    public bool DiffLido { get; set; }
}

public sealed class GrupoDeIssue
{
    public const string SemIssue = "(sem issue)";

    public string Issue { get; init; } = "";

    public List<CommitDoDia> Commits { get; init; } = [];

    public double Peso { get; set; }

    public string Resumo { get; set; } = "";

    public IEnumerable<string> Projetos => Commits.Select(c => c.Projeto).Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Um worklog a lançar. Hora, minutos e comentário são editáveis na tela.</summary>
public sealed class BlocoDeWorklog
{
    public string Issue { get; set; } = "";

    public string Comentario { get; set; } = "";

    public string TituloJira { get; set; } = "";

    public string WorklogId { get; set; } = "";

    public string Aviso { get; set; } = "";

    /// <summary>Commit que explica o worklog (modo por commit); vazio = todos os commits da issue.</summary>
    public string Hash { get; init; } = "";

    public string Projeto { get; init; } = "";

    public DateTime Inicio { get; set; }

    public int Minutos { get; set; }

    public bool Lancar { get; set; } = true;

    public bool Lancado { get; set; }
}

public sealed class ResultadoDoDiario
{
    public DateTime Dia { get; init; }

    public List<CommitDoDia> Commits { get; init; } = [];

    public List<GrupoDeIssue> Grupos { get; set; } = [];

    public List<BlocoDeWorklog> Blocos { get; } = [];

    /// <summary>blocos, proporcional ou por-commit.</summary>
    public string Modo { get; set; } = "";

    public string Motivo { get; set; } = "";

    public List<string> Avisos { get; } = [];

    public int Total => Blocos.Sum(b => b.Minutos);
}
