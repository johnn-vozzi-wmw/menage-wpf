namespace Menage.Core.Operacoes;

/// <summary>Identificação e regras de acesso de uma operação, lidas pelos decoradores (perfil, proteção).</summary>
public interface IOperacaoEmLote
{
    string Id { get; }

    string Nome { get; }

    IReadOnlyCollection<PerfilDeUso> PerfisQueLiberam { get; }

    /// <summary>Reescreve histórico ou publica: sujeita à proteção de branches.</summary>
    bool Destrutiva { get; }
}

/// <summary>
/// Fluxo único das operações em lote: validar, prévia, confirmar, executar por projeto (com progresso e
/// "parar após o atual") e resumo. Cada operação implementa só <see cref="ExecutarEmProjeto"/>.
/// </summary>
public abstract class OperacaoEmLote<TOpcoes, TResultado>(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso) : IOperacaoEmLote
{
    /// <summary>Todos os projetos conhecidos, para operações que não usam só os marcados (ex.: "mudar a branch de TODOS").</summary>
    protected IProjetos ProjetosConhecidos => projetos;

    public abstract string Id { get; }

    public abstract string Nome { get; }

    public virtual IReadOnlyCollection<PerfilDeUso> PerfisQueLiberam => ConjuntosDePerfis.Todos;

    public virtual bool Destrutiva => false;

    /// <summary>Projetos que a operação vai percorrer; por padrão, os marcados na grade.</summary>
    public virtual IReadOnlyList<Projeto> ProjetosAlvo(TOpcoes opcoes) => projetos.Marcados;

    /// <summary>Erro que impede a execução (nenhum projeto, opção inválida...) ou nulo.</summary>
    protected virtual Erro? Validar(TOpcoes opcoes, IReadOnlyList<Projeto> alvo) =>
        alvo.Count == 0 ? new Erro("Nenhum projeto marcado", null, "Marque ao menos um projeto na grade.") : null;

    /// <summary>Como <see cref="Validar"/>, para validações que precisam do git (nome de branch, por exemplo).</summary>
    protected virtual Task<Erro?> ValidarAsync(TOpcoes opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
        Task.FromResult(Validar(opcoes, alvo));

    /// <summary>Prévia a confirmar; nulo significa executar direto (operações só de leitura).</summary>
    protected virtual Task<Previa?> MontarPreviaAsync(TOpcoes opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
        Task.FromResult<Previa?>(null);

    protected abstract Task<ResultadoDoProjeto<TResultado>> ExecutarEmProjeto(Projeto projeto, TOpcoes opcoes, CancellationToken cancelar);

    /// <summary>Passo depois do último projeto (itens extras e observações do resumo); roda também se o lote parou.</summary>
    protected virtual Task<ConclusaoDoLote<TResultado>> ConcluirAsync(
        TOpcoes opcoes, IReadOnlyList<ItemDoLote<TResultado>> itens, bool interrompido, CancellationToken cancelar) =>
        Task.FromResult(new ConclusaoDoLote<TResultado>());

    public async Task<Resultado<ResumoDoLote<TResultado>>> ExecutarAsync(TOpcoes opcoes, SinaisDoLote sinais = default)
    {
        var alvo = ProjetosAlvo(opcoes);
        if (await ValidarAsync(opcoes, alvo, sinais.Abortar) is { } invalido) return Resultado.Falha<ResumoDoLote<TResultado>>(invalido);

        var previa = await MontarPreviaAsync(opcoes, alvo, sinais.Abortar);
        if (previa is not null && !await dialogos.ConfirmarAsync(previa))
            return Resultado.Falha<ResumoDoLote<TResultado>>(Erro.Cancelado);

        var (itens, naoExecutados, interrompido) = await PercorrerAsync(alvo, opcoes, sinais);
        var conclusao = await ConcluirSemExcecaoAsync(opcoes, itens, interrompido, sinais.Abortar);
        var resumo = new ResumoDoLote<TResultado>(Nome, [.. itens, .. conclusao.Itens], interrompido)
        {
            NaoExecutados = naoExecutados,
            Observacoes = conclusao.Observacoes,
        };
        await dialogos.MostrarResumoAsync(resumo);
        return Resultado.Ok(resumo);
    }

    async Task<(List<ItemDoLote<TResultado>> Itens, IReadOnlyList<string> NaoExecutados, bool Interrompido)> PercorrerAsync(
        IReadOnlyList<Projeto> alvo, TOpcoes opcoes, SinaisDoLote sinais)
    {
        var itens = new List<ItemDoLote<TResultado>>();
        var interrompido = false;
        for (var i = 0; i < alvo.Count; i++)
        {
            if (sinais.PararApos.IsCancellationRequested || sinais.Abortar.IsCancellationRequested)
                return (itens, Nomes(alvo, i), true);

            var item = await ExecutarComProgressoAsync(alvo[i], i + 1, alvo.Count, opcoes, sinais.Abortar);
            itens.Add(item);
            if (item.Resultado.InterromperLote) return (itens, Nomes(alvo, i + 1), true);
            if (item.Resultado.Erro == Erro.Cancelado) interrompido = true;
        }

        return (itens, [], interrompido);
    }

    static string[] Nomes(IReadOnlyList<Projeto> alvo, int inicio) => [.. alvo.Skip(inicio).Select(p => p.Nome)];

    async Task<ConclusaoDoLote<TResultado>> ConcluirSemExcecaoAsync(
        TOpcoes opcoes, IReadOnlyList<ItemDoLote<TResultado>> itens, bool interrompido, CancellationToken cancelar)
    {
        try
        {
            return await ConcluirAsync(opcoes, itens, interrompido, cancelar);
        }
        catch (Exception ex)
        {
            return new ConclusaoDoLote<TResultado>
            {
                Observacoes = [new ObservacaoDoLote($"Falha ao concluir o lote: {ex.Message}", Situacao.Erro)],
            };
        }
    }

    async Task<ItemDoLote<TResultado>> ExecutarComProgressoAsync(Projeto projeto, int indice, int total, TOpcoes opcoes, CancellationToken abortar)
    {
        progresso.Relatar(new(FaseDoProjeto.Iniciou, indice, total, projeto.Nome));
        var resultado = await ExecutarSemExcecaoAsync(projeto, opcoes, abortar);
        progresso.Relatar(new(FaseDoProjeto.Terminou, indice, total, projeto.Nome, resultado.Situacao, resultado.Mensagem));
        return new ItemDoLote<TResultado>(projeto, resultado);
    }

    // Exceção em um projeto vira erro daquele projeto; os demais continuam.
    async Task<ResultadoDoProjeto<TResultado>> ExecutarSemExcecaoAsync(Projeto projeto, TOpcoes opcoes, CancellationToken abortar)
    {
        try
        {
            return await ExecutarEmProjeto(projeto, opcoes, abortar);
        }
        catch (OperationCanceledException) when (abortar.IsCancellationRequested)
        {
            return ResultadoDoProjeto<TResultado>.Falha(Erro.Cancelado);
        }
        catch (Exception ex)
        {
            return ResultadoDoProjeto<TResultado>.Falha(new Erro("Falha inesperada", ex.Message));
        }
    }
}
