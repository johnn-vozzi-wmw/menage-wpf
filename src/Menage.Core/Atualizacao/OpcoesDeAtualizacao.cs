using System.Text.Json;

namespace Menage.Core.Atualizacao;

/// <summary>
/// Origem: pasta local/UNC, URL http(s) ou "github:dono/repo". O token do GitHub nunca vai neste arquivo.
/// </summary>
public sealed record OpcoesDeAtualizacao(string? Origem, string? VersaoIgnorada, string? Canal = null)
{
    public const string VariavelOrigem = "MENAGE_ORIGEM_ATUALIZACAO";
    public const string VariavelToken = "MENAGE_TOKEN_ATUALIZACAO";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string CaminhoPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Menage", "atualizacao.json");

    /// <summary>Arquivo de distribuição que acompanha o exe (vem de distribuicao.json na raiz do repositório).</summary>
    public static string CaminhoDistribuicao => Path.Combine(AppContext.BaseDirectory, "distribuicao.json");

    /// <summary>Prioridade da origem: variável de ambiente, depois o arquivo do usuário, depois o padrão de distribuição do pacote.</summary>
    public static OpcoesDeAtualizacao Carregar(string caminho, string? caminhoDistribuicao = null)
    {
        var lidas = File.Exists(caminho)
            ? JsonSerializer.Deserialize<OpcoesDeAtualizacao>(File.ReadAllText(caminho), Json)
            : null;
        var doAmbiente = Environment.GetEnvironmentVariable(VariavelOrigem);
        var origem = new[] { doAmbiente, lidas?.Origem, LerOrigemDeDistribuicao(caminhoDistribuicao ?? CaminhoDistribuicao) }
            .FirstOrDefault(o => !string.IsNullOrWhiteSpace(o));
        return (lidas ?? new(null, null)) with { Origem = origem };
    }

    static string? LerOrigemDeDistribuicao(string caminho)
    {
        try
        {
            if (!File.Exists(caminho)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(caminho));
            return doc.RootElement.TryGetProperty("origemAtualizacao", out var o) ? o.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public void Salvar(string caminho)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, JsonSerializer.Serialize(this, Json));
    }
}
