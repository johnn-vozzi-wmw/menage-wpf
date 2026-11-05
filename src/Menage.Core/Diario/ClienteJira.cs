using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Menage.Core.Diario;

public sealed record IssueDoJira(string Chave, string Titulo, string Status, string Tipo);

public sealed record WorklogDoJira(string Id, string Autor, string Comentario, DateTime Inicio, int Segundos);

/// <summary>
/// Jira Server/Data Center (API REST v2) para conferir issues e lançar worklogs. A autenticação é um Personal Access Token lido da variável
/// de ambiente (padrão <c>JIRA_TOKEN</c>): nunca é gravado, mostrado nem registrado.
/// </summary>
public sealed class ClienteJira
{
    readonly HttpClient _http;
    readonly string _baseUrl;
    readonly string _token;
    readonly int _timeout;
    string? _usuario;

    public ClienteJira(HttpClient http, ConfiguracaoDoDiario cfg, IVariaveisDeAmbiente ambiente, int timeoutSeg = 30)
    {
        _http = http;
        _baseUrl = cfg.JiraUrl.TrimEnd('/');
        NomeDaVariavel = cfg.VarTokenJira.Length > 0 ? cfg.VarTokenJira : "JIRA_TOKEN";
        _token = ambiente.Ler(NomeDaVariavel);
        _timeout = timeoutSeg;
    }

    public string NomeDaVariavel { get; }

    public bool TemToken => _token.Length > 0;

    public string Base => _baseUrl;

    async Task<Resultado<JsonNode>> ChamarAsync(HttpMethod metodo, string caminho, object? corpo, CancellationToken cancelar)
    {
        if (_token.Length == 0)
            return Resultado.Falha<JsonNode>($"A variável de ambiente {NomeDaVariavel} (token do Jira) não está definida", null, "Defina o token e reabra o Menage.");
        var cab = new Dictionary<string, string> { ["Authorization"] = "Bearer " + _token };
        var (texto, falha) = await HttpSimples.EnviarAsync(_http, metodo, _baseUrl + caminho, corpo is null ? null : JsonSerializer.Serialize(corpo), cab, _timeout, cancelar);
        if (falha is not null) return Resultado.Falha<JsonNode>("Falha ao falar com o Jira", falha.Mensagem);
        try
        {
            return Resultado.Ok(JsonNode.Parse(texto ?? "") ?? new JsonObject());
        }
        catch (JsonException)
        {
            return Resultado.Falha<JsonNode>("Resposta inesperada do Jira", "O servidor não devolveu JSON (confira a URL do Jira em Configurar).");
        }
    }

    /// <summary>Quem sou eu (usuário do token): confirma a conexão e identifica os meus worklogs.</summary>
    public async Task<Resultado<string>> UsuarioAsync(CancellationToken cancelar = default)
    {
        if (_usuario is not null) return Resultado.Ok(_usuario);
        var r = await ChamarAsync(HttpMethod.Get, "/rest/api/2/myself", null, cancelar);
        return r.Mapear(d => _usuario = HttpSimples.Texto(d, "name").Length > 0 ? HttpSimples.Texto(d, "name") : HttpSimples.Texto(d, "key"));
    }

    public async Task<Resultado<IssueDoJira>> IssueAsync(string chave, CancellationToken cancelar = default) =>
        (await ChamarAsync(HttpMethod.Get, $"/rest/api/2/issue/{Uri.EscapeDataString(chave)}?fields=summary,status,issuetype", null, cancelar)).Mapear(d =>
            new IssueDoJira(HttpSimples.Texto(d, "key"), HttpSimples.Texto(d["fields"], "summary"),
                HttpSimples.Texto(d["fields"]?["status"], "name"), HttpSimples.Texto(d["fields"]?["issuetype"], "name")));

    public async Task<Resultado<IReadOnlyList<WorklogDoJira>>> WorklogsAsync(string chave, CancellationToken cancelar = default) =>
        (await ChamarAsync(HttpMethod.Get, $"/rest/api/2/issue/{Uri.EscapeDataString(chave)}/worklog", null, cancelar)).Mapear<IReadOnlyList<WorklogDoJira>>(d =>
        {
            var lista = new List<WorklogDoJira>();
            if (d["worklogs"] is not JsonArray ws) return lista;
            foreach (var w in ws.OfType<JsonObject>())
            {
                var autor = HttpSimples.Texto(w["author"], "name");
                if (autor.Length == 0) autor = HttpSimples.Texto(w["author"], "key");
                _ = DateTimeOffset.TryParse(HttpSimples.Texto(w, "started"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var q);
                _ = int.TryParse(HttpSimples.Texto(w, "timeSpentSeconds"), CultureInfo.InvariantCulture, out var seg);
                lista.Add(new WorklogDoJira(HttpSimples.Texto(w, "id"), autor, HttpSimples.Texto(w, "comment"), q == default ? default : q.LocalDateTime, seg));
            }

            return lista;
        });

    /// <summary>Minutos que eu já lancei nessa issue naquele dia (para avisar antes de lançar de novo).</summary>
    public async Task<Resultado<int>> MinutosLancadosAsync(string chave, DateTime dia, CancellationToken cancelar = default)
    {
        var eu = await UsuarioAsync(cancelar);
        if (!eu.EhOk) return Resultado.Falha<int>(eu.Erro);
        return (await WorklogsAsync(chave, cancelar)).Mapear(ws =>
            ws.Where(w => string.Equals(w.Autor, eu.Valor, StringComparison.OrdinalIgnoreCase) && w.Inicio.Date == dia.Date).Sum(w => w.Segundos) / 60);
    }

    /// <summary>Formato do Jira Server: 2026-10-15T09:00:00.000-0300.</summary>
    public static string FormatarInicio(DateTime inicio)
    {
        var desvio = TimeZoneInfo.Local.GetUtcOffset(inicio);
        var sinal = desvio < TimeSpan.Zero ? "-" : "+";
        return inicio.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture) + sinal
            + Math.Abs(desvio.Hours).ToString("00", CultureInfo.InvariantCulture) + Math.Abs(desvio.Minutes).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Cria o worklog e devolve o id.</summary>
    public async Task<Resultado<string>> LancarAsync(string chave, DateTime inicio, int minutos, string comentario, CancellationToken cancelar = default)
    {
        var corpo = new Dictionary<string, object> { ["comment"] = comentario, ["started"] = FormatarInicio(inicio), ["timeSpentSeconds"] = minutos * 60 };
        return (await ChamarAsync(HttpMethod.Post, $"/rest/api/2/issue/{Uri.EscapeDataString(chave)}/worklog", corpo, cancelar)).Mapear(d => HttpSimples.Texto(d, "id"));
    }
}
