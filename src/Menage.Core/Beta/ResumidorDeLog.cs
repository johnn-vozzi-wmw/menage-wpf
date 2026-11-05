using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.Beta;

/// <summary>Lê o log do Maven (Windows-1252, arquivo ainda aberto por quem escreve) e extrai o resumo da linha.</summary>
public static partial class ResumidorDeLog
{
    static readonly string[] SinaisDoSurefire =
    [
        "The forked VM terminated", "There was a timeout or other error in the fork", "hs_err_pid", "SIGSEGV",
        "EXCEPTION_ACCESS_VIOLATION", "VM crash", "Crashed tests", "spinInnerClass",
    ];

    public static string Ler(string caminho, long maxBytes = 0)
    {
        if (!File.Exists(caminho)) return "";
        using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var cortado = maxBytes > 0 && fs.Length > maxBytes;
        if (cortado) fs.Seek(-maxBytes, SeekOrigin.End);
        using var leitor = new StreamReader(fs, Encoding.Latin1);
        var texto = leitor.ReadToEnd();
        return cortado && texto.IndexOf('\n') is var i and >= 0 ? texto[(i + 1)..] : texto;
    }

    public static bool ProblemaDoSurefire(string log) =>
        log.Contains("surefire", StringComparison.OrdinalIgnoreCase)
        && SinaisDoSurefire.Any(s => log.Contains(s, StringComparison.OrdinalIgnoreCase));

    public static string Resumir(string log, int codigo)
    {
        var linhas = log.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var (testes, falhas, erros) = ContarTestes(linhas);
        var txt = testes > 0 ? $"{testes} testes ({falhas} falhas, {erros} erros)" : "sem testes";
        return codigo == 0 ? $"OK - {txt}" : $"{txt} | {Motivo(linhas)}";
    }

    static (int Testes, int Falhas, int Erros) ContarTestes(string[] linhas)
    {
        int testes = 0, falhas = 0, erros = 0;
        foreach (var l in linhas)
        {
            var m = LinhaDeTestes().Match(l);
            if (!m.Success) continue;
            testes += int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            falhas += int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            erros += int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return (testes, falhas, erros);
    }

    static string Motivo(string[] linhas)
    {
        var motivo = linhas.FirstOrDefault(l => ErroJava().IsMatch(l))
                     ?? linhas.FirstOrDefault(l => ErroIndentado().IsMatch(l))
                     ?? linhas.FirstOrDefault(l => TesteFalhou().IsMatch(l))
                     ?? FalhaDeGoal(linhas)
                     ?? linhas.FirstOrDefault(l => ErroGenerico().IsMatch(l) && !Ruido().IsMatch(l))
                     ?? "erro (ver log)";
        motivo = SufixoHelp().Replace(PrefixoErro().Replace(motivo, ""), "").Trim();
        return motivo.Length > 220 ? motivo[..220] + "..." : motivo;
    }

    static string? FalhaDeGoal(string[] linhas)
    {
        for (var i = 0; i < linhas.Length; i++)
        {
            var m = GoalFalhou().Match(linhas[i]);
            if (!m.Success) continue;
            var motivo = SufixoHelp().Replace(m.Groups[1].Value, "");
            // mensagem quebrada em várias linhas (ex.: erro do Ant): junta a seguinte
            if (motivo.EndsWith(':') && i + 1 < linhas.Length) motivo += " " + PrefixoErro().Replace(linhas[i + 1], "").Trim();
            return motivo;
        }

        return null;
    }

    [GeneratedRegex(@"Tests run: (\d+), Failures: (\d+), Errors: (\d+), Skipped: \d+\s*$")]
    private static partial Regex LinhaDeTestes();

    [GeneratedRegex(@"^\[ERROR\] .*\.java:\[\d+")]
    private static partial Regex ErroJava();

    [GeneratedRegex(@"^\[ERROR\]   \S")]
    private static partial Regex ErroIndentado();

    [GeneratedRegex("<<< (FAILURE|ERROR)!")]
    private static partial Regex TesteFalhou();

    [GeneratedRegex(@"^\[ERROR\] Failed to execute goal [^ ]+ \([^)]*\) on project [^:]+: (.*)$")]
    private static partial Regex GoalFalhou();

    [GeneratedRegex(@"^\[ERROR\] \S")]
    private static partial Regex ErroGenerico();

    [GeneratedRegex(@"COMPILATION ERROR|Re-run Maven|To see the full|For more information|^\[ERROR\] \[Help|^\[ERROR\] ->")]
    private static partial Regex Ruido();

    [GeneratedRegex(@"^\[ERROR\]\s*")]
    private static partial Regex PrefixoErro();

    [GeneratedRegex(@"\s*->\s*\[Help 1\]\s*$")]
    private static partial Regex SufixoHelp();
}
