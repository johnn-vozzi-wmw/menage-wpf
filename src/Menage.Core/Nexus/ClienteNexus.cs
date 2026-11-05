using System.Globalization;
using System.Text.Json;
using Menage.Core.Http;

namespace Menage.Core.Nexus;

/// <summary>API REST do Sonatype Nexus 3 (repositories, search, download). Só fala HTTP pela <see cref="IClienteHttp"/>.</summary>
public sealed class ClienteNexus(IClienteHttp http, ConfiguracaoDoNexus configuracao, string senha)
{
    static readonly string[] _sufixosIgnorados = [".md5", ".sha1", ".sha256", ".sha512", ".asc"];
    readonly Uri _base = new(configuracao.Url.Trim().TrimEnd('/') + "/");
    readonly CredencialHttp _credencial = new(configuracao.Usuario, senha);

    public async Task<Resultado<IReadOnlyList<string>>> RepositoriosAsync(CancellationToken cancelar = default)
    {
        var json = await ObterAsync("service/rest/v1/repositories", cancelar);
        return json.Encadear(texto => Interpretar(texto, doc =>
        {
            var lista = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (Texto(item, "type") == "group" || Texto(item, "name") is not { Length: > 0 } nome) continue;
                lista.Add(nome);
            }

            lista.Sort(StringComparer.OrdinalIgnoreCase);
            return (IReadOnlyList<string>)lista;
        }));
    }

    /// <summary>Uma página da busca. Curingas: <c>*</c> no nome/grupo; o chamador põe <c>*</c> no fim da versão para prefixo.</summary>
    public async Task<Resultado<PaginaNexus>> BuscarAsync(string repositorio, string? nome, string? grupo, string? versao, string? token, CancellationToken cancelar = default)
    {
        var consulta = new List<string>();
        void Add(string chave, string? valor)
        {
            if (!string.IsNullOrWhiteSpace(valor)) consulta.Add($"{chave}={Uri.EscapeDataString(valor.Trim())}");
        }

        Add("repository", repositorio);
        Add("name", nome);
        Add("group", grupo);
        Add("version", versao);
        Add("continuationToken", token);
        var json = await ObterAsync("service/rest/v1/search" + (consulta.Count > 0 ? "?" + string.Join('&', consulta) : ""), cancelar);
        return json.Encadear(texto => Interpretar(texto, LerPagina));
    }

    /// <summary>Baixa um arquivo e devolve o caminho gravado. A credencial só vai para o mesmo servidor configurado.</summary>
    public async Task<Resultado<string>> BaixarAsync(ArquivoNexus arquivo, string pasta, bool manterEstrutura, Action<long>? progresso, CancellationToken cancelar = default)
    {
        var relativo = manterEstrutura ? arquivo.Caminho.Replace('/', Path.DirectorySeparatorChar) : Path.GetFileName(arquivo.Caminho);
        var raiz = Path.GetFullPath(pasta);
        var destino = Path.GetFullPath(Path.Combine(raiz, relativo));
        if (!destino.StartsWith(raiz, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(destino).Length == 0)
            return Resultado.Falha<string>("Caminho inválido no Nexus", arquivo.Caminho, "Esse arquivo não foi baixado.");
        if (!Uri.TryCreate(arquivo.Url, UriKind.Absolute, out var url))
            return Resultado.Falha<string>("O Nexus não informou o endereço de download", arquivo.Caminho);

        var mesmoServidor = url.Host.Equals(_base.Host, StringComparison.OrdinalIgnoreCase) && url.Port == _base.Port;
        var resposta = await http.BaixarAsync(url, destino, mesmoServidor ? _credencial : null, progresso, cancelar);
        if (!resposta.EhOk) return Resultado.Falha<string>(resposta.Erro);
        return resposta.Valor is >= 200 and < 300 ? Resultado.Ok(destino) : Resultado.Falha<string>(TraduzirStatus(resposta.Valor));
    }

    async Task<Resultado<string>> ObterAsync(string caminho, CancellationToken cancelar)
    {
        var resposta = await http.ObterTextoAsync(new Uri(_base, caminho), _credencial, cancelar);
        if (!resposta.EhOk) return Resultado.Falha<string>(resposta.Erro);
        return resposta.Valor.Sucesso ? Resultado.Ok(resposta.Valor.Corpo) : Resultado.Falha<string>(TraduzirStatus(resposta.Valor.Status));
    }

    static Erro TraduzirStatus(int status) => status switch
    {
        401 => new Erro("Usuário ou senha recusados pelo Nexus (401)", null, "Confira usuário e senha em Servidor."),
        403 => new Erro("Sem permissão de leitura neste repositório (403)", null, "Peça acesso ao repositório."),
        404 => new Erro("Endereço não encontrado no Nexus (404)", null, "Confira a URL do servidor."),
        _ => new Erro($"O Nexus respondeu erro {status}", null, "Tente de novo; se repetir, avise quem cuida do Nexus."),
    };

    static Resultado<T> Interpretar<T>(string json, Func<JsonDocument, T> ler)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Resultado.Ok(ler(doc));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Resultado.Falha<T>("Resposta inesperada do Nexus", ex.Message, "Confira se a URL é a raiz do Nexus 3 (sem /#browse).");
        }
    }

    static PaginaNexus LerPagina(JsonDocument doc)
    {
        var componentes = new List<ComponenteNexus>();
        if (doc.RootElement.TryGetProperty("items", out var itens))
            foreach (var item in itens.EnumerateArray()) componentes.Add(LerComponente(item));
        var token = doc.RootElement.TryGetProperty("continuationToken", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        return new PaginaNexus(componentes, token);
    }

    static ComponenteNexus LerComponente(JsonElement item)
    {
        var arquivos = new List<ArquivoNexus>();
        DateTime? atualizado = null;
        if (item.TryGetProperty("assets", out var assets))
            foreach (var asset in assets.EnumerateArray())
            {
                var caminho = Texto(asset, "path");
                if (_sufixosIgnorados.Any(s => caminho.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                long.TryParse(Texto(asset, "fileSize"), CultureInfo.InvariantCulture, out var tamanho);
                arquivos.Add(new ArquivoNexus(caminho, Texto(asset, "downloadUrl"), Path.GetExtension(caminho).TrimStart('.').ToLowerInvariant(), tamanho));
                if (DateTime.TryParse(Texto(asset, "lastModified"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var data))
                {
                    data = data.ToLocalTime();
                    if (atualizado is null || data > atualizado) atualizado = data;
                }
            }

        return new ComponenteNexus(Texto(item, "repository"), Texto(item, "group"), Texto(item, "name"), Texto(item, "version"), Texto(item, "format"), atualizado, arquivos);
    }

    static string Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()
            : "";
}
