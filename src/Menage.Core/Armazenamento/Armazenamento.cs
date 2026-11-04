using System.Text.Json;
using System.Text.Json.Nodes;

namespace Menage.Core.Armazenamento;

/// <summary>Pasta onde ficam os dados do usuário. Testes injetam uma pasta em %TEMP%.</summary>
public sealed record PastaDeDados(string Caminho)
{
    public static PastaDeDados Padrao() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Menage"));
}

public interface IArmazenamento<T>
{
    string Arquivo { get; }

    Resultado<T> Carregar();

    Resultado<Nada> Salvar(T dados);
}

/// <summary>
/// JSON tipado com envelope <c>{"versao":N,"dados":...}</c>. Escrita atômica (arquivo temporário + troca);
/// arquivo de versão mais nova ou corrompido nunca é sobrescrito.
/// </summary>
/// <param name="migrar">Converte dados de uma versão antiga (versão lida, dados) para a atual, passo a passo.</param>
public sealed class Armazenamento<T>(
    PastaDeDados pasta,
    string nome,
    int versaoDoEsquema = 1,
    Func<T>? padrao = null,
    Func<int, JsonNode, JsonNode>? migrar = null) : IArmazenamento<T>
{
    static readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly object _trava = new();

    public string Arquivo { get; } = Path.Combine(pasta.Caminho, nome + ".json");

    public Resultado<T> Carregar()
    {
        lock (_trava)
        {
            if (!File.Exists(Arquivo)) return PadraoOuErro();
            try
            {
                return Ler();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return Resultado.Falha<T>($"Não foi possível ler {nome}.json", ex.Message,
                    $"Corrija ou apague o arquivo {Arquivo}.");
            }
        }
    }

    public Resultado<Nada> Salvar(T dados)
    {
        lock (_trava)
        {
            try
            {
                if (File.Exists(Arquivo) && VersaoGravada() > versaoDoEsquema)
                    return Resultado.Falha<Nada>($"{nome}.json é de uma versão mais nova do Menage", null,
                        "Atualize o Menage antes de gravar.");

                Directory.CreateDirectory(pasta.Caminho);
                var envelope = new JsonObject { ["versao"] = versaoDoEsquema, ["dados"] = JsonSerializer.SerializeToNode(dados, _json) };
                var temporario = Arquivo + ".tmp";
                File.WriteAllText(temporario, envelope.ToJsonString(_json));
                File.Move(temporario, Arquivo, overwrite: true);
                return Resultado.Ok();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return Resultado.Falha<Nada>($"Não foi possível gravar {nome}.json", ex.Message, "Confira permissões e espaço em disco.");
            }
        }
    }

    Resultado<T> Ler()
    {
        var raiz = JsonNode.Parse(File.ReadAllText(Arquivo))?.AsObject() ?? throw new JsonException("Arquivo vazio.");
        var versao = raiz["versao"]?.GetValue<int>() ?? 1;
        if (versao > versaoDoEsquema)
            return Resultado.Falha<T>($"{nome}.json é de uma versão mais nova do Menage", $"Arquivo v{versao}, programa v{versaoDoEsquema}.",
                "Atualize o Menage.");

        var dados = raiz["dados"] ?? throw new JsonException("Campo 'dados' ausente.");
        for (var v = versao; v < versaoDoEsquema; v++)
            dados = migrar?.Invoke(v, dados) ?? dados;

        return Resultado.Ok(dados.Deserialize<T>(_json) ?? throw new JsonException("Dados nulos."));
    }

    int VersaoGravada()
    {
        try { return JsonNode.Parse(File.ReadAllText(Arquivo))?["versao"]?.GetValue<int>() ?? 1; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return 1; }
    }

    Resultado<T> PadraoOuErro() =>
        padrao is null
            ? Resultado.Falha<T>($"{nome}.json ainda não existe", null, "Grave os dados uma vez.")
            : Resultado.Ok(padrao());
}
