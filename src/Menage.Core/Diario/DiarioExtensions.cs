using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Menage.Core.Diario;

public static class DiarioExtensions
{
    /// <summary>Motor do Diário de trabalho (commits do dia, jornada, resumo, IA opcional e Jira).</summary>
    public static IServiceCollection AddDiario(this IServiceCollection servicos)
    {
        servicos.TryAddSingleton<IArmazenamento<ConfiguracaoDoDiario>>(sp =>
            new Armazenamento<ConfiguracaoDoDiario>(sp.GetRequiredService<PastaDeDados>(), ConfiguracaoDoDiario.NomeDoArquivo, padrao: ConfiguracaoDoDiario.Padrao));
        servicos.TryAddSingleton<IVariaveisDeAmbiente, VariaveisDoWindows>();
        servicos.TryAddSingleton(sp => new FabricaDeClientes(new HttpClient(), sp.GetRequiredService<IVariaveisDeAmbiente>()));
        return servicos
            .AddSingleton<RepositorioDoDiario>()
            .AddSingleton<ColetorDoDiario>()
            .AddSingleton<LeitorDeDiffs>()
            .AddSingleton<ServicoDoDiario>();
    }
}
