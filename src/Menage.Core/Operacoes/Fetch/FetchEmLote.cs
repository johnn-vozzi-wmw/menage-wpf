namespace Menage.Core.Operacoes.Fetch;

/// <param name="Prune">Remove referências a branches que sumiram do remoto.</param>
public sealed record OpcoesDeFetch(bool Prune = true);

/// <param name="TrouxeNovidades">Falso quando o projeto já estava atualizado.</param>
public sealed record ResultadoDeFetch(bool TrouxeNovidades);

/// <summary>
/// OPERAÇÃO MODELO: só o que é específico do Fetch. Validação, confirmação, progresso, parada,
/// tratamento de exceção e resumo vêm de <see cref="OperacaoEmLote{TOpcoes,TResultado}"/>.
/// </summary>
public sealed class FetchEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git)
    : OperacaoEmLote<OpcoesDeFetch, ResultadoDeFetch>(projetos, dialogos, progresso)
{
    public override string Id => "git.fetch";

    public override string Nome => "Fetch";

    protected override async Task<ResultadoDoProjeto<ResultadoDeFetch>> ExecutarEmProjeto(
        Projeto projeto, OpcoesDeFetch opcoes, CancellationToken cancelar)
    {
        var remotos = await git.ExecutarAsync(Git.Em(projeto.Caminho).Comando("remote"), cancelar);
        if (!remotos.EhOk) return ResultadoDoProjeto<ResultadoDeFetch>.Falha(remotos.Erro);
        if (remotos.Valor.Texto.Trim().Length == 0)
            return ResultadoDoProjeto<ResultadoDeFetch>.Aviso("Sem remoto configurado");

        var args = Git.Em(projeto.Caminho).Comando("fetch").Opcao("--all");
        if (opcoes.Prune) args = args.Opcao("--prune");

        var fetch = await git.ExecutarAsync(args, cancelar);
        if (!fetch.EhOk) return ResultadoDoProjeto<ResultadoDeFetch>.Falha(fetch.Erro);

        // o git escreve o que baixou no stderr; sem texto, já estava atualizado
        var novidades = fetch.Valor.TextoErro.Trim().Length > 0 || fetch.Valor.Texto.Trim().Length > 0;
        return ResultadoDoProjeto<ResultadoDeFetch>.Ok(new(novidades), novidades ? "Atualizado" : "Já estava atualizado");
    }
}
