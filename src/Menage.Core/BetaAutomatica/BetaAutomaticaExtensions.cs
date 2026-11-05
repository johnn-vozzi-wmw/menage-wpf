using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Menage.Core.BetaAutomatica;

public static class BetaAutomaticaExtensions
{
    /// <summary>Motor da Beta automática. Depende do Gerador de Beta (<c>AddGeradorBeta</c>) para o Maven e a lista de projetos.</summary>
    public static IServiceCollection AddBetaAutomatica(this IServiceCollection servicos)
    {
        servicos.TryAddSingleton<IFabricaDeJira, FabricaDeJiraHttp>();
        servicos.AddSingleton<IArmazenamento<ConfigBetaAuto>>(sp =>
            new Armazenamento<ConfigBetaAuto>(sp.GetRequiredService<PastaDeDados>(), ConfigBetaAuto.NomeDoArquivo, padrao: ConfigBetaAuto.Padrao));
        servicos.AddSingleton(sp => new ConsoleDaBeta(sp.GetRequiredService<PastaDeDados>()));
        servicos.AddSingleton<VerificadorDeBranches>();
        servicos.AddSingleton<PreparadorDeClone>();
        servicos.AddSingleton<ServicoDeBetaAutomatica>();
        return servicos;
    }
}
