using Menage.Core.Operacoes.Commit;
using Menage.Core.Operacoes.ComDevelop;
using Menage.Core.Operacoes.CommitPom;
using Menage.Core.Operacoes.DesfazerCommit;
using Menage.Core.Operacoes.Excluir;
using Menage.Core.Operacoes.Limpeza;
using Menage.Core.Operacoes.Push;
using Menage.Core.Operacoes.Renomear;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.Core.Operacoes;

/// <summary>Registro das operações do módulo de operações em lote (commit, push, branches, limpeza, ferramentas...).</summary>
public static class OperacoesDeLoteExtensions
{
    public static IServiceCollection AddOperacoesDeLote(this IServiceCollection servicos) =>
        servicos
            .AddSingleton<EnvioParaOrigin>()
            .AddSingleton<CommitEmLote>()
            .AddSingleton<PushEmLote>()
            .AddSingleton<ExclusorDeRefs>()
            .AddSingleton<ExcluirEmLote>()
            .AddSingleton<RenomearBranchEmLote>()
            .AddSingleton<ComDevelopEmLote>()
            .AddSingleton<LimpezaEmLote>()
            .AddSingleton<DesfazerCommitEmLote>()
            .AddSingleton<CommitPomEmLote>();
}
