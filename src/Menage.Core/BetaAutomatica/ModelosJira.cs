namespace Menage.Core.BetaAutomatica;

public sealed record ComentarioJira(string Id, string Autor, string Corpo, DateTime Quando);

public sealed record MudancaJira(DateTime Quando, string Campo, string De, string Para, string DeNome, string ParaNome, string Autor);

public sealed class IssueJira
{
    public string Chave { get; set; } = "";

    public string Titulo { get; set; } = "";

    public string Tipo { get; set; } = "";

    public string Status { get; set; } = "";

    public string Responsavel { get; set; } = "";

    public string ResponsavelNome { get; set; } = "";

    public string Pai { get; set; } = "";

    public bool EhSubtask { get; set; }

    public List<ComentarioJira> Comentarios { get; } = [];

    public List<MudancaJira> Historico { get; } = [];

    /// <summary>Última passagem de status para <paramref name="status"/> (a que vale).</summary>
    public MudancaJira? UltimaPassagemPara(string status) =>
        Historico.Where(h => h.Campo == "status" && h.ParaNome.Equals(status, StringComparison.OrdinalIgnoreCase)).OrderByDescending(h => h.Quando).FirstOrDefault();

    /// <summary>Quem era o responsável num instante (pelo histórico de "assignee").</summary>
    public string ResponsavelEm(DateTime quando)
    {
        var mudancas = Historico.Where(h => h.Campo == "assignee").OrderBy(h => h.Quando).ToList();
        if (mudancas.Count == 0) return Responsavel;
        var antes = mudancas.LastOrDefault(h => h.Quando <= quando);
        return antes is not null ? antes.Para : mudancas[0].De;
    }
}

/// <summary>Acesso ao Jira (só leitura). O App usa HTTP; os testes, um falso.</summary>
public interface IClienteJira
{
    bool TemToken { get; }

    string NomeDaVariavel { get; }

    string Endereco { get; }

    Task<string> UsuarioAsync(CancellationToken cancelar = default);

    /// <summary>Atividades da busca já com comentários e histórico de mudanças.</summary>
    Task<IReadOnlyList<IssueJira>> BuscarCompletasAsync(string jql, int maximo, CancellationToken cancelar = default);
}

/// <summary>Quem fabrica o cliente: lê a configuração atual a cada ciclo (URL e variável do token podem mudar).</summary>
public interface IFabricaDeJira
{
    IClienteJira Criar(ConfigBetaAuto config);
}
