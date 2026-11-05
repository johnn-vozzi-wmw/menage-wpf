namespace Menage.Core.Operacoes.Ferramentas;

/// <param name="Mensagem">Com mensagem a tag é anotada; sem, é leve.</param>
/// <param name="De">Branch, tag ou commit de onde sai a tag; vazio = o commit atual de cada projeto.</param>
public sealed record OpcoesDeTag(string Nome, string Mensagem = "", string De = "", bool Enviar = true);

public sealed record ResultadoDeTag(bool Criada, bool Enviada);

/// <summary>Cria a mesma tag em vários projetos. Nunca move uma tag que já existe (local ou no origin).</summary>
public sealed class TagEmMassaEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas)
    : OperacaoEmLote<OpcoesDeTag, ResultadoDeTag>(projetos, dialogos, progresso)
{
    public override string Id => "ferramentas.tag-em-massa";

    public override string Nome => "Criar tag em massa";

    protected override async Task<Erro?> ValidarAsync(OpcoesDeTag opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
        await consultas.NomeDeBranchValidoAsync(opcoes.Nome.Trim(), cancelar) ? Validar(opcoes, alvo) : new Erro($"Nome de tag inválido: \"{opcoes.Nome}\"");

    protected override Task<Previa?> MontarPreviaAsync(OpcoesDeTag opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
        Task.FromResult<Previa?>(new Previa("Criar tag em massa",
            $"Criar a tag \"{opcoes.Nome.Trim()}\" {(opcoes.De.Trim().Length > 0 ? $"a partir de \"{opcoes.De.Trim()}\"" : "no commit atual")}{(opcoes.Enviar ? " e enviar ao origin" : "")}? Tags existentes nunca são movidas.",
            [.. alvo.Select(p => p.Nome)]));

    protected override async Task<ResultadoDoProjeto<ResultadoDeTag>> ExecutarEmProjeto(Projeto projeto, OpcoesDeTag opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var nome = opcoes.Nome.Trim();
        await consultas.AtualizarRefsAsync(repo, cancelar);
        if (await consultas.RefExisteAsync(repo, "refs/tags/" + nome, cancelar))
            return ResultadoDoProjeto<ResultadoDeTag>.Falha($"a tag {nome} já existe - não foi movida");

        var origem = opcoes.De.Trim().Length == 0 ? "HEAD" : (await consultas.ResolverBaseAsync(repo, opcoes.De.Trim(), cancelar))?.Ref;
        if (origem is null) return ResultadoDoProjeto<ResultadoDeTag>.Falha($"\"{opcoes.De}\" não existe");

        var args = Git.Em(repo).Comando("tag");
        if (opcoes.Mensagem.Trim().Length > 0) args = args.Opcao("-a").Opcao("-m", opcoes.Mensagem.Trim());
        var criar = await git.ExecutarAsync(args.Argumento(nome).Argumento(origem), cancelar);
        if (!criar.EhOk) return ResultadoDoProjeto<ResultadoDeTag>.Falha(new Erro("Não foi possível criar a tag", criar.Erro.ToString()));
        if (!opcoes.Enviar) return ResultadoDoProjeto<ResultadoDeTag>.Ok(new(true, false), $"tag {nome} criada (só local)");

        var push = await git.ExecutarAsync(Git.Em(repo).Comando("push").Argumento("origin").Argumento("refs/tags/" + nome).ComTimeout(ConsultasGit.TempoLongo), cancelar);
        return push.EhOk
            ? ResultadoDoProjeto<ResultadoDeTag>.Ok(new(true, true), $"tag {nome} criada e enviada")
            : ResultadoDoProjeto<ResultadoDeTag>.Aviso($"tag {nome} criada, mas o push falhou: {push.Erro.Causa}", new(true, false));
    }
}
