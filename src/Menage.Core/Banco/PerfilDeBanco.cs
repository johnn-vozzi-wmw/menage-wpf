namespace Menage.Core.Banco;

public static class TipoDeBanco
{
    public const string Oracle = "oracle", Postgres = "postgres", SqlServer = "sqlserver";

    public static IReadOnlyList<string> Todos { get; } = [Oracle, Postgres, SqlServer];

    public static string Nome(string tipo) => tipo switch { Oracle => "Oracle", Postgres => "PostgreSQL", SqlServer => "SQL Server", _ => tipo };

    public static string PortaPadrao(string tipo) => tipo switch { Oracle => "1521", Postgres => "5432", _ => "1433" };

    /// <summary>Nome do campo "banco" em cada tecnologia (rótulo na tela).</summary>
    public static string RotuloDoBanco(string tipo) => tipo == Oracle ? "Service name" : "Database";
}

/// <summary>Usuário, senha (já criptografada, colada como está) e banco de uma conexão.</summary>
public sealed record ConexaoDeBanco(string Usuario = "", string Banco = "", string Senha = "");

/// <summary>As conexões do context.xml: <c>web</c> (LAVENDEREWEBDS), <c>app</c> (LAVENDEREPDADS) e <c>int</c> (LAVENDEREINTDS).</summary>
public static class ChavesDeConexao
{
    public const string Web = "web", App = "app", Integracao = "int";

    public static IReadOnlyList<string> Todas { get; } = [Web, App, Integracao];

    public static string NomeDoRecurso(string chave) => chave switch { Web => "jdbc/LAVENDEREWEBDS", App => "jdbc/LAVENDEREPDADS", _ => "jdbc/LAVENDEREINTDS" };
}

/// <summary>
/// Os valores que mudam no context.xml. Oracle usa um service name só (o de <see cref="Web"/>) nas três conexões.
/// <see cref="Id"/> é a ligação estável com outros módulos (sobrevive a renomear).
/// </summary>
public sealed record PerfilDeBanco
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Nome { get; init; } = "";

    public string Tipo { get; init; } = TipoDeBanco.Oracle;

    public string Host { get; init; } = "localhost";

    public string Porta { get; init; } = "1521";

    public ConexaoDeBanco Web { get; init; } = new();

    public ConexaoDeBanco App { get; init; } = new();

    public ConexaoDeBanco Integracao { get; init; } = new();

    /// <summary>"Igual a WEB" (padrão): o INT espelha o WEB.</summary>
    public bool IntIgualWeb { get; init; } = true;

    /// <summary>Valores que valem de fato para a conexão: o INT espelha o WEB quando marcado; no Oracle o service name é único.</summary>
    public ConexaoDeBanco Efetiva(string chave)
    {
        var c = chave switch { ChavesDeConexao.Web => Web, ChavesDeConexao.App => App, _ => IntIgualWeb ? Web : Integracao };
        return Tipo == TipoDeBanco.Oracle ? c with { Banco = Web.Banco } : c;
    }

    public string Resumo => $"{TipoDeBanco.Nome(Tipo)} - {Host}:{Porta}" + (Web.Banco.Length == 0 ? "" : "/" + Web.Banco);

    /// <summary>Mesma configuração no arquivo (ignora nome e id)?</summary>
    public bool Equivale(PerfilDeBanco? outro)
    {
        if (outro is null || Tipo != outro.Tipo || Host != outro.Host || Porta != outro.Porta) return false;
        return ChavesDeConexao.Todas.All(k => Efetiva(k) == outro.Efetiva(k));
    }
}

/// <summary>Um banco encontrado no context.xml, ativo (descomentado) ou comentado.</summary>
public sealed record BancoNoArquivo(PerfilDeBanco Perfil, bool Ativo);
