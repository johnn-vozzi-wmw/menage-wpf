using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.Diario;

/// <summary>
/// O que mudou de fato no código, sem o que é sensível ou inútil: tira segredos, detecta métodos/classes/tabelas tocados
/// e confere se um resumo (ex.: da IA) só cita nomes que existem nas alterações.
/// </summary>
public static partial class AnaliseDeDiff
{
    // Arquivos cujo CONTEÚDO nunca é lido nem enviado; o nome continua na lista de arquivos alterados.
    static readonly string[] _nomesSensiveis = ["context.xml", ".env", ".pem", ".key", ".p12", ".pfx", ".jks", "id_rsa", "secret", "credential", "password", "senha", "wmw_database_config"];

    static readonly string[] _inuteis =
        [".lock", "package-lock.json", ".min.js", ".min.css", ".class", ".jar", ".war", ".zip", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf", ".dll", ".exe", ".woff", ".ttf", ".map", ".svg"];

    static readonly string[] _reservadas = ["if", "for", "while", "switch", "catch", "return", "new", "class", "else", "try", "using", "get", "set", "this", "super", "when", "foreach", "lock"];

    [GeneratedRegex(@"(?i)\b(pass(?:word|wd)?|senha|pwd|secret|token|api[_-]?key|authorization)\b([""']?\s*[=:]\s*)([""']?)([^\s,;""']+)")]
    private static partial Regex ReSegredo();

    [GeneratedRegex(@"(?i)\b(bearer|basic)\s+([A-Za-z0-9._~+/=-]{8,})")]
    private static partial Regex ReBearer();

    [GeneratedRegex(@"[A-Za-z0-9+/=_\-]{32,}")]
    private static partial Regex ReLongo();

    [GeneratedRegex(@"^diff --git a/(.+?) b/")]
    private static partial Regex ReCabecalho();

    static readonly Regex[] _simbolos =
    [
        new(@"^\+\s*(?:(?:public|private|protected|internal|static|final|abstract|override|virtual|async|synchronized|default)\s+)+[\w<>\[\],.?\s]*?\b(\w+)\s*\(", RegexOptions.CultureInvariant),
        new(@"\b(?:class|interface|enum|struct|record)\s+(\w+)", RegexOptions.CultureInvariant),
        new(@"\bfunction\s+(\w+)", RegexOptions.CultureInvariant),
        new(@"(?i)\b(?:create|alter|drop)\s+(?:or\s+replace\s+)?(?:table|view|index|procedure|function|trigger|sequence)\s+(?:if\s+(?:not\s+)?exists\s+)?(\w+)", RegexOptions.CultureInvariant),
    ];

    static readonly Regex[] _citados =
    [
        new(@"\b[\w.-]+\.(?:java|cs|js|ts|xml|sql|jsp|html|css|properties|json|yml|yaml|py|md|bat|ps1|cfg|gradle)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b[a-z]+(?:[A-Z][a-z0-9]*)+\b", RegexOptions.CultureInvariant),   // camelCase
        new(@"\b(?:[A-Z][a-z0-9]+){2,}\b", RegexOptions.CultureInvariant),      // PascalCase
        new(@"\b[A-Za-z0-9]+(?:_[A-Za-z0-9]+)+\b", RegexOptions.CultureInvariant), // snake_case
    ];

    public static bool Sensivel(string arquivo) =>
        _nomesSensiveis.Any(n => arquivo.Contains(n, StringComparison.OrdinalIgnoreCase));

    public static bool Inutil(string arquivo) =>
        _inuteis.Any(n => arquivo.EndsWith(n, StringComparison.OrdinalIgnoreCase));

    // Texto longo só é segredo se parecer chave (tem dígitos ou muitas maiúsculas misturadas). Nomes camelCase longos e
    // constantes_COM_SUBLINHADO passam: são exatamente o que o resumo precisa citar.
    static string MascararLongo(Match m)
    {
        var s = m.Value;
        int digitos = s.Count(char.IsDigit), letras = s.Count(char.IsLetter), maiusculas = s.Count(char.IsUpper);
        var suspeito = digitos > 0 || (!s.Contains('_') && letras > 0 && maiusculas / (double)letras >= 0.3);
        return suspeito ? "***" : s;
    }

    /// <summary>Troca por *** senhas, tokens, chaves e "Authorization: Bearer ...".</summary>
    public static string Mascarar(string linha)
    {
        var s = ReBearer().Replace(linha, m => m.Groups[1].Value + " ***"); // antes: o Bearer não pode deixar o valor
        s = ReSegredo().Replace(s, m => m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + "***");
        return ReLongo().Replace(s, MascararLongo);
    }

    static string Corta(string s, int max) => s.Length > max ? s[..max] : s;

    /// <summary>Preenche DiffTexto (compacto, só linhas +/-, sem segredos) e Simbolos do commit a partir do <c>git show</c>.</summary>
    public static void Processar(string patch, CommitDoDia c, int maxCharsCommit = 3600)
    {
        var sb = new StringBuilder();
        var simbolos = new List<string>();
        var arquivo = "";
        var ignorar = false;
        int mais = 0, menos = 0, linhasArquivo = 0;
        var buffer = new List<string>();

        void Fechar()
        {
            if (arquivo.Length == 0) return;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"### {arquivo} (+{mais}/-{menos})") + (ignorar ? "  [conteúdo omitido]" : ""));
            if (!ignorar) foreach (var l in buffer) sb.AppendLine(l);
        }

