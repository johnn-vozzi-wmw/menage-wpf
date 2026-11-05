using System.Text.Json;
using System.Text.RegularExpressions;
using Menage.Core.Armazenamento;
using Menage.Core.Http;
using Menage.Core.Processos;
using Menage.Core.Repos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Menage.Core.PullRequests;

public sealed record PullRequest
{
    public int Numero { get; init; }

    public string Titulo { get; init; } = "";

    public string Autor { get; init; } = "";

    public string Branch { get; init; } = "";

    public string Sha { get; init; } = "";

    public string Url { get; init; } = "";

    public string Atualizado { get; init; } = "";

    public bool Rascunho { get; init; }

    /// <summary>Resultado da validação; <c>ShaValidado</c> é o commit que passou (commit novo no PR exige validar de novo).</summary>
    public bool? Valido { get; init; }

    public string Validacao { get; init; } = "";

    public string ShaValidado { get; init; } = "";

    public bool ProntoParaMesclar => Valido == true && ShaValidado.Length > 0 && ShaValidado == Sha;

    public string Ref => $"refs/gm-pr/{Numero}";
}

public enum TipoDeLinha { Info, Titulo, Sucesso, Aviso, Erro, Discreto }

public sealed record LinhaDoPr(TipoDeLinha Tipo, string Texto);

public sealed record ListaDePrs(IReadOnlyList<PullRequest> Prs, string Fonte);

public sealed record ResultadoDaMescla(bool Enviado, string Mensagem);

/// <summary>Configuração do módulo (<c>pull-requests.json</c>).</summary>
public sealed record ConfiguracaoDosPrs
{
    public const string NomeDoArquivo = "pull-requests";

    /// <summary>Pasta do repositório que recebe os PRs (padrão: as fontes do Menage).</summary>
    public string Fontes { get; init; } = @"C:\wmw\menage-wpf";

    public string BranchPrincipal { get; init; } = "main";

    /// <summary>Comando que prova que o PR compila depois do merge (roda numa cópia temporária; sai com 0 = ok).</summary>
    public string ComandoDeValidacao { get; init; } = "dotnet build Menage.sln -warnaserror";

    /// <summary>A tela só aparece para o dono do repositório (conveniência; quem autoriza o push é o GitHub).</summary>
    public string PrefixoEmailDono { get; init; } = "johnn.bryan@";

    public static ConfiguracaoDosPrs Padrao() => new();
}

public static partial class RepositorioGitHub
{
    /// <summary>dono/repo a partir da URL do origin (https://github.com/dono/repo.git ou git@github.com:dono/repo.git).</summary>
    public static string? DeUrl(string? url)
    {
        var m = Origem().Match(url ?? "");
        return m.Success ? $"{m.Groups[1].Value}/{m.Groups[2].Value}" : null;
    }

    /// <summary>Resposta de GET /repos/{dono}/{repo}/pulls.</summary>
    public static IReadOnlyList<PullRequest> LerPrs(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var lista = new List<PullRequest>();
        foreach (var d in doc.RootElement.EnumerateArray())
        {
            var head = d.TryGetProperty("head", out var h) ? h : default;
            var user = d.TryGetProperty("user", out var u) ? u : default;
            lista.Add(new PullRequest
            {
                Numero = d.GetProperty("number").GetInt32(),
                Titulo = Texto(d, "title"),
                Autor = Texto(user, "login"),
                Branch = Texto(head, "ref"),
                Sha = Texto(head, "sha"),
                Url = Texto(d, "html_url"),
                Atualizado = Data(Texto(d, "updated_at")),
                Rascunho = d.TryGetProperty("draft", out var dr) && dr.ValueKind == JsonValueKind.True,
            });
        }

        return [.. lista.OrderBy(p => p.Numero)];
    }

    static string Texto(JsonElement e, string nome) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static string Data(string iso) =>
        DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
            ? t.ToLocalTime().ToString("dd/MM HH:mm", System.Globalization.CultureInfo.InvariantCulture) : iso;

    [GeneratedRegex(@"github\.com[:/]+([^/]+)/([^/\s]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex Origem();
}

/// <summary>Token do GitHub que o Git Credential Manager já guardou (nada interativo; sem credencial = nulo). Nunca é gravado nem logado.</summary>
public interface IProvedorDeTokenGitHub
{
    Task<string?> ObterAsync(CancellationToken cancelar = default);
}

public sealed class TokenDoCredentialManager(IProcessoRunner runner) : IProvedorDeTokenGitHub
{
    public async Task<string?> ObterAsync(CancellationToken cancelar = default)
    {
        var comando = new ComandoProcesso("git", ["credential", "fill"], Ambiente: new Dictionary<string, string?>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "never",
            ["GIT_ASKPASS"] = "",
        }, Timeout: TimeSpan.FromSeconds(20), Entrada: System.Text.Encoding.UTF8.GetBytes("protocol=https\nhost=github.com\n\n"));
        var saida = await runner.ExecutarAsync(comando, cancelar);
        if (!saida.Sucesso) return null;
        foreach (var linha in saida.Texto.Split('\n'))
            if (linha.StartsWith("password=", StringComparison.Ordinal) && linha[9..].Trim() is { Length: > 0 } token) return token;
        return null;
    }
}

public static class PullRequestsExtensions
{
    public static IServiceCollection AddPullRequests(this IServiceCollection servicos)
    {
        servicos.TryAddSingleton<IClienteHttp, ClienteHttp>();
        servicos.TryAddSingleton<IProvedorDeTokenGitHub, TokenDoCredentialManager>();
        servicos.AddSingleton<IArmazenamento<ConfiguracaoDosPrs>>(sp =>
            new Armazenamento<ConfiguracaoDosPrs>(sp.GetRequiredService<PastaDeDados>(), ConfiguracaoDosPrs.NomeDoArquivo, padrao: ConfiguracaoDosPrs.Padrao));
        return servicos.AddSingleton<ServicoDePullRequests>();
    }
}
