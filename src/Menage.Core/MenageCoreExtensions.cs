using Menage.Core.Operacoes.Decoradores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Menage.Core;

public static class MenageCoreExtensions
{
    /// <summary>
    /// Liga o Core. O app registra antes (ou depois, com Replace) as implementações de tela:
    /// <see cref="IServicoDeDialogos"/>, <see cref="IServicoDeJanelas"/>, <see cref="IProgresso"/>,
    /// <see cref="IPerfilAtual"/>. Os padrões abaixo só existem para o app subir e para testes.
    /// </summary>
    public static IServiceCollection AddMenageCore(this IServiceCollection servicos, PastaDeDados? pasta = null)
    {
        servicos.AddSingleton(pasta ?? PastaDeDados.Padrao());
        servicos.AddSingleton<IProcessoRunner, ProcessoRunner>();
        servicos.AddSingleton<IAmbienteSsh>(sp => new AmbienteSsh(sp.GetRequiredService<IProcessoRunner>()));
        servicos.AddSingleton<IGit, GitExecutor>();
        servicos.TryAddSingleton<IProjetos, ProjetosEmMemoria>();
        servicos.TryAddSingleton<IProgresso, ProgressoNulo>();
        servicos.TryAddSingleton<IPerfilAtual, PerfilFixo>();
        servicos.TryAddSingleton(RegrasDeProtecao.Padrao);
        servicos.AddSingleton<ICatalogoDeComandos>(sp => CatalogoDeComandos.Montar(sp.GetServices<IModulo>()));
        servicos.AddSingleton<IExecutorDeOperacoes>(MontarExecutor);
        return servicos;
    }

    /// <summary>Registra uma operação em lote e o módulo que expõe seus comandos.</summary>
    public static IServiceCollection AddModulo<TModulo>(this IServiceCollection servicos)
        where TModulo : class, IModulo =>
        servicos.AddSingleton<IModulo, TModulo>();

    // Ordem de fora para dentro: log, perfil, proteção, execução.
    static IExecutorDeOperacoes MontarExecutor(IServiceProvider sp)
    {
        IExecutorDeOperacoes executor = new ExecutorDeOperacoes();
        executor = new ExecutorComProtecao(executor, sp.GetRequiredService<IGit>(), sp.GetRequiredService<RegrasDeProtecao>());
        executor = new ExecutorComPerfil(executor, sp.GetRequiredService<IPerfilAtual>());
        return new ExecutorComLog(executor, sp.GetRequiredService<ILoggerFactory>().CreateLogger<ExecutorComLog>());
    }
}
