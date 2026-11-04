namespace Menage.TestKit;

/// <summary>
/// <c>mvn.cmd</c> de mentira: grava os argumentos recebidos, imprime o texto combinado e sai com o código pedido.
/// Serve para testar o Gerador de Beta sem Maven e sem nunca fazer deploy de verdade.
/// </summary>
public sealed class MavenFalso : IDisposable
{
    const string Proibidos = "&|<>^%\"";

    readonly string _log;

    MavenFalso(string pasta)
    {
        Pasta = pasta;
        Caminho = Path.Combine(pasta, "mvn.cmd");
        _log = Path.Combine(pasta, "chamadas.log");
    }

    public string Pasta { get; }

    public string Caminho { get; }

    /// <summary>Uma linha por chamada: os argumentos como o Maven falso os recebeu.</summary>
    public IReadOnlyList<string> Chamadas => File.Exists(_log) ? File.ReadAllLines(_log) : [];

    /// <param name="codigoDeSaida">0 = sucesso.</param>
    /// <param name="saida">Texto no stdout (sem os caracteres especiais do cmd).</param>
    /// <param name="erro">Texto no stderr.</param>
    /// <param name="atrasoEmSegundos">Demora simulada antes de sair (para testar timeout e cancelamento).</param>
    public static MavenFalso Criar(int codigoDeSaida = 0, string saida = "BUILD SUCCESS", string? erro = null, int atrasoEmSegundos = 0)
    {
        if (saida.Any(Proibidos.Contains) || (erro?.Any(Proibidos.Contains) ?? false))
            throw new ArgumentException($"O texto não pode ter nenhum destes caracteres: {Proibidos}");

        var maven = new MavenFalso(Path.Combine(Path.GetTempPath(), "mv" + Guid.NewGuid().ToString("N")[..6]));
        Directory.CreateDirectory(maven.Pasta);
        var linhas = new List<string> { "@echo off", $"echo %*>>\"{maven._log}\"", $"echo {saida}" };
        if (erro is not null) linhas.Add($"echo {erro} 1>&2");
        if (atrasoEmSegundos > 0) linhas.Add($"ping -n {atrasoEmSegundos + 1} 127.0.0.1 >nul");
        linhas.Add($"exit /b {codigoDeSaida}");
        File.WriteAllLines(maven.Caminho, linhas);
        return maven;
    }

    public void Dispose()
    {
        if (Directory.Exists(Pasta)) Directory.Delete(Pasta, recursive: true);
    }
}
