using Menage.Core.Banco;
using Menage.Core.Estado;
using Menage.Core.Saida;
using Menage.Core.Operacoes;
using Menage.Core.Historico;
using Menage.Core.ModoBeta;
using Menage.Core.Operacoes.BuscarCommit;
using Menage.Core.Operacoes.Checkout;
using Menage.Core.Operacoes.CherryPick;
using Menage.Core.Operacoes.CriarBranch;
using Menage.Core.Operacoes.Decoradores;
using Menage.Core.Operacoes.Fetch;
using Menage.Core.Operacoes.Pull;
using Menage.Core.Operacoes.Rebase;
using Menage.Core.Operacoes.Stash;
using Menage.Core.Operacoes.Status;
using Menage.Core.Operacoes.Sync;
using Menage.Core.Protecao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Menage.Core;

public static class MenageCoreExtensions
{
    /// <summary>
    /// Liga o Core. O app registra antes (ou depois, com Replace) as implementações de tela:
    /// <see cref="IServicoDeDialogos"/>, <see cref="IServicoDeJanelas"/>, <see cref="IProgresso"/>,
    /// <see cref="IPerfilAtual"/>, <see cref="IResolvedorDeConflitos"/>. Os padrões abaixo só existem para o app subir e para testes.
    /// </summary>
    public static IServiceCollection AddMenageCore(this IServiceCollection servicos, PastaDeDados? pasta = null)
    {
        servicos.AddSingleton(pasta ?? PastaDeDados.Padrao());
        servicos.AddSingleton<IProcessoRunner, ProcessoRunner>();
        servicos.AddSingleton<IAmbienteSsh>(sp => new AmbienteSsh(sp.GetRequiredService<IProcessoRunner>()));
        servicos.AddSingleton<IGit, GitExecutor>();
        servicos.TryAddSingleton<IConfiguracaoDeProjetos>(sp => new ConfiguracaoDeProjetos(sp.GetRequiredService<PastaDeDados>()));
        servicos.TryAddSingleton<IModoBeta>(sp => sp.GetRequiredService<IConfiguracaoDeProjetos>());
        servicos.TryAddSingleton<IProjetos, ProjetosConfigurados>();
        servicos.TryAddSingleton<IProgresso>(sp => sp.GetRequiredService<CoordenadorDeOperacoes>());
        servicos.TryAddSingleton<IPerfilAtual, PerfilFixo>();
        servicos.TryAddSingleton<IResolvedorDeConflitos, ResolvedorQueAborta>();
        servicos.TryAddSingleton(sp => new ProtecaoDeBranches(sp.GetRequiredService<PastaDeDados>()));
        servicos.TryAddSingleton<IRegrasDeProtecao>(sp => sp.GetRequiredService<ProtecaoDeBranches>());
        servicos.TryAddSingleton<IValidadorDeCommit>(sp => sp.GetRequiredService<ProtecaoDeBranches>());
        servicos.AddSingleton<ICatalogoDeComandos>(sp => CatalogoDeComandos.Montar(sp.GetServices<IModulo>()));
        servicos.AddSingleton<IExecutorDeOperacoes>(MontarExecutor);
        return servicos.AddNucleoGit().AddOperacoesDeLote();
    }

    /// <summary>Registra uma operação em lote e o módulo que expõe seus comandos.</summary>
    public static IServiceCollection AddModulo<TModulo>(this IServiceCollection servicos)
        where TModulo : class, IModulo =>
        servicos.AddSingleton<IModulo, TModulo>();

    static IServiceCollection AddNucleoGit(this IServiceCollection servicos) =>
        servicos
            .AddSingleton<ConsultasGit>()
            .AddSingleton<SugestoesDeBranches>()
            .AddSingleton<GuardaDePendencias>()
            .AddSingleton<LeitorDeEstado>()
            .AddSingleton<ILeitorDeSituacao, LeitorDeSituacao>()
            .AddSingleton<InformacoesDoAmbiente>()
            .AddSingleton<SaidaDeOperacoes>()
            .AddSingleton<CoordenadorDeOperacoes>()
            .AddSingleton<IServicoDeBanco, ServicoDeBancoFalso>()
            .AddSingleton<LeitorDeHistorico>()
            .AddSingleton<AjustadorDeFrameworks>()
            .AddSingleton<AutoStash>()
            .AddSingleton<PreparadorDeCherryPick>()
            .AddSingleton<AplicadorDeCommit>()
            .AddSingleton<ConflitoDeRebase>()
            .AddSingleton<FetchSilencioso>()
            .AddSingleton<FetchEmLote>()
            .AddSingleton<PullEmLote>()
            .AddSingleton<SyncEmLote>()
            .AddSingleton<StatusEmLote>()
            .AddSingleton<CheckoutEmLote>()
            .AddSingleton<CriarBranchEmLote>()
            .AddSingleton<StashEmLote>()
            .AddSingleton<StashPopEmLote>()
            .AddSingleton<CherryPickEmLote>()
            .AddSingleton<RebaseEmLote>()
            .AddSingleton<BuscarCommitEmLote>();

    // Ordem de fora para dentro: log, perfil, proteção, convenção de commit, execução.
    static IExecutorDeOperacoes MontarExecutor(IServiceProvider sp)
    {
        IExecutorDeOperacoes executor = new ExecutorDeOperacoes();
        executor = new ExecutorComConvencaoDeCommit(executor, sp.GetRequiredService<IValidadorDeCommit>());
        executor = new ExecutorComProtecao(executor, sp.GetRequiredService<IGit>(), sp.GetRequiredService<IRegrasDeProtecao>());
        executor = new ExecutorComPerfil(executor, sp.GetRequiredService<IPerfilAtual>());
        return new ExecutorComLog(executor, sp.GetRequiredService<ILoggerFactory>().CreateLogger<ExecutorComLog>());
    }
}
