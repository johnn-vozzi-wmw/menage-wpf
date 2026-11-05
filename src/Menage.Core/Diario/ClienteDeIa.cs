using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Menage.Core.Diario;

public sealed record ResumoDaIa(string Texto, string Aviso);

/// <summary>
/// Resumo por IA gratuita (API compatível com a da OpenAI: Groq, xAI/Grok, Ollama local ou OpenRouter). Só é chamada quando o usuário
/// pede, uma vez por worklog. A chave vem de variável de ambiente e nunca é gravada nem mostrada.
/// </summary>
public sealed partial class ClienteDeIa
{
    public static IReadOnlyList<string> Provedores { get; } = ["groq", "xai", "ollama", "openrouter"];

    static readonly ConcurrentDictionary<string, string> _modelosEscolhidos = new();

    static readonly Dictionary<string, string[]> _preferidos = new()
    {
        ["groq"] = ["openai/gpt-oss-120b", "openai/gpt-oss-20b", "qwen/qwen3.6-27b", "llama-3.3-70b-versatile", "llama-3.1-8b-instant"],
        ["xai"] = ["grok-3-mini", "grok-4", "grok-3"],
        ["openrouter"] = ["meta-llama/llama-3.3-70b-instruct:free", "openai/gpt-oss-120b:free", "openai/gpt-oss-20b:free"],
        ["ollama"] = ["qwen2.5:7b", "llama3.1:8b", "llama3.2:3b", "gemma2:9b"],
    };

    static readonly string[] _naoEhChat = ["whisper", "tts", "guard", "embed", "orpheus", "playai", "moderation", "image", "rerank", "transcri"];

    [GeneratedRegex(@"(?is)<think>.*?</think>")]
    private static partial Regex ReRaciocinio();

    [GeneratedRegex(@"^(?:[-*•·]+|\d+[.)])\s*")]
    private static partial Regex ReMarcador();

    [GeneratedRegex(@"\s+")]
    private static partial Regex ReEspacos();

    readonly HttpClient _http;
    readonly ConfiguracaoDaIa _cfg;
    readonly IVariaveisDeAmbiente _ambiente;

    public ClienteDeIa(HttpClient http, ConfiguracaoDaIa cfg, IVariaveisDeAmbiente ambiente)
    {
        (_http, _ambiente) = (http, ambiente);
        _cfg = cfg with { Provedor = Normalizar(cfg.Provedor) };
    }

    /// <summary>"Grok" (xAI) e "Groq" são serviços diferentes: aceita os dois nomes e variações comuns.</summary>
    public static string Normalizar(string? p)
    {
        p = (p ?? "").Trim().ToLowerInvariant();
        return p switch { "grok" or "x.ai" or "xai" or "x-ai" => "xai", "" or "regras" or "nenhuma" or "desligada" => "regras", _ => p };
    }

    public static ConfiguracaoDaIa Padrao(string provedor) => Normalizar(provedor) switch
    {
        "xai" => new ConfiguracaoDaIa { Provedor = "xai", Url = "https://api.x.ai/v1/chat/completions", Modelo = "grok-3-mini", VarChave = "XAI_API_KEY" },
        "groq" => new ConfiguracaoDaIa { Provedor = "groq", Url = "https://api.groq.com/openai/v1/chat/completions", Modelo = "openai/gpt-oss-120b", VarChave = "GROQ_API_KEY" },
        "ollama" => new ConfiguracaoDaIa { Provedor = "ollama", Url = "http://localhost:11434/v1/chat/completions", Modelo = "qwen2.5:7b" },
        "openrouter" => new ConfiguracaoDaIa { Provedor = "openrouter", Url = "https://openrouter.ai/api/v1/chat/completions", Modelo = "meta-llama/llama-3.3-70b-instruct:free", VarChave = "OPENROUTER_API_KEY" },
        "regras" => new ConfiguracaoDaIa { Provedor = "regras" },
        _ => new ConfiguracaoDaIa { Provedor = provedor.Trim().ToLowerInvariant() },
    };

    string Url => _cfg.Url.Length > 0 ? _cfg.Url : Padrao(_cfg.Provedor).Url;

    string Modelo => _cfg.Modelo.Length > 0 ? _cfg.Modelo : Padrao(_cfg.Provedor).Modelo;

