namespace Menage.Core.Repos;

public interface IGit
{
    /// <summary>Executa e devolve a saída crua, qualquer que seja o código de saída (ex.: <c>diff --quiet</c>).</summary>
    Task<SaidaProcesso> ExecutarBrutoAsync(GitArgs args, CancellationToken cancelar = default);

    /// <summary>Executa; código diferente de zero vira <see cref="Erro"/> classificado (autenticação, rede, SSH...).</summary>
    Task<Resultado<SaidaProcesso>> ExecutarAsync(GitArgs args, CancellationToken cancelar = default);
}
