using System.IO;
using Menage.App.Modulos.BetaAutomatica;
using Menage.Core.Armazenamento;
using Menage.Core.Beta;
using Menage.Core.BetaAutomatica;
using Menage.Core.Perfis;
using Menage.Core.Processos;
using Menage.Core.Projetos;
using Menage.Core.Repos;
using Menage.TestKit;

namespace Menage.App.Tests.BetaAutomatica;

/// <summary>Jira de mentira: devolve as atividades combinadas e guarda as consultas feitas. Nenhuma chamada de rede.</summary>
sealed class JiraDeMentira : IClienteJira, IFabricaDeJira
{
    public List<IssueJira> Issues { get; } = [];

    public List<string> Consultas { get; } = [];

    public bool TemToken { get; set; } = true;

    public string NomeDaVariavel => "JIRA_TOKEN";

    public string Endereco => "http://jira.falso";

    public Task<string> UsuarioAsync(CancellationToken cancelar = default) => Task.FromResult("eu");

    public Task<IReadOnlyList<IssueJira>> BuscarCompletasAsync(string jql, int maximo, CancellationToken cancelar = default)
    {
        Consultas.Add(jql);
        IReadOnlyList<IssueJira> r = jql.StartsWith("key in", StringComparison.Ordinal) ? [] : [.. Issues];
        return Task.FromResult(r);
    }

    public IClienteJira Criar(ConfigBetaAuto config) => this;
}

/// <summary>Serviço da Beta automática sobre pasta temporária curta, Jira de mentira e sem projetos: só a lógica de apresentação é exercitada.</summary>
sealed class CenarioDasAbas : IDisposable
{
    public string Raiz { get; } = Path.Combine(Path.GetTempPath(), "ab" + Guid.NewGuid().ToString("N")[..6]);

    public JiraDeMentira Jira { get; } = new();

    public ConsoleDaBeta Console { get; }

    public ServicoDeBetaAutomatica Servico { get; }

    public DialogosFalsos Dialogos { get; } = new();

    public JanelasFalsas Janelas { get; } = new();

    public CenarioDasAbas(PerfilDeUso perfil = PerfilDeUso.Desenvolvedor)
    {
        Directory.CreateDirectory(Raiz);
        var dados = new PastaDeDados(Path.Combine(Raiz, "dados"));
        var git = GitDeTeste.Criar();
        var consultas = new ConsultasGit(git);
        var projetos = new ProjetosEmMemoria([]);
        var gerador = new RepositorioDoGerador(new Armazenamento<ConfiguracaoDoGerador>(dados, "gerador-beta", padrao: () => new ConfiguracaoDoGerador()));
        var perfilAtual = new PerfilFixo(perfil);
        Console = new ConsoleDaBeta(dados);
        var armazenamento = new Armazenamento<ConfigBetaAuto>(dados, "beta-automatica", padrao: () => new ConfigBetaAuto { PastaTrabalho = Path.Combine(Raiz, "clones") });
        Servico = new ServicoDeBetaAutomatica(
            armazenamento, Jira, new VerificadorDeBranches(git, consultas), projetos, gerador, perfilAtual, Console, dados,
            new PreparadorDeClone(git, Console), new PreparadorDeGeracao(gerador, projetos, consultas, perfilAtual, dados), new ProcessoRunner());
    }

    public ConfigBetaViewModel NovaConfig() => new(Servico, Janelas);

    public JqlViewModel NovoJql() => new(Servico);

    public DiagnosticoViewModel NovoDiagnostico() => new(Servico);

    public ConsoleDoDockViewModel NovoConsole() => new(Console);

    public BetaAutomaticaViewModel NovoPainel() => new(Servico, Dialogos, NovaConfig(), NovoJql(), NovoDiagnostico(), NovoConsole());

    public static ItemBeta Item(string chave, EstadoDoItem estado, params ProjetoDoItem[] projetos) => new()
    {
        Chave = chave, Titulo = "Titulo " + chave, Estado = estado, Branch = chave, Motivo = "comentário seu", DetectadaEm = "2026-11-06 08:00:00",
        TransicaoEm = "2026-11-06 08:00", Projetos = [.. projetos],
    };

    public static ProjetoDoItem Projeto(string nome, SituacaoDaBranch situacao, string resultado = "", bool explicito = true) =>
        new() { Nome = nome, Situacao = situacao, Resultado = resultado, Explicito = explicito };

    public void Itens(params ItemBeta[] itens) => Servico.Alterar(c => c.Itens = [.. itens]);

    public void Dispose()
    {
        Servico.Dispose();
        if (Directory.Exists(Raiz)) Directory.Delete(Raiz, recursive: true);
    }
}

