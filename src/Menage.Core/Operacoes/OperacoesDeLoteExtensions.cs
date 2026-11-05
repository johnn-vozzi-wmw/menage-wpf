using Menage.Core.Operacoes.Commit;
using Menage.Core.Operacoes.Push;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.Core.Operacoes;

/// <summary>Registro das operações do módulo de operações em lote (commit, push, branches, limpeza, ferramentas...).</summary>
public static class OperacoesDeLoteExtensions
{
    public static IServiceCollection AddOperacoesDeLote(this IServiceCollection servicos) =>
        servicos
            .AddSingleton<EnvioParaOrigin>()
            .AddSingleton<CommitEmLote>()
            .AddSingleton<PushEmLote>();
}
