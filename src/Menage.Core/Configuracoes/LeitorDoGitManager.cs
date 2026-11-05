using System.Text;
using System.Text.Json;

namespace Menage.Core.Configuracoes;

/// <summary>
/// Leitura SOMENTE LEITURA dos arquivos de configuração do Git Manager antigo (JSON sem envelope, .cfg de uma linha).
/// Abre sempre com compartilhamento de leitura e nunca grava, renomeia nem apaga nada na pasta antiga.
/// </summary>
public static class LeitorDoGitManager
{
    static readonly string[] _arquivosConhecidos =
        ["perfil.json", "preferencias.json", "protecao.json", "config-geral.cfg", "config-ssh.cfg", "merge.json", "config-update.cfg", "diario.json", "config-nexus.json", "config-tema.cfg", "config-beta.cfg"];

    /// <summary>Instalações antigas dentro de uma pasta (ex.: <c>C:\wmw</c>): subpastas e a própria pasta com algum arquivo conhecido.</summary>
    public static IReadOnlyList<string> Localizar(string raiz)
    {
        if (!Directory.Exists(raiz)) return [];
        try
        {
            return new[] { raiz }.Concat(Directory.EnumerateDirectories(raiz))
                .Where(TemArquivoConhecido).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static bool TemArquivoConhecido(string pasta) => _arquivosConhecidos.Any(a => File.Exists(Path.Combine(pasta, a)));

    public static string? Texto(string pasta, string arquivo)
    {
        var caminho = Path.Combine(pasta, arquivo);
        try
        {
            if (!File.Exists(caminho)) return null;
            using var fluxo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var leitor = new StreamReader(fluxo, new UTF8Encoding(false, throwOnInvalidBytes: false), detectEncodingFromByteOrderMarks: true);
            return leitor.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Primeira linha não vazia de um .cfg (como o "set /p" do menu CMD antigo).</summary>
    public static string? PrimeiraLinha(string pasta, string arquivo) =>
        Texto(pasta, arquivo)?.Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

    public static JsonElement? Json(string pasta, string arquivo)
    {
        var texto = Texto(pasta, arquivo);
        if (string.IsNullOrWhiteSpace(texto)) return null;
        try
        {
            using var doc = JsonDocument.Parse(texto, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static JsonElement? Campo(this JsonElement objeto, string nome)
    {
        foreach (var p in objeto.EnumerateObject())
            if (string.Equals(p.Name, nome, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    public static string? Texto(this JsonElement objeto, string nome) =>
        objeto.Campo(nome) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public static bool? Booleano(this JsonElement objeto, string nome) =>
        objeto.Campo(nome) is { ValueKind: JsonValueKind.True or JsonValueKind.False } v ? v.GetBoolean() : null;

    public static int? Inteiro(this JsonElement objeto, string nome) =>
        objeto.Campo(nome) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var n) ? n : null;

    public static IReadOnlyList<string>? Lista(this JsonElement objeto, string nome) =>
        objeto.Campo(nome) is { ValueKind: JsonValueKind.Array } v
            ? v.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!.Trim()).Where(s => s.Length > 0).ToList()
            : null;
}
