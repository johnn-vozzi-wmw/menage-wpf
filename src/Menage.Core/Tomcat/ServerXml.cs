using System.Globalization;
using System.Text.RegularExpressions;

namespace Menage.Core.Tomcat;

/// <summary>Portas do server.xml com as posições no texto original (0 = não achou).</summary>
public sealed record PortasDoTomcat
{
    public int Http { get; init; }

    public int Parada { get; init; }

    public int Ajp { get; init; }

    public int HttpPos { get; init; } = -1;

    public int HttpTam { get; init; }

    public int ParadaPos { get; init; } = -1;

    public int ParadaTam { get; init; }

    public int AjpPos { get; init; } = -1;

    public int AjpTam { get; init; }

    public int ConectoresHttpAtivos { get; init; }

    public string HttpTexto { get; init; } = "";

    public string ComandoDeParada { get; init; } = "SHUTDOWN";
}

public static partial class ServerXml
{
    /// <summary>Comentários viram espaços (mesmo tamanho): o que sobra é o XML ativo, com as posições do original.</summary>
    public static string Mascarar(string xml) => Comentario().Replace(xml, m => new string(' ', m.Length));

    public static PortasDoTomcat Ler(string xml)
    {
        var ativo = Mascarar(xml);
        var portas = new PortasDoTomcat();
        var servidor = TagServer().Match(ativo);
        if (servidor.Success)
        {
            var tag = xml.Substring(servidor.Index, servidor.Length);
            var porta = AtributoPorta().Match(tag);
            if (porta.Success)
                portas = portas with { ParadaPos = servidor.Index + porta.Groups[1].Index, ParadaTam = porta.Groups[1].Length, Parada = Numero(porta.Groups[1].Value) };
            var comando = ComandoShutdown().Match(tag);
            if (comando.Success) portas = portas with { ComandoDeParada = comando.Groups[1].Value };
        }

        foreach (Match m in TagConector().Matches(ativo))
        {
            var tag = xml.Substring(m.Index, m.Length);
            var porta = AtributoPorta().Match(tag);
            if (!porta.Success) continue;
            var protocolo = Protocolo().Match(tag);
            var ajp = (protocolo.Success ? protocolo.Groups[1].Value : "HTTP/1.1").Contains("AJP", StringComparison.OrdinalIgnoreCase);
            var ssl = Ssl().IsMatch(tag) || Https().IsMatch(tag);
            int pos = m.Index + porta.Groups[1].Index, tam = porta.Groups[1].Length, valor = Numero(porta.Groups[1].Value);
            if (ajp)
            {
                if (portas.AjpPos < 0) portas = portas with { AjpPos = pos, AjpTam = tam, Ajp = valor };
            }
            else if (!ssl)
            {
                portas = portas with { ConectoresHttpAtivos = portas.ConectoresHttpAtivos + 1 };
                if (portas.HttpPos < 0) portas = portas with { HttpPos = pos, HttpTam = tam, Http = valor, HttpTexto = porta.Groups[1].Value };
            }
        }

        return portas;
    }

    /// <summary>Troca só os números (o resto, comentários e formatação, fica como está). 0 = não mexe.</summary>
    public static string Aplicar(string xml, PortasDoTomcat p, int http, int parada, int ajp)
    {
        var trocas = new List<(int Pos, int Tam, int Valor)>();
        if (http > 0 && p.HttpPos >= 0) trocas.Add((p.HttpPos, p.HttpTam, http));
        if (parada > 0 && p.ParadaPos >= 0) trocas.Add((p.ParadaPos, p.ParadaTam, parada));
        if (ajp > 0 && p.AjpPos >= 0) trocas.Add((p.AjpPos, p.AjpTam, ajp));
        foreach (var t in trocas.OrderByDescending(x => x.Pos))
            xml = xml[..t.Pos] + t.Valor.ToString(CultureInfo.InvariantCulture) + xml[(t.Pos + t.Tam)..];
        return xml;
    }

    static int Numero(string texto) => int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comentario();

    [GeneratedRegex(@"<Connector\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex TagConector();

    [GeneratedRegex(@"<Server\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex TagServer();

    [GeneratedRegex(@"(?<![A-Za-z])port\s*=\s*""([^""]*)""")]
    private static partial Regex AtributoPorta();

    [GeneratedRegex(@"shutdown\s*=\s*""([^""]*)""")]
    private static partial Regex ComandoShutdown();

    [GeneratedRegex(@"protocol\s*=\s*""([^""]*)""")]
    private static partial Regex Protocolo();

    [GeneratedRegex(@"SSLEnabled\s*=\s*""true""", RegexOptions.IgnoreCase)]
    private static partial Regex Ssl();

    [GeneratedRegex(@"scheme\s*=\s*""https""", RegexOptions.IgnoreCase)]
    private static partial Regex Https();
}
