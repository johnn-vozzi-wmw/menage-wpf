using Microsoft.Extensions.DependencyInjection;

namespace Menage.Core.CherryPick;

public static class CherryPickExtensions
{
    public const string ArquivoDaFila = "fila-cherry";

    /// <summary>Motor da fila de cherry-pick: armazenamento (fila e configuração do script), serviço, controle e a operação em lote.</summary>
    public static IServiceCollection AddFilaDeCherryPick(this IServiceCollection servicos) => servicos
        .AddSingleton<IArmazenamento<FilaDeCherryPick>>(sp => new Armazenamento<FilaDeCherryPick>(sp.GetRequiredService<PastaDeDados>(), ArquivoDaFila))
        .AddSingleton<IArmazenamento<ConfigDoScript>>(sp => new Armazenamento<ConfigDoScript>(sp.GetRequiredService<PastaDeDados>(), ConfigDoScript.Nome, padrao: () => new ConfigDoScript()))
        .AddSingleton<ServicoDaFila>()
        .AddSingleton<RetiradorDeScript>()
        .AddSingleton<PreparoDoProjeto>()
        .AddSingleton<AplicadorDaFila>()
        .AddSingleton<ControleDaFila>()
        .AddSingleton<FilaDeCherryPickEmLote>();
}
