using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.Banco;

/// <summary>
/// Reescrita do context.xml do Tomcat. Os marcadores da região gerada são os mesmos do Git Manager antigo de propósito:
/// os dois programas reconhecem (e substituem) o mesmo trecho, sem duplicá-lo.
/// </summary>
public static partial class ContextoXml
{
    public const string MarcaInicio = "<!-- GIT-MANAGER:BANCO INICIO (gerado pelo Git Manager; nao edite este trecho) -->";
    public const string MarcaFim = "<!-- GIT-MANAGER:BANCO FIM -->";

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex ReHost();

    [GeneratedRegex(@"^(?!.*--)[^\s""<>]+$")]
    private static partial Regex ReValor();

    static string Esc(string v) => v.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>Mensagem de erro da validação, ou nulo se o perfil pode ser gravado.</summary>
    public static string? Validar(PerfilDeBanco? p)
    {
        if (p is null) return "Perfil vazio.";
        if (string.IsNullOrWhiteSpace(p.Nome)) return "Informe o nome do perfil.";
        if (!TipoDeBanco.Todos.Contains(p.Tipo)) return "Tipo de banco inválido.";
        if (!ReHost().IsMatch(p.Host)) return "Host/IP inválido (use só letras, números, ponto e hífen).";
        if (!int.TryParse(p.Porta, out var porta) || porta is < 1 or > 65535) return "Porta inválida.";
        var rotulo = TipoDeBanco.RotuloDoBanco(p.Tipo);
        foreach (var k in ChavesDeConexao.Todas)
        {
            var c = p.Efetiva(k);
            var quem = k.ToUpperInvariant();
            if (!ReValor().IsMatch(c.Usuario)) return $"Usuário da conexão {quem} vazio ou com caracteres inválidos.";
            if (!ReValor().IsMatch(c.Banco)) return $"{rotulo} da conexão {quem} vazio ou com caracteres inválidos.";
            if (!ReValor().IsMatch(c.Senha)) return $"Senha da conexão {quem} vazia ou com caracteres inválidos.";
        }

        return null;
    }

    /// <summary>Um <c>&lt;Resource&gt;</c> pronto, no formato que a WMW usa em cada banco.</summary>
    public static string GerarResource(PerfilDeBanco p, string chave)
    {
        var c = p.Efetiva(chave);
        string host = Esc(p.Host), porta = Esc(p.Porta), banco = Esc(c.Banco), user = Esc(c.Usuario), senha = Esc(c.Senha);
        var (url, validacao, driver) = p.Tipo switch
        {
            TipoDeBanco.Oracle => ($"jdbc:oracle:thin:@{host}:{porta}/{banco}?v$session.program={chave}_{user}", "select 1 from dual", "oracle.jdbc.OracleDriver"),
            TipoDeBanco.Postgres => ($"jdbc:postgresql://{host}:{porta}/{banco}?ApplicationName={chave}_{user}", "select 1", "org.postgresql.Driver"),
            _ => ($"jdbc:sqlserver://{host}:{porta};databaseName={banco};sendStringParametersAsUnicode=false;integratedSecurity=false;applicationName={chave}_{banco};encrypt=true;trustServerCertificate=true",
                "select 1", "com.microsoft.sqlserver.jdbc.SQLServerDriver"),
        };
        var extras = p.Tipo == TipoDeBanco.SqlServer ? "" : "\n  connectionProperties=\"defaultRowFetchSize=500;defaultRowPrefetch=500\"";
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<Resource name=\"{ChavesDeConexao.NomeDoRecurso(chave)}\" url=\"{url}\"\n");
        sb.Append(CultureInfo.InvariantCulture, $"  username=\"{user}\" password=\"{senha}\"\n");
        sb.Append(CultureInfo.InvariantCulture, $"  validationQuery=\"{validacao}\" driverClassName=\"{driver}\"\n");
        sb.Append("  maxTotal=\"200\" maxIdle=\"20\" minIdle=\"3\" maxWaitMillis=\"30000\" minEvictableIdleTimeMillis=\"60000\" timeBetweenEvictionRunsMillis=\"15000\"\n");
        sb.Append("  lifo=\"true\" logAbandoned=\"true\" removeAbandonedOnBorrow=\"true\" removeAbandonedTimeout=\"300\"\n");
        sb.Append(CultureInfo.InvariantCulture, $"  auth=\"Container\" factory=\"org.apache.tomcat.pool.PooledDataSourceFactory\" type=\"javax.sql.DataSource\"{extras}\n");
        sb.Append("/>");
        return sb.ToString();
    }

    public static string GerarBloco(PerfilDeBanco p) => string.Join("\n\n", ChavesDeConexao.Todas.Select(k => GerarResource(p, k)));
}