    string VarChave => _cfg.VarChave.Length > 0 ? _cfg.VarChave : Padrao(_cfg.Provedor).VarChave;

    string ChaveDoCache => _cfg.Provedor + "|" + Modelo;

    public string ModeloUsado => _modelosEscolhidos.GetValueOrDefault(ChaveDoCache, Modelo);

    public bool Ativa => _cfg.Provedor is not ("" or "regras");

    public string Descricao => Ativa ? $"{_cfg.Provedor} ({ModeloUsado})" : "regras (sem IA)";

    /// <summary>Pode usar? Devolve o motivo quando não (o Ollama não precisa de chave).</summary>
    public bool Disponivel(out string motivo)
    {
        motivo = "";
        if (!Ativa) { motivo = "IA desligada (resumo por regras)."; return false; }
        if (!Provedores.Contains(_cfg.Provedor)) { motivo = $"Provedor de IA desconhecido: \"{_cfg.Provedor}\". Use groq, xai (Grok), ollama ou openrouter."; return false; }
        if (_cfg.Provedor != "ollama" && VarChave.Length > 0 && _ambiente.Ler(VarChave).Length == 0)
        {
            motivo = $"A variável de ambiente {VarChave} (chave do {(_cfg.Provedor == "xai" ? "Grok/xAI" : _cfg.Provedor)}) não foi encontrada. Crie a chave no site do provedor, defina a variável e reabra o programa.";
            return false;
        }

        return true;
    }

    public static string Instrucao(bool comCodigo) =>
        "Você escreve a descrição de um worklog do Jira, em português do Brasil, como UM parágrafo de texto corrido (2 a 4 frases, em torno de 450 caracteres), "
        + "em tom técnico e objetivo e voz impessoal (\"foi adicionado\", \"passou a\", \"foi ajustado\"). "
        + "Descreva o que foi implementado ou alterado e o efeito no comportamento, citando pelo nome as classes e métodos principais quando aparecerem nas informações. "
        + "Dê prioridade ao que as alterações de código mostram; as mensagens de commit são apenas apoio e podem ser vagas. "
        + "Ignore mudanças de formatação, indentação e linhas em branco. "
        + "Use SOMENTE o que está nas informações: não invente comportamento, motivo nem resultado; se faltar detalhe, descreva de forma geral. "
        + "Sem listas, sem títulos, sem markdown, sem hash e sem o id da issue."
        + (comCodigo ? "" : " Você NÃO recebe o código, só mensagens e nomes de arquivo: não descreva detalhes de implementação além do que as mensagens dizem.");

    /// <summary>
    /// O que sai da máquina: id da issue, projetos, mensagens dos commits, nomes de arquivo e métodos/classes detectados.
    /// Só com <paramref name="incluirDiff"/> (desligado por padrão) vai também o diff compacto (linhas +/-, sem segredos).
    /// </summary>
    public static string MontarPedido(GrupoDeIssue g, bool incluirDiff = false, int maxChars = 4000)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Issue: " + g.Issue);
        sb.AppendLine("Projetos: " + string.Join(", ", g.Projetos));
        sb.AppendLine("Mensagens dos commits:");
        foreach (var c in g.Commits.Take(25)) sb.AppendLine("- " + ResumoDoDiario.LimparMensagem(c.Assunto, g.Issue));
        var arqs = g.Commits.SelectMany(c => c.Arquivos).Select(Path.GetFileName).OfType<string>().Distinct().Take(20).ToList();
        if (arqs.Count > 0) { sb.AppendLine("Arquivos alterados (apenas nomes):"); foreach (var a in arqs) sb.AppendLine("- " + a); }
        var simbolos = g.Commits.SelectMany(c => c.Simbolos).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        if (simbolos.Count > 0) sb.AppendLine("Métodos, classes ou tabelas criados/alterados (detectados nas linhas adicionadas): " + string.Join(", ", simbolos));
        if (incluirDiff)
        {
            sb.AppendLine("Alterações no código (linhas \"+\" adicionadas, \"-\" removidas):");
            foreach (var c in g.Commits.Where(c => c.DiffTexto.Length > 0))
            {
                sb.AppendLine("Commit: " + ResumoDoDiario.LimparMensagem(c.Assunto, g.Issue));
                sb.AppendLine(c.DiffTexto);
                if (sb.Length >= maxChars) break;
            }
        }

