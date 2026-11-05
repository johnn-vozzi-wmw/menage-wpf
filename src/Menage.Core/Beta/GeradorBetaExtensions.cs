using Menage.Core.Entregas;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.Core.Beta;

public static class GeradorBetaExtensions
{
    /// <summary>Motor do Gerador de Beta e das Entregas. O App registra <see cref="IAcoesDeEntrega"/>.</summary>
    public static IServiceCollection AddGeradorBeta(this IServiceCollection servicos) =>
        servicos
            .AddSingleton<IArmazenamento<ConfiguracaoDoGerador>>(sp =>
                new Armazenamento<ConfiguracaoDoGerador>(sp.GetRequiredService<PastaDeDados>(), ConfiguracaoDoGerador.NomeDoArquivo, padrao: ConfiguracaoDoGerador.Padrao))
            .AddSingleton<RepositorioDoGerador>()
            .AddSingleton<PreparadorDeGeracao>()
            .AddSingleton<ExecutorDoGerador>()
            .AddSingleton<ServicoDeEntrega>();
}
