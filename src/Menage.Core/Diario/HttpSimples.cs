using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Menage.Core.Diario;

/// <summary>Onde o token do Jira e as chaves de IA são lidos: variáveis de ambiente (nunca gravados em arquivo).</summary>
public interface IVariaveisDeAmbiente
{
    /// <summary>Valor da variável (processo, usuário ou máquina) ou vazio.</summary>
    string Ler(string nome);
}

public sealed class VariaveisDoWindows : IVariaveisDeAmbiente
{
    public string Ler(string nome)
    {
        if (string.IsNullOrEmpty(nome)) return "";
        // aplicativos abertos antes da variável existir só a veem no registro do usuário
        foreach (var alvo in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            if (Environment.GetEnvironmentVariable(nome, alvo) is { } v && !string.IsNullOrWhiteSpace(v)) return v.Trim();
        return "";
    }
}

/// <summary>Falha de HTTP com o status (0 = sem resposta).</summary>
public sealed record FalhaHttp(int Status, string Mensagem);

/// <summary>Cliente HTTP mínimo: JSON de ida e volta, mensagem de erro legível e timeout.</summary>
public static partial class HttpSimples
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();

    public static async Task<(string? Corpo, FalhaHttp? Falha)> EnviarAsync(
        HttpClient http, HttpMethod metodo, string url, string? corpo, IReadOnlyDictionary<string, string>? cabecalhos, int timeoutSeg, CancellationToken cancelar = default)
    {
        using var req = new HttpRequestMessage(metodo, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.UserAgent.ParseAdd("Menage");
        if (cabecalhos is not null)
            foreach (var (k, v) in cabecalhos)
                if (k == "Authorization" && v.StartsWith("Bearer ", StringComparison.Ordinal)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", v[7..]);
                else req.Headers.TryAddWithoutValidation(k, v);
        if (corpo is not null) req.Content = new StringContent(corpo, new UTF8Encoding(false), "application/json");

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelar);
        limite.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, timeoutSeg)));
        try
        {
            using var resp = await http.SendAsync(req, limite.Token);
            var texto = await resp.Content.ReadAsStringAsync(limite.Token);
            return resp.IsSuccessStatusCode ? (texto, null) : (null, new FalhaHttp((int)resp.StatusCode, MensagemDeErro((int)resp.StatusCode, texto)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancelar.IsCancellationRequested) throw;
            var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
            return (null, new FalhaHttp(0, $"sem resposta de {host} ({ex.Message})"));
        }
    }

    public static Resultado<string> Resultado(string? corpo, FalhaHttp? falha, string contexto) =>
        falha is null ? Core.Resultado.Ok(corpo ?? "") : Core.Resultado.Falha<string>(contexto, falha.Mensagem);

    /// <summary>Extrai a mensagem útil do corpo do erro (Jira: errorMessages/errors; OpenAI/Groq: error.message).</summary>
    static string MensagemDeErro(int status, string corpo)
    {
        try
        {
            if (JsonNode.Parse(corpo) is JsonObject d)
            {
                var partes = new List<string>();
                if (d["errorMessages"] is JsonArray ms) partes.AddRange(ms.Select(x => x?.ToString() ?? ""));
                if (d["errors"] is JsonObject es) partes.AddRange(es.Select(kv => $"{kv.Key}: {kv.Value}"));
                if (d["error"] is { } e) partes.Add(e is JsonObject eo && eo["message"] is { } m ? m.ToString() : e.ToString());
                if (partes.Count == 0 && d["message"] is { } msg) partes.Add(msg.ToString());
                if (partes.Count > 0) return $"HTTP {status}: " + string.Join(" | ", partes);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // corpo que não é JSON: cai no texto curto abaixo
        }

        var curto = Espacos().Replace(corpo, " ").Trim();
        return $"HTTP {status}" + (curto.Length > 0 ? ": " + (curto.Length > 200 ? curto[..200] + "..." : curto) : "");
    }

    public static string Texto(JsonNode? no, string chave) => no?[chave]?.ToString() ?? "";

    public static HttpStatusCode? Codigo(FalhaHttp falha) => falha.Status == 0 ? null : (HttpStatusCode)falha.Status;
}
