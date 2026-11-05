using System.Text;

namespace Menage.Core.Beta;

/// <summary>
/// Monta o <c>.cmd</c> que roda o Maven de um projeto: ambiente (JAVA_HOME, JAVA8_HOME, PATH), pasta e comando com a saída
/// anexada ao log. O arquivo substitui o <c>gerador-beta-executar.ps1</c> antigo: o log vai sendo gravado pelo próprio cmd, então a
/// tela acompanha a execução lendo o arquivo enquanto o <see cref="Processos.IProcessoRunner"/> espera o processo terminar.
/// </summary>
public static class ScriptDoProjeto
{
    /// <summary>Linhas do ambiente de um projeto; também servem para abrir um cmd interativo na pasta do projeto.</summary>
    public static IReadOnlyList<string> Ambiente(string javaHome, string java8Home, string mavenBin) =>
    [
        $"set \"JAVA_HOME={javaHome}\"",
        $"set \"JAVA8_HOME={java8Home}\"",
        $"set \"PATH={javaHome}\\bin;{mavenBin};%PATH%\"",
    ];

    public static string Montar(PlanoDeProjeto plano, PedidoDeGeracao pedido, string log, string? jvmOpts)
    {
        var s = new StringBuilder();
        s.AppendLine("@echo off");
        foreach (var linha in Ambiente(plano.JavaHome, pedido.Java8Home, pedido.MavenBin)) s.AppendLine(linha);
        if (jvmOpts is not null) s.AppendLine(System.FormattableString.Invariant($"set \"JAVA_TOOL_OPTIONS={jvmOpts}\""));
        s.AppendLine(System.FormattableString.Invariant($"cd /d \"{plano.Pasta}\""));
        s.AppendLine(System.FormattableString.Invariant($"call {Escapar(plano.Comando)} >> \"{log}\" 2>&1"));
        s.AppendLine("exit /b %errorlevel%");
        return s.ToString();
    }

    // No .cmd o % é caractere especial; dobrado, vale como o próprio %.
    static string Escapar(string comando) => comando.Replace("%", "%%");
}
