using Menage.Core.Historico;
using Menage.Core.ModoBeta;
using Menage.Core.Operacoes.Checkout;
using Menage.Core.Operacoes.CherryPick;
using Menage.Core.Operacoes.Rebase;

namespace Menage.Tests.NucleoGit;

/// <summary>Decide os conflitos por uma função do teste; sem função, aborta (o padrão seguro).</summary>
public sealed class ResolvedorFalso : IResolvedorDeConflitos
{
    public Func<ConflitoDoGit, DecisaoDeConflito> Decisao { get; set; } = _ => DecisaoDeConflito.Abortar;

    public int Perguntas { get; private set; }

    public Task<DecisaoDeConflito> DecidirAsync(ConflitoDoGit conflito, CancellationToken cancelar = default)
    {
        Perguntas++;
        return Task.FromResult(Decisao(conflito));
    }
}

/// <summary>Um repositório descartável com origin, as operações do núcleo montadas sobre ele e atalhos de leitura.</summary>
public abstract class NucleoGitBase : IDisposable
{
    protected GitExecutor GitExec { get; } = GitDeTeste.Criar();

    protected NucleoGitBase()
    {
        Consultas = new ConsultasGit(GitExec);
        Guarda = new GuardaDePendencias(GitExec, Consultas);
        Projeto = Repo.ComoProjeto("projeto-teste");
        Projetos.Adicionar(Projeto);
        Projetos.Marcar(Projeto.Caminho, true);
    }

    protected RepositorioDescartavel Repo { get; } = RepositorioDescartavel.Criar();

    protected DialogosFalsos Dialogos { get; } = new();

    protected ProjetosEmMemoria Projetos { get; } = new();

    protected ResolvedorFalso Resolvedor { get; } = new();

    protected ConsultasGit Consultas { get; }

    protected GuardaDePendencias Guarda { get; }

    protected Projeto Projeto { get; }

    public void Dispose()
    {
        Liberar();
        Repo.Dispose();
    }

    protected virtual void Liberar()
    {
    }

    protected static IProgresso Progresso => new ProgressoNulo();

    protected string Arquivo(string nome) => Path.Combine(Repo.Caminho, nome);

    protected void Escrever(string nome, string conteudo) => File.WriteAllText(Arquivo(nome), conteudo);

    protected string Assunto(string rev = "HEAD") => Repo.Rodar("log", "-1", "--format=%s", rev);

    protected AjustadorDeFrameworks Frameworks(bool beta = false) => new(GitExec, Consultas, Projetos, new ModoBetaFixo(beta));

    protected CheckoutEmLote Checkout(bool beta = false) =>
        new(Projetos, Dialogos, Progresso, GitExec, Consultas, new AutoStash(GitExec, Consultas), Frameworks(beta));

    protected PreparadorDeCherryPick Preparador() => new(Consultas, new LeitorDeHistorico(GitExec));

    protected CherryPickEmLote CherryPick() =>
        new(Projetos, Dialogos, Progresso, GitExec, Consultas, new AplicadorDeCommit(GitExec, Consultas, Resolvedor));

    protected RebaseEmLote Rebase() =>
        new(Projetos, Dialogos, Progresso, GitExec, Consultas, Guarda, new ConflitoDeRebase(GitExec, Consultas, Resolvedor));

    protected static ResultadoDoProjeto<T> Unico<T>(Resultado<ResumoDoLote<T>> resultado)
    {
        resultado.EhOk.Should().BeTrue(resultado.ToString());
        return resultado.Valor.Itens.Single().Resultado;
    }
}
