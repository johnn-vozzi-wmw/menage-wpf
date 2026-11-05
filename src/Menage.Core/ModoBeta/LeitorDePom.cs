using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.ModoBeta;

/// <summary>artifactId e version do projeto e do parent, com a posição do texto da versão para trocá-la sem reescrever o XML.</summary>
public sealed record PomInfo
{
    public string Artefato { get; init; } = "";

    public string? Versao { get; init; }

    public int InicioDaVersao { get; init; }

    public string PaiArtefato { get; init; } = "";

    public string? PaiVersao { get; init; }

    public int InicioDaVersaoDoPai { get; init; }
}

/// <summary>Leitura do pom.xml sem parser de XML: os arquivos têm acentos em codificações variadas e precisam voltar byte a byte.</summary>
public static class LeitorDePom
{
    /// <summary>Latin-1 mapeia cada byte num caractere: acentos em UTF-8 ou Cp1252 sobrevivem à regravação.</summary>
    public static string Ler(string arquivo) => Encoding.Latin1.GetString(File.ReadAllBytes(arquivo));

    public static void Gravar(string arquivo, string xml) => File.WriteAllBytes(arquivo, Encoding.Latin1.GetBytes(xml));

    public static string? LerFrameworkVersion(string repo)
    {
        var pom = Path.Combine(repo, "pom.xml");
        if (!File.Exists(pom)) return null;

        var m = Regex.Match(Ler(pom), @"<framework\.version>([^<]*)");
        return m.Success && m.Groups[1].Value.Trim() is { Length: > 0 } versao ? versao : null;
    }

    /// <summary>Dependências framework-*: artifactId e versão, com <c>${propriedade}</c> resolvida; nulo quando o pom não diz a versão.</summary>
    public static Dictionary<string, string?> LerDependenciasFramework(string repo)
    {
        var dependencias = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var pom = Path.Combine(repo, "pom.xml");
        if (!File.Exists(pom)) return dependencias;

        var xml = Regex.Replace(Ler(pom), "<!--.*?-->", "", RegexOptions.Singleline);
        var propriedades = LerPropriedades(xml);
        foreach (Match m in Regex.Matches(xml, "<dependency>(.*?)</dependency>", RegexOptions.Singleline))
        {
            var bloco = Regex.Replace(m.Groups[1].Value, "<exclusions>.*?</exclusions>", "", RegexOptions.Singleline);
            var artefato = Regex.Match(bloco, @"<artifactId>\s*([^<]*?)\s*</artifactId>").Groups[1].Value;
            if (!artefato.StartsWith("framework-", StringComparison.OrdinalIgnoreCase)) continue;

            var versao = VersaoDeclarada(bloco, propriedades);
            if (!dependencias.TryGetValue(artefato, out var ja) || ja is null) dependencias[artefato] = versao;
        }

        return dependencias;
    }

    static Dictionary<string, string> LerPropriedades(string xml)
    {
        var propriedades = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match bloco in Regex.Matches(xml, "<properties>(.*?)</properties>", RegexOptions.Singleline))
            foreach (Match p in Regex.Matches(bloco.Groups[1].Value, @"<([\w\.\-]+)>\s*([^<]*?)\s*</\1>"))
                propriedades[p.Groups[1].Value] = p.Groups[2].Value;
        return propriedades;
    }

    static string? VersaoDeclarada(string bloco, Dictionary<string, string> propriedades)
    {
        var m = Regex.Match(bloco, @"<version>\s*([^<]*?)\s*</version>");
        if (!m.Success) return null;

        var versao = Regex.Replace(m.Groups[1].Value, @"\$\{([^}]+)\}", x => propriedades.GetValueOrDefault(x.Groups[1].Value, x.Value));
        return versao.Length == 0 || versao.Contains("${") ? null : versao;
    }

    /// <summary>Só os filhos diretos de project e de parent contam: versões de dependências e comentários ficam de fora.</summary>
    public static PomInfo Interpretar(string xml)
    {
        var info = new PomInfo();
        var pilha = new List<(string Nome, int Inicio, bool TemFilho)>();
        foreach (Match m in Regex.Matches(xml, @"<!--.*?-->|<(/?)([A-Za-z_][\w\.\-:]*)[^>]*?(/?)>", RegexOptions.Singleline))
        {
            if (m.Value.StartsWith("<!--", StringComparison.Ordinal)) continue;
            if (m.Groups[1].Value != "/")
            {
                if (pilha.Count > 0) pilha[^1] = pilha[^1] with { TemFilho = true };
                if (m.Groups[3].Value != "/") pilha.Add((m.Groups[2].Value, m.Index + m.Length, false));
            }
            else if (pilha.Count > 0)
            {
                if (!pilha[^1].TemFilho) info = Registrar(info, string.Join('/', pilha.Select(p => p.Nome)), xml, pilha[^1].Inicio, m.Index);
                pilha.RemoveAt(pilha.Count - 1);
            }
        }

        return info;
    }

    static PomInfo Registrar(PomInfo info, string caminho, string xml, int inicio, int fim)
    {
        var texto = xml[inicio..fim];
        var valor = texto.Trim();
        var posicao = inicio + texto.Length - texto.TrimStart().Length;
        return caminho switch
        {
            "project/version" => info with { Versao = valor, InicioDaVersao = posicao },
            "project/artifactId" => info with { Artefato = valor },
            "project/parent/version" => info with { PaiVersao = valor, InicioDaVersaoDoPai = posicao },
            "project/parent/artifactId" => info with { PaiArtefato = valor },
            _ => info,
        };
    }
}