        var s = sb.ToString();
        var limite = Math.Max(2000, incluirDiff ? maxChars : 4000);
        return s.Length > limite ? s[..limite] + "\n[...]" : s;
    }

    /// <summary>Deixa a resposta como texto corrido: sem raciocínio, markdown, listas nem quebras (um parágrafo, até ~700 caracteres).</summary>
    public static string NormalizarTexto(string? r)
    {
        r = ReRaciocinio().Replace(r ?? "", "").Replace("\r", "").Replace("**", "");
        var partes = new List<string>();
        foreach (var l in r.Split('\n'))
        {
            var t = l.Trim();
            if (t.StartsWith('#')) continue;
            t = ReMarcador().Replace(t, "").Trim();
            if (t.Length > 0) partes.Add(t);
        }

        var texto = ReEspacos().Replace(string.Join(' ', partes), " ").Trim().Trim('"', '“', '”').Trim();
        if (texto.Length <= 700) return texto;
        var corte = texto.LastIndexOfAny(['.', '!', '?'], 699);
        return corte > 300 ? texto[..(corte + 1)] : texto[..697] + "...";
    }

    /// <summary>
    /// Resume e CONFERE: só pode citar arquivos, métodos e classes que aparecem no que foi enviado. Se citar outros, pede de novo;
    /// se insistir, remove as frases sem respaldo (e, se não sobrar nada, falha e quem chamou fica com o resumo por regras).
    /// </summary>
    public async Task<Resultado<ResumoDaIa>> ResumirAsync(GrupoDeIssue g, bool incluirDiff, int maxChars, CancellationToken cancelar = default)
    {
        var pedido = MontarPedido(g, incluirDiff, maxChars);
        var sistema = Instrucao(incluirDiff);
        var primeira = await PerguntarAsync(sistema, pedido, cancelar);
        if (!primeira.EhOk) return Resultado.Falha<ResumoDaIa>(primeira.Erro);
        var resp = NormalizarTexto(primeira.Valor);
        if (resp.Length == 0) return Resultado.Falha<ResumoDaIa>("A IA devolveu uma resposta vazia");
        var ruins = AnaliseDeDiff.NaoSustentados(resp, pedido);
        if (ruins.Count == 0) return Resultado.Ok(new ResumoDaIa(resp, ""));

        var reforco = pedido + "\n\nATENÇÃO: na resposta anterior você citou nomes que NÃO aparecem nas informações acima: " + string.Join(", ", ruins)
            + ". Reescreva usando somente nomes que aparecem acima; se não souber o nome, descreva de forma genérica.";
        var segunda = await PerguntarAsync(sistema, reforco, cancelar);
        var resp2 = segunda.EhOk ? NormalizarTexto(segunda.Valor) : "";
        var ruins2 = resp2.Length > 0 ? AnaliseDeDiff.NaoSustentados(resp2, pedido) : ruins;
        if (resp2.Length > 0 && ruins2.Count == 0) return Resultado.Ok(new ResumoDaIa(resp2, "a primeira resposta citava nomes que não estão nas alterações; foi refeita"));

        var (limpo, removidas) = AnaliseDeDiff.RemoverFrases(resp2.Length > 0 ? resp2 : resp, ruins2);
        return limpo.Length == 0
            ? Resultado.Falha<ResumoDaIa>("A IA citou nomes que não existem nas alterações", string.Join(", ", ruins2))
            : Resultado.Ok(new ResumoDaIa(limpo, $"{removidas} trecho(s) removido(s) por citar nomes que não estão nas alterações"));
    }

    public async Task<Resultado<string>> PerguntarAsync(string sistema, string usuario, CancellationToken cancelar = default)
    {
        if (!Disponivel(out var motivo)) return Resultado.Falha<string>(motivo);
        var modelo = ModeloUsado;
        var r = await EnviarAsync(modelo, sistema, usuario, cancelar);
        if (r.EhOk || !ModeloInexistente(r.Erro)) return r;

        var novo = await EscolherModeloAsync(modelo, cancelar);
        if (!novo.EhOk) return Resultado.Falha<string>(r.Erro.Causa, $"{r.Erro.Explicacao} (e não foi possível listar os modelos: {novo.Erro.Explicacao ?? novo.Erro.Causa})");
        _modelosEscolhidos[ChaveDoCache] = novo.Valor;
        return await EnviarAsync(novo.Valor, sistema, usuario, cancelar);
    }

    Dictionary<string, string> Cabecalhos()
    {
        var cab = new Dictionary<string, string>();
        var chave = VarChave.Length > 0 ? _ambiente.Ler(VarChave) : "";
        if (chave.Length > 0) cab["Authorization"] = "Bearer " + chave;
        if (_cfg.Provedor == "openrouter") cab["X-Title"] = "Menage";
        return cab;
    }

    async Task<Resultado<string>> EnviarAsync(string modelo, string sistema, string usuario, CancellationToken cancelar)
    {
        var raciocinio = modelo.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase);
        var corpo = new JsonObject
        {
            ["model"] = modelo, ["temperature"] = 0.2, ["max_tokens"] = raciocinio ? 900 : 220, ["stream"] = false,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = sistema }, new JsonObject { ["role"] = "user", ["content"] = usuario }),
        };
        if (raciocinio && _cfg.Provedor == "groq") corpo["reasoning_effort"] = "low"; // modelos gpt-oss gastam tokens pensando
        var (texto, falha) = await HttpSimples.EnviarAsync(_http, HttpMethod.Post, Url, corpo.ToJsonString(), Cabecalhos(), _cfg.TimeoutSeg, cancelar);
        if (falha is not null) return Resultado.Falha<string>("Falha ao falar com a IA", falha.Mensagem);
        try
        {
            return JsonNode.Parse(texto ?? "")?["choices"]?[0]?["message"]?["content"]?.ToString() is { } c
                ? Resultado.Ok(c)
                : Resultado.Falha<string>("Resposta inesperada da IA");
        }
        catch (JsonException)
        {
            return Resultado.Falha<string>("Resposta inesperada da IA");
        }
    }

    static bool ModeloInexistente(Erro erro) =>
        (erro.Explicacao ?? "").Contains("model", StringComparison.OrdinalIgnoreCase)
        && ((erro.Explicacao ?? "").Contains("HTTP 404", StringComparison.Ordinal) || (erro.Explicacao ?? "").Contains("HTTP 400", StringComparison.Ordinal));

    /// <summary>Os provedores aposentam modelos com frequência: lista os de chat disponíveis.</summary>
    public async Task<Resultado<IReadOnlyList<string>>> ListarModelosAsync(CancellationToken cancelar = default)
    {
        var url = Url.Contains("/chat/completions", StringComparison.Ordinal) ? Url.Replace("/chat/completions", "/models", StringComparison.Ordinal) : Url.TrimEnd('/') + "/models";
        var (texto, falha) = await HttpSimples.EnviarAsync(_http, HttpMethod.Get, url, null, Cabecalhos(), _cfg.TimeoutSeg, cancelar);
        if (falha is not null) return Resultado.Falha<IReadOnlyList<string>>("Falha ao listar modelos", falha.Mensagem);
        var ids = new List<string>();
        try
        {
            var d = JsonNode.Parse(texto ?? "");
            foreach (var m in ((d?["data"] ?? d?["models"]) as JsonArray ?? []).OfType<JsonObject>())
                if ((m["id"] ?? m["name"])?.ToString() is { } id) ids.Add(id);
        }
        catch (JsonException)
        {
            return Resultado.Falha<IReadOnlyList<string>>("Lista de modelos inesperada");
        }

        return Resultado.Ok<IReadOnlyList<string>>([.. ids.Where(i => !_naoEhChat.Any(n => i.Contains(n, StringComparison.OrdinalIgnoreCase)))]);
    }

    async Task<Resultado<string>> EscolherModeloAsync(string recusado, CancellationToken cancelar)
    {
        var lista = await ListarModelosAsync(cancelar);
        if (!lista.EhOk) return Resultado.Falha<string>(lista.Erro);
        var ids = lista.Valor.Where(i => !string.Equals(i, recusado, StringComparison.OrdinalIgnoreCase)).ToList();
        if (_preferidos.TryGetValue(_cfg.Provedor, out var pref))
            foreach (var p in pref)
                if (ids.Find(i => string.Equals(i, p, StringComparison.OrdinalIgnoreCase)) is { } achou) return Resultado.Ok(achou);
        return ids.Count > 0
            ? Resultado.Ok(ids[0])
            : Resultado.Falha<string>($"O modelo \"{recusado}\" não existe e o provedor não listou nenhum outro modelo de chat para esta chave.");
    }
}
