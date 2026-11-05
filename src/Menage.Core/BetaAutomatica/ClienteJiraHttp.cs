using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Menage.Core.BetaAutomatica;

/// <summary>Jira Server (REST v2) só para leitura, com o token pessoal numa variável de ambiente (padrão <c>JIRA_TOKEN</c>).</summary>
public sealed class ClienteJiraHttp : IClienteJira
{
    const string Campos = "summary,status,issuetype,assignee,subtasks,comment,parent";

    readonly HttpClient _http;
    readonly string _token;
    string? _usuario;

    public ClienteJiraHttp(string endereco, string variavel, HttpMessageHandler? handler = null, string? token = null)
    {
        Endereco = endereco.TrimEnd('/');
        NomeDaVariavel = string.IsNullOrWhiteSpace(variavel) ? "JIRA_TOKEN" : variavel;
        _token = token ?? Environment.GetEnvironmentVariable(NomeDaVariavel)?.Trim() ?? "";
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(25);
        if (_token.Length > 0) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    public bool TemToken => _token.Length > 0;

    public string NomeDaVariavel { get; }

    public string Endereco { get; }

    public async Task<string> UsuarioAsync(CancellationToken cancelar = default)
    {
        if (_usuario is not null) return _usuario;
        using var doc = await GetAsync("/rest/api/2/myself", cancelar);
        var raiz = doc.RootElement;
        var nome = Texto(raiz, "name");
        return _usuario = nome.Length > 0 ? nome : Texto(raiz, "key");
    }

    public async Task<IReadOnlyList<IssueJira>> BuscarCompletasAsync(string jql, int maximo, CancellationToken cancelar = default)
    {
        var caminho = $"/rest/api/2/search?expand=changelog&fields={Campos}&maxResults={maximo}&jql={Uri.EscapeDataString(jql)}";
        using var doc = await GetAsync(caminho, cancelar);
        var res = new List<IssueJira>();
        if (doc.RootElement.TryGetProperty("issues", out var lista) && lista.ValueKind == JsonValueKind.Array)
            res.AddRange(lista.EnumerateArray().Select(Ler).Where(i => i.Chave.Length > 0));
        return res;
    }

    async Task<JsonDocument> GetAsync(string caminho, CancellationToken cancelar)
    {
        if (!TemToken) throw new InvalidOperationException($"A variável de ambiente {NomeDaVariavel} (token do Jira) não está definida.");
        using var resposta = await _http.GetAsync(Endereco + caminho, cancelar);
        var corpo = await resposta.Content.ReadAsStringAsync(cancelar);
        if (!resposta.IsSuccessStatusCode)
            throw new HttpRequestException($"Jira respondeu {(int)resposta.StatusCode} {resposta.ReasonPhrase}: {Resumir(corpo)}");
        return JsonDocument.Parse(corpo);
    }

    static string Resumir(string corpo) => corpo.Length > 200 ? corpo[..200] + "..." : corpo;

    /// <summary>Leitura de um item do JSON do Jira (público para teste com respostas gravadas).</summary>
    public static IssueJira Ler(JsonElement d)
    {
        var i = new IssueJira { Chave = Texto(d, "key") };
        if (d.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object)
        {
            i.Titulo = Texto(f, "summary");
            i.Status = Texto(Filho(f, "status"), "name");
            var tipo = Filho(f, "issuetype");
            i.Tipo = Texto(tipo, "name");
            i.EhSubtask = tipo.ValueKind == JsonValueKind.Object && tipo.TryGetProperty("subtask", out var st) && st.ValueKind == JsonValueKind.True;
            var resp = Filho(f, "assignee");
            i.Responsavel = Texto(resp, "name").Length > 0 ? Texto(resp, "name") : Texto(resp, "key");
            i.ResponsavelNome = Texto(resp, "displayName");
            i.Pai = Texto(Filho(f, "parent"), "key");
            if (Filho(f, "comment") is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty("comments", out var cs) && cs.ValueKind == JsonValueKind.Array)
                foreach (var cd in cs.EnumerateArray())
                {
                    var autor = Filho(cd, "author");
                    var quem = Texto(autor, "name").Length > 0 ? Texto(autor, "name") : Texto(autor, "key");
                    i.Comentarios.Add(new ComentarioJira(Texto(cd, "id"), quem, Texto(cd, "body"), Data(Texto(cd, "created"))));
                }
        }

        if (Filho(d, "changelog") is { ValueKind: JsonValueKind.Object } cl && cl.TryGetProperty("histories", out var hs) && hs.ValueKind == JsonValueKind.Array)
            foreach (var h in hs.EnumerateArray())
            {
                if (!h.TryGetProperty("items", out var itens) || itens.ValueKind != JsonValueKind.Array) continue;
                var autor = Texto(Filho(h, "author"), "name");
                var quando = Data(Texto(h, "created"));
                foreach (var it in itens.EnumerateArray())
                    i.Historico.Add(new MudancaJira(quando, Texto(it, "field"), Texto(it, "from"), Texto(it, "to"), Texto(it, "fromString"), Texto(it, "toString"), autor));
            }

        return i;
    }

    static JsonElement Filho(JsonElement e, string nome) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v) ? v : default;

    static string Texto(JsonElement e, string nome) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()
            : "";

    static DateTime Data(string s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var q) ? q.LocalDateTime : DateTime.MinValue;
}

public sealed class FabricaDeJiraHttp : IFabricaDeJira
{
    public IClienteJira Criar(ConfigBetaAuto config) => new ClienteJiraHttp(config.JiraUrl, config.VariavelDoToken);
}