        foreach (var bruta in patch.Split('\n'))
        {
            var linha = bruta.TrimEnd('\r');
            if (linha.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                Fechar();
                arquivo = ReCabecalho().Match(linha) is { Success: true } m0 ? m0.Groups[1].Value : "";
                mais = menos = linhasArquivo = 0;
                buffer = [];
                ignorar = Sensivel(arquivo) || Inutil(arquivo);
                continue;
            }

            if (arquivo.Length == 0 || EhCabecalhoSemInteresse(linha)) continue;
            if (linha.StartsWith("Binary files", StringComparison.Ordinal)) { ignorar = true; continue; }
            if (linha.StartsWith("@@", StringComparison.Ordinal))
            {
                var i = linha.IndexOf("@@", 2, StringComparison.Ordinal);
                var ctx = i > 0 ? linha[(i + 2)..].Trim() : ""; // o git mostra aqui o método/classe onde está o trecho
                if (!ignorar && ctx.Length is > 0 and < 160) AcharSimbolos("+" + ctx, simbolos);
                if (!ignorar && buffer.Count < 40) buffer.Add("@@ " + Corta(ctx, 120));
                continue;
            }

            if (linha.StartsWith(' ') && linha.Trim().Length > 0) // contexto (1 antes e 1 depois da mudança)
            {
                if (!ignorar && buffer.Count < 40) buffer.Add("  " + Corta(Mascarar(linha.Trim()), 160));
                continue;
            }

            var add = linha.StartsWith('+');
            if (!add && !linha.StartsWith('-')) continue;
            if (add) mais++; else menos++;
            if (ignorar) continue;
            var conteudo = linha[1..];
            if (conteudo.Trim().Length == 0) continue;
            if (add) AcharSimbolos(linha, simbolos);
            if (linhasArquivo++ >= 40) continue;
            buffer.Add((add ? "+ " : "- ") + Corta(Mascarar(conteudo.Trim()), 160));
        }

        Fechar();
        var texto = sb.ToString();
        c.DiffTexto = texto.Length > maxCharsCommit ? texto[..maxCharsCommit] + "\n[...]" : texto;
        c.Simbolos = [.. simbolos.Take(12)];
    }

    static bool EhCabecalhoSemInteresse(string l) =>
        l.StartsWith("+++", StringComparison.Ordinal) || l.StartsWith("---", StringComparison.Ordinal) || l.StartsWith("index ", StringComparison.Ordinal)
        || l.StartsWith("new file", StringComparison.Ordinal) || l.StartsWith("deleted file", StringComparison.Ordinal)
        || l.StartsWith("similarity", StringComparison.Ordinal) || l.StartsWith("rename ", StringComparison.Ordinal);

    static void AcharSimbolos(string linha, List<string> destino)
    {
        foreach (var re in _simbolos)
            if (re.Match(linha) is { Success: true } m) Adicionar(destino, m.Groups[1].Value);
    }

    static void Adicionar(List<string> l, string s)
    {
        if (s.Length < 3 || _reservadas.Contains(s, StringComparer.OrdinalIgnoreCase) || l.Contains(s, StringComparer.OrdinalIgnoreCase)) return;
        l.Add(s);
    }

    // ---- coerência: o resumo só pode citar nomes (arquivos, métodos, classes) que aparecem nas alterações enviadas

    public static List<string> NaoSustentados(string resumo, string baseTexto)
    {
        var ruins = new List<string>();
        foreach (var re in _citados)
            foreach (Match m in re.Matches(resumo))
            {
                var termo = m.Value;
                if (baseTexto.Contains(termo, StringComparison.OrdinalIgnoreCase) || ruins.Contains(termo, StringComparer.OrdinalIgnoreCase)) continue;
                if (ruins.Exists(r => r.StartsWith(termo + ".", StringComparison.OrdinalIgnoreCase))) continue; // "FantasmaService" já está em "FantasmaService.java"
                ruins.Add(termo);
            }

        return ruins;
    }

    /// <summary>Remove as FRASES do resumo (texto corrido) que citam termos não sustentados.</summary>
    public static (string Texto, int Removidas) RemoverFrases(string resumo, IReadOnlyList<string> termos)
    {
        var mantidas = new List<string>();
        var removidas = 0;
        foreach (var f in Regex.Split(resumo.Trim(), @"(?<=[.!?])\s+"))
        {
            if (f.Trim().Length == 0) continue;
            if (termos.Any(t => f.Contains(t, StringComparison.OrdinalIgnoreCase))) { removidas++; continue; }
            mantidas.Add(f.Trim());
        }

        return (string.Join(' ', mantidas).Trim(), removidas);
    }
}

/// <summary>Lê o diff (<c>git show</c>) de cada commit ainda não lido. Só leitura; nada sai da máquina aqui.</summary>
public sealed class LeitorDeDiffs(IGit git)
{
    public async Task LerAsync(IEnumerable<CommitDoDia> commits, IReadOnlyDictionary<string, string> pastaPorProjeto, int maximo = 80, CancellationToken cancelar = default)
    {
        var n = 0;
        foreach (var c in commits)
        {
            if (c.DiffLido) continue;
            if (++n > maximo) break;
            if (pastaPorProjeto.TryGetValue(c.Projeto, out var pasta))
            {
                var saida = await git.ExecutarBrutoAsync(
                    Git.Em(pasta).Comando("show").Opcao("--format=").Opcao("--unified=1").Opcao("-w").Opcao("--ignore-blank-lines")
                        .Opcao("--no-color").Opcao("--no-ext-diff").Argumento(c.Hash), cancelar);
                if (saida.Sucesso) AnaliseDeDiff.Processar(saida.Texto, c);
            }

            c.DiffLido = true;
        }
    }
}
