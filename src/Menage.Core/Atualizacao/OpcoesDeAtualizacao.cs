using System.Text.Json;

namespace Menage.Core.Atualizacao;

/// <summary>
/// Origem: pasta local/UNC, URL http(s) ou "github:dono/repo". O token do GitHub nunca vai neste arquivo.
/// </summary>
public sealed record OpcoesDeAtualizacao(string? Origem, string? VersaoIgnorada)
{
    public const string VariavelOrigem = "MENAGE_ORIGEM_ATUALIZACAO";
    public const string VariavelToken = "MENAGE_TOKEN_ATUALIZACAO";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string CaminhoPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Menage", "atualizacao.json");

    public static OpcoesDeAtualizacao Carregar(string caminho)
    {
        var lidas = File.Exists(caminho)
            ? JsonSerializer.Deserialize<OpcoesDeAtualizacao>(File.ReadAllText(caminho), Json)
            : null;
        var doAmbiente = Environment.GetEnvironmentVariable(VariavelOrigem);
        return (lidas ?? new(null, null)) with { Origem = string.IsNullOrWhiteSpace(doAmbiente) ? lidas?.Origem : doAmbiente };
    }

    public void Salvar(string caminho)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, JsonSerializer.Serialize(this, Json));
    }
}
