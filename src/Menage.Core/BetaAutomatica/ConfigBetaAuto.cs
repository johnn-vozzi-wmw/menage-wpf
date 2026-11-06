using System.Text.Json.Serialization;

namespace Menage.Core.BetaAutomatica;

/// <summary>Consulta JQL da busca. Id fixo = consulta padrão do programa (Jql vazio = texto padrão); as outras são personalizadas.</summary>
public sealed class JqlBeta
{
    public string Id { get; set; } = "";

    public string Nome { get; set; } = "";

    public string Jql { get; set; } = "";

    public bool Ativa { get; set; } = true;

    public bool Padrao { get; set; }
}

/// <summary>Tudo o que a Beta automática guarda (<c>beta-automatica.json</c>).</summary>
public sealed class ConfigBetaAuto
{
    public const string NomeDoArquivo = "beta-automatica";

    /// <summary>Só consulta o Jira depois que o usuário ativa o monitor.</summary>
    public bool Ativo { get; set; }

    public int IntervaloMin { get; set; } = 3;

    public string JiraUrl { get; set; } = "https://jira2.wmw.com.br";

    public string VariavelDoToken { get; set; } = "JIRA_TOKEN";

    public string StatusAlvo { get; set; } = "Waiting for Testing";

    public string StatusRevisao { get; set; } = "Waiting for Code Review";

    public string StatusDev { get; set; } = "In Development";

    /// <summary>A atividade que sai de Waiting for Testing/Testing e vai para este status é finalizada sozinha.</summary>
    public string StatusFinal { get; set; } = "Waiting for Product Increment";

    /// <summary>Todos os status de finalização automática (o campo aceita vários, separados por vírgula).</summary>
    [JsonIgnore]
    public IReadOnlyList<string> StatusFinais => ListaSeparada.Ler(StatusFinal);

    public string StatusCodeReview { get; set; } = "In Code Review";

    /// <summary>Perfil Tester: "atribuidas" (assignee = eu) ou "todas".</summary>
    public string TesterEscopo { get; set; } = "atribuidas";

    public string TiposCoding { get; set; } = "Coding,Code,Codigo,Código";

    public string ChavesJira { get; set; } = "VENDAS";

    /// <summary>Onde ficam os clones isolados (nunca os projetos de trabalho).</summary>
    public string PastaTrabalho { get; set; } = @"C:\wmw\beta-auto";

    /// <summary>yyyy-MM-dd HH:mm: o que já estava no status antes disso entra sem alerta.</summary>
    public string Baseline { get; set; } = "";

    public string UltimaConsulta { get; set; } = "";

    public string UltimoErro { get; set; } = "";

    public List<JqlBeta> Jqls { get; set; } = [];

    public List<string> OrdemProjetos { get; set; } = [];

    /// <summary>CHAVE|passagem removidas pelo usuário: a próxima consulta as traz de volta.</summary>
    public List<string> Removidas { get; set; } = [];

    public List<string> Vistos { get; set; } = [];

    public List<ItemBeta> Itens { get; set; } = [];

    public static Func<ConfigBetaAuto> Padrao => () => new ConfigBetaAuto();

    public ItemBeta? Achar(string chave) => Itens.FirstOrDefault(i => i.Chave.Equals(chave, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> Tipos =>
        [.. TiposCoding.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries).Select(LeitorDeComentario.Norm)];
}
