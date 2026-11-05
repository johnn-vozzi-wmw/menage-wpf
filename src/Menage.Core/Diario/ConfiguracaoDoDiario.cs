using System.Globalization;

namespace Menage.Core.Diario;

/// <summary>Provedor de IA (API compatível com a da OpenAI). Vazio em Url, Modelo e VarChave = padrão do provedor. A chave nunca é gravada.</summary>
public sealed record ConfiguracaoDaIa
{
    /// <summary>regras (sem IA), groq, xai, ollama ou openrouter.</summary>
    public string Provedor { get; init; } = "regras";

    public string Url { get; init; } = "";

    public string Modelo { get; init; } = "";

    /// <summary>Nome da variável de ambiente que guarda a chave (não a chave).</summary>
    public string VarChave { get; init; } = "";

    public int TimeoutSeg { get; init; } = 45;
}

/// <summary>Um worklog já enviado ao Jira: reabrir o dia não o lança de novo.</summary>
public sealed record LancamentoDoDiario(string Issue, string Data, string Inicio, int Minutos, string WorklogId, string LancadoEm);

/// <summary>
/// <c>diario.json</c>: preferências e o que já foi lançado. Nunca contém token nem chave (só os <i>nomes</i> das variáveis de ambiente).
/// </summary>
public sealed record ConfiguracaoDoDiario
{
    public const string NomeDoArquivo = "diario";

    /// <summary>Jornada do dia em minutos (8h48).</summary>
    public int JornadaMin { get; init; } = 528;

    /// <summary>Um worklog tem no máximo 4h.</summary>
    public int BlocoMin { get; init; } = 240;

    public int ArredondarMin { get; init; } = 5;

    public int MinimoPorIssueMin { get; init; } = 15;

    public string InicioDia { get; init; } = "09:00";

    /// <summary>Um worklog por commit (hora e minutos editáveis em cada um) em vez de um por issue.</summary>
    public bool WorklogPorCommit { get; init; }

    /// <summary>1h de intervalo depois do primeiro bloco de 4h (só afeta o horário de início).</summary>
    public bool Almoco { get; init; } = true;

    /// <summary>E-mail ou nome nos commits; vazio = user.email de cada projeto.</summary>
    public string Autor { get; init; } = "";

    /// <summary>Chaves de projeto do Jira reconhecidas em mensagens e branches (separadas por vírgula); vazio = qualquer CHAVE-123.</summary>
    public string ProjetosJira { get; init; } = "VENDAS";

    public string JiraUrl { get; init; } = "https://jira2.wmw.com.br";

    public string VarTokenJira { get; init; } = "JIRA_TOKEN";

    public bool UsarIa { get; init; }

    /// <summary>A IA também recebe trechos do código alterado. Desligado por padrão: o código sairia da máquina.</summary>
    public bool EnviarDiff { get; init; }

    public int MaxCharsDiff { get; init; } = 7000;

    public ConfiguracaoDaIa Ia { get; init; } = new();

    public IReadOnlyList<LancamentoDoDiario> Lancamentos { get; init; } = [];

    public static ConfiguracaoDoDiario Padrao() => new();

    /// <summary>Corrige valores impossíveis (arquivo editado à mão).</summary>
    public ConfiguracaoDoDiario Normalizada() => this with
    {
        JornadaMin = JornadaMin <= 0 ? 528 : JornadaMin,
        BlocoMin = BlocoMin <= 0 ? 240 : BlocoMin,
        ArredondarMin = ArredondarMin <= 0 ? 5 : ArredondarMin,
        MinimoPorIssueMin = Math.Max(1, MinimoPorIssueMin),
    };

    public LancamentoDoDiario? Lancado(string issue, DateTime dia, string inicio)
    {
        var d = dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Lancamentos.FirstOrDefault(l => l.Data == d && l.Inicio == inicio && string.Equals(l.Issue, issue, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"8h48", "4h", "45min".</summary>
    public static string FormatarMin(int min) => min < 60 ? $"{min}min" : $"{min / 60}h" + (min % 60 == 0 ? "" : (min % 60).ToString("00", CultureInfo.InvariantCulture));
}

/// <summary>Guarda <c>diario.json</c>; cada alteração grava na hora.</summary>
public sealed class RepositorioDoDiario(IArmazenamento<ConfiguracaoDoDiario> armazenamento)
{
    ConfiguracaoDoDiario? _atual;

    public ConfiguracaoDoDiario Configuracao => _atual ??= armazenamento.Carregar().ValorOu(new ConfiguracaoDoDiario()).Normalizada();

    public event Action? Alterado;

    public Resultado<Nada> Salvar(ConfiguracaoDoDiario nova)
    {
        nova = nova.Normalizada();
        var r = armazenamento.Salvar(nova);
        if (r.EhOk) _atual = nova;
        Alterado?.Invoke();
        return r;
    }

    public Resultado<Nada> RegistrarLancamento(LancamentoDoDiario lancamento) =>
        Salvar(Configuracao with { Lancamentos = [.. Configuracao.Lancamentos, lancamento] });
}
