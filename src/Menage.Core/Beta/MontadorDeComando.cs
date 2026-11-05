namespace Menage.Core.Beta;

/// <summary>Monta o comando Maven de um projeto: goals efetivos, parâmetros comuns, JDK e opções da JVM. Sem I/O.</summary>
public static class MontadorDeComando
{
    /// <summary>lavenderepda (JDK 8): o surefire quebra ao gerar lambdas; excluir esta classe do JIT contorna.</summary>
    public const string OpcoesDoSurefire =
        "-XX:CompileCommand=quiet -XX:CompileCommand=exclude,java/lang/invoke/InnerClassLambdaMetafactory.spinInnerClass";

    public static string Goals(ProjetoDoGerador projeto, bool deploy) =>
        projeto.Goals.Replace("{modo}", deploy ? "deploy" : "install");

    /// <summary>Argumentos do Maven (sem o "mvn"): <c>-B clean install -D...</c>.</summary>
    public static string Argumentos(ProjetoDoGerador projeto, ConfiguracaoDoGerador config, bool deploy) =>
        $"-B {Goals(projeto, deploy)} {config.Parametros}".Trim();

    /// <summary>O que aparece na coluna Comando: o digitado pelo usuário ou o padrão.</summary>
    public static string Completo(ProjetoDoGerador projeto, ConfiguracaoDoGerador config, bool deploy) =>
        config.ComandosPersonalizados.TryGetValue(projeto.Nome, out var proprio) && !string.IsNullOrWhiteSpace(proprio)
            ? proprio
            : $"mvn {Argumentos(projeto, config, deploy)}";

    public static bool Personalizado(ProjetoDoGerador projeto, ConfiguracaoDoGerador config) =>
        config.ComandosPersonalizados.TryGetValue(projeto.Nome, out var proprio) && !string.IsNullOrWhiteSpace(proprio);

    public static string? JavaHome(ProjetoDoGerador projeto, ConfiguracaoDoGerador config) =>
        config.Jdks.TryGetValue(projeto.Jdk, out var home) && !string.IsNullOrWhiteSpace(home) ? home : null;

    /// <summary>Erro que impede de gerar (JDK ou goals faltando) ou nulo.</summary>
    public static Erro? Validar(ProjetoDoGerador projeto, ConfiguracaoDoGerador config)
    {
        if (string.IsNullOrWhiteSpace(projeto.Goals) || string.IsNullOrWhiteSpace(projeto.Jdk))
            return new Erro($"{projeto.Nome}: falta JDK ou goals do Maven", null, "Configure o JDK e os goals do projeto.");
        var home = JavaHome(projeto, config);
        return home is null || !File.Exists(Path.Combine(home, "bin", "java.exe"))
            ? new Erro($"JDK {projeto.Jdk} não encontrado em '{home}'", $"Necessário para {projeto.Nome}.", "Ajuste o caminho do JDK na configuração do Gerador.")
            : null;
    }
}
