using Menage.Core.Entregas;

namespace Menage.Core.Beta;

/// <summary>Projeto que o Gerador sabe montar: onde procurar a pasta, JDK e goals do Maven ({modo} vira install ou deploy).</summary>
public sealed record ProjetoDoGerador
{
    public string Nome { get; init; } = "";

    public string Grupo { get; init; } = "";

    /// <summary>Caminhos relativos à pasta base onde o projeto pode estar (o primeiro com pom.xml vale).</summary>
    public List<string> Pastas { get; init; } = [];

    public string Jdk { get; init; } = "";

    public string Goals { get; init; } = "clean {modo}";

}

/// <summary>Seleção de projetos + modo guardada com um nome. A ordem da lista é a ordem de execução.</summary>
public sealed record FavoritoDoGerador
{
    public string Nome { get; init; } = "";

    public bool Deploy { get; init; }

    public List<string> Projetos { get; init; } = [];

    public string Resumo => $"{(Deploy ? "deploy" : "install")} · {Projetos.Count} {(Projetos.Count == 1 ? "projeto" : "projetos")}";

    public bool Igual(bool deploy, IEnumerable<string> selecionados) => Deploy == deploy && Projetos.SequenceEqual(selecionados);
}

/// <summary>"Limpar target ao gerar": desligado por padrão; a escolha dos projetos fica guardada mesmo desligado.</summary>
public sealed record LimparTargetConfig
{
    public bool Ativo { get; init; }

    public List<string> Projetos { get; init; } = [];

    public bool Vale(string projeto) => Ativo && Projetos.Contains(projeto, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Tudo o que o Gerador guarda (<c>gerador-beta.json</c>).</summary>
public sealed record ConfiguracaoDoGerador
{
    public const string NomeDoArquivo = "gerador-beta";

    /// <summary>Pastas onde os projetos ficam lado a lado (ex.: C:\wmw\dev\dev1). Vazio = só os projetos do Menage.</summary>
    public List<string> PastasBase { get; init; } = [];

    public string MavenBin { get; init; } = @"C:\wmw\ferramentas\apache-maven-3.9.6\bin";

    public string Java8Home { get; init; } = @"C:\wmw\ferramentas\java\jdk8";

    public string Parametros { get; init; } = "-Duser.language.format=pt -Dfile.encoding=ISO-8859-1";

    public Dictionary<string, string> Jdks { get; init; } = new() { ["8"] = @"C:\wmw\ferramentas\java\jdk8", ["17"] = @"C:\wmw\ferramentas\java\jdk17" };

    public List<ProjetoDoGerador> Projetos { get; init; } = ProjetosPadrao.Lista();

    public LimparTargetConfig LimparTarget { get; init; } = new();

    public List<FavoritoDoGerador> Favoritos { get; init; } = [];

    public ConfiguracaoDeEntregas Entregas { get; init; } = ConfiguracaoDeEntregas.Padrao();

    /// <summary>Última base usada (vazio = projetos do Menage) e último modo: a tela abre como foi fechada.</summary>
    public string UltimaBase { get; init; } = "";

    public bool UltimoDeploy { get; init; }

    /// <summary>Comandos digitados na grade no lugar do padrão (nome do projeto → comando inteiro).</summary>
    public Dictionary<string, string> ComandosPersonalizados { get; init; } = [];

    public static Func<ConfiguracaoDoGerador> Padrao => () => new ConfiguracaoDoGerador();
}

/// <summary>Os projetos que o Git Manager antigo trazia de fábrica.</summary>
static class ProjetosPadrao
{
    public static List<ProjetoDoGerador> Lista() =>
    [
        Fw("framework-util", "17", "framework/framework-util"),
        Fw("framework-service", "17", "framework/framework-service"),
        Fw("framework-core", "17", "framework/framework-core"),
        Fw("framework-integrator", "17", "framework/framework-integrator"),
        Fw("framework-integrator-tc", "17", "framework/framework-integrator-tc"),
        Fw("framework-web", "17", "framework/framework-web"),
        new() { Nome = "framework-tc", Grupo = "Framework", Jdk = "8", Pastas = ["pda/framework-tc", "framework-tc"] },
        new()
        {
            Nome = "lavenderepda", Grupo = "PDA", Jdk = "8", Pastas = ["pda/lavenderepda", "lavenderepda"],
        },
        Web("lavenderecore", "clean antrun:run {modo}"),
        Web("lavenderedados", "clean {modo}"),
        Web("lavendereweb", "clean {modo}"),
    ];

    static ProjetoDoGerador Fw(string nome, string jdk, string pasta) =>
        new() { Nome = nome, Grupo = "Framework", Jdk = jdk, Pastas = [pasta, nome] };

    static ProjetoDoGerador Web(string nome, string goals) =>
        new() { Nome = nome, Grupo = "Web", Jdk = "17", Goals = goals, Pastas = [$"web/{nome}", nome] };
}
