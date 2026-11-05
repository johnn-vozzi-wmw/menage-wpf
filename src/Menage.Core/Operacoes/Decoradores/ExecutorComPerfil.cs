using Menage.Core.Perfis;

namespace Menage.Core.Operacoes.Decoradores;

/// <summary>
/// Recusa operações que o perfil de uso atual não libera. Quando há <see cref="IRegrasDePerfil"/> e a operação corresponde a uma
/// funcionalidade do catálogo, vale também a lista do perfil (editável), além dos perfis que a própria operação declara.
/// </summary>
public sealed class ExecutorComPerfil(IExecutorDeOperacoes interno, IPerfilAtual perfil, IRegrasDePerfil? regras = null) : IExecutorDeOperacoes
{
    public Task<Resultado<ResumoDoLote<TResultado>>> ExecutarAsync<TOpcoes, TResultado>(
        OperacaoEmLote<TOpcoes, TResultado> operacao, TOpcoes opcoes, SinaisDoLote sinais = default)
    {
        if (Libera(operacao.Id, operacao.PerfisQueLiberam)) return interno.ExecutarAsync(operacao, opcoes, sinais);

        var erro = new Erro($"'{operacao.Nome}' não está liberada no perfil {perfil.Atual}", null, "Troque o perfil de uso nas Configurações.");
        return Task.FromResult(Resultado.Falha<ResumoDoLote<TResultado>>(erro));
    }

    bool Libera(string idDaOperacao, IReadOnlyCollection<PerfilDeUso> declarados) =>
        declarados.Contains(perfil.Atual) &&
        (regras is null || Funcionalidades.DaOperacao(idDaOperacao) is not { } funcao || regras.Permite(funcao));
}
