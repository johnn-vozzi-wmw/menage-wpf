using System.Net;
using System.Text.RegularExpressions;

namespace Menage.Core.Banco;

public static partial class ContextoXml
{
    [GeneratedRegex(@"(\w+)\s*=\s*""([^""]*)""")]
    private static partial Regex ReAtributo();

    [GeneratedRegex(@"^(jdbc:\w+:(?:thin:@|//))([^:/;?]+)(:\d+)?")]
    private static partial Regex ReServidor();

    [GeneratedRegex(@"^jdbc:oracle:thin:@([^:/]+):(\d+)/([^?]+)")]
    private static partial Regex ReOracle();

    [GeneratedRegex(@"^jdbc:postgresql://([^:/?]+)(?::(\d+))?/([^?]+)")]
    private static partial Regex RePostgres();

    [GeneratedRegex(@"^jdbc:sqlserver://([^:;/]+)(?::(\d+))?;(?:[^;]*;)*?(?i:databaseName|database)=([^;]+)")]
    private static partial Regex ReSqlServer();

    static Dictionary<string, string> Atributos(string resource) =>
        ReAtributo().Matches(resource).ToDictionary(a => a.Groups[1].Value, a => WebUtility.HtmlDecode(a.Groups[2].Value), StringComparer.Ordinal);

    /// <summary>"web", "app" ou "int" pelo name do Resource; nulo se não for uma conexão LAVENDERE*DS.</summary>
    static string? ChaveDo(Dictionary<string, string> at) =>
        at.TryGetValue("name", out var nome) ? ChavesDeConexao.Todas.FirstOrDefault(k => nome == ChavesDeConexao.NomeDoRecurso(k)) : null;

    /// <summary>O banco ativo (descomentado) do context.xml; nulo se não há conexão ativa reconhecível.</summary>
    public static PerfilDeBanco? LerAtivo(string texto)
    {
        var achados = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (ini, fim) in TrechosAtivos(texto))
            foreach (Match m in ReResource().Matches(texto[ini..fim]))
            {
                var at = Atributos(m.Value);
                if (ChaveDo(at) is { } k) achados[k] = at;
            }

        return Montar(achados);
    }

    /// <summary>
    /// Todos os bancos do arquivo, ativos e comentados, na ordem do arquivo. Um banco = WEB/PDA/INT em sequência no mesmo servidor;
    /// começa outro quando a ordem volta (PDA e depois WEB), quando muda o servidor ou de ativo para comentado.
    /// </summary>
    public static IReadOnlyList<BancoNoArquivo> LerTodos(string texto)
    {
        var ativos = TrechosAtivos(texto);
        var lista = new List<BancoNoArquivo>();
        Dictionary<string, Dictionary<string, string>>? grupo = null;
        var grupoAtivo = false;
        var ultima = -1;
        var servidor = "";

        void Fechar()
        {
            if (grupo is not null && Montar(grupo) is { } p) lista.Add(new BancoNoArquivo(p, grupoAtivo));
            grupo = null;
        }

        foreach (Match m in ReResource().Matches(texto))
        {
            var at = Atributos(m.Value);
            if (ChaveDo(at) is not { } k) continue;
            var ativo = ativos.Exists(s => m.Index >= s.Ini && m.Index < s.Fim);
            var ordem = ListaDeChaves.IndexOf(k);
            var srv = Servidor(at.GetValueOrDefault("url"));
            if (grupo is not null && (ordem <= ultima || ativo != grupoAtivo || srv != servidor)) Fechar();
            if (grupo is null) { grupo = []; grupoAtivo = ativo; servidor = srv; }
            grupo[k] = at;
            ultima = ordem;
        }

        Fechar();
        return lista;
    }

    static readonly List<string> ListaDeChaves = [.. ChavesDeConexao.Todas];

    static string Servidor(string? url)
    {
        var m = ReServidor().Match(url ?? "");
        return m.Success ? (m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value).ToLowerInvariant() : "";
    }

    /// <summary>Perfil a partir dos atributos de cada conexão; nulo sem a WEB ou com url fora dos formatos conhecidos.</summary>
    static PerfilDeBanco? Montar(Dictionary<string, Dictionary<string, string>> achados)
    {
        if (!achados.ContainsKey(ChavesDeConexao.Web)) return null;

        string? host = null, porta = null, tipo = null;
        var conexoes = new Dictionary<string, ConexaoDeBanco>();
        foreach (var k in ChavesDeConexao.Todas)
        {
            if (!achados.TryGetValue(k, out var at)) continue;
            var url = at.GetValueOrDefault("url") ?? "";
            Match m;
            string t;
            if ((m = ReOracle().Match(url)).Success) t = TipoDeBanco.Oracle;
            else if ((m = RePostgres().Match(url)).Success) t = TipoDeBanco.Postgres;
            else if ((m = ReSqlServer().Match(url)).Success) t = TipoDeBanco.SqlServer;
            else return null;
            if (tipo is null) { tipo = t; host = m.Groups[1].Value; porta = m.Groups[2].Success ? m.Groups[2].Value : TipoDeBanco.PortaPadrao(t); }
            conexoes[k] = new ConexaoDeBanco(at.GetValueOrDefault("username") ?? "", m.Groups[3].Value, at.GetValueOrDefault("password") ?? "");
        }

        var web = conexoes[ChavesDeConexao.Web];
        var app = conexoes.GetValueOrDefault(ChavesDeConexao.App, web);
        var inte = conexoes.GetValueOrDefault(ChavesDeConexao.Integracao, web);
        var igual = inte == web;
        if (tipo == TipoDeBanco.Oracle) { app = app with { Banco = web.Banco }; inte = inte with { Banco = web.Banco }; }
        return new PerfilDeBanco { Tipo = tipo!, Host = host!, Porta = porta!, Web = web, App = app, Integracao = inte, IntIgualWeb = igual };
    }
}
