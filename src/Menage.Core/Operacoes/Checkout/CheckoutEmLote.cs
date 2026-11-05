using Menage.Core.ModoBeta;

namespace Menage.Core.Operacoes.Checkout;

/// <param name="Branch">Nome da branch ou tag; "origin/x" quer a branch do remoto.</param>
/// <param name="AutoStash">Guarda as pendências ao sair da branch e restaura ao voltar para ela.</param>
public sealed record OpcoesDeCheckout(string Branch, bool AutoStash = false)
{
    public bool SoOrigin => Branch.StartsWith("origin/", StringComparison.Ordinal) && Branch.Length > 7;

    public string Nome => SoOrigin ? Branch[7..] : Branch;
}

/// <param name="Trocou">Falso nos itens do modo beta e quando a troca falhou.</param>
public sealed record ResultadoDeCheckout(bool Trocou, TipoDeRef? Tipo = null);

/// <summary>Branch local, depois origin, depois tag; se não acha ou falha, faz fetch e tenta de novo uma vez.</summary>
public sealed class CheckoutEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas, AutoStash autoStash, AjustadorDeFrameworks frameworks)
    : OperacaoEmLote<OpcoesDeCheckout, ResultadoDeCheckout>(projetos, dialogos, progresso)
{
    public override string Id => "git.checkout";

    public override string Nome => "Checkout";

    public override IReadOnlyList<Projeto> ProjetosAlvo(OpcoesDeCheckout opcoes) =>
        frameworks.SemFrameworks(base.ProjetosAlvo(opcoes), opcoes.Nome);

    protected override Erro? Validar(OpcoesDeCheckout opcoes, IReadOnlyList<Projeto> alvo) =>
        string.IsNullOrWhiteSpace(opcoes.Branch) ? new Erro("Informe a branch") : base.Validar(opcoes, alvo);

    protected override async Task<ResultadoDoProjeto<ResultadoDeCheckout>> ExecutarEmProjeto(Projeto projeto, OpcoesDeCheckout opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var guardado = opcoes.AutoStash ? await autoStash.GuardarAsync(repo, opcoes.Nome, cancelar) : new PassoDeAutoStash(null, null);
        var resultado = await TrocarAsync(repo, opcoes, cancelar);

        var voltar = resultado.Dados?.Trocou == true ? opcoes.Nome : guardado.Branch;
        var restaurado = opcoes.AutoStash && voltar is not null ? await autoStash.RestaurarAsync(repo, voltar, cancelar) : new PassoDeAutoStash(null, null);
        return ComNotas(resultado, guardado, restaurado);
    }

    protected override async Task<ConclusaoDoLote<ResultadoDeCheckout>> ConcluirAsync(
        OpcoesDeCheckout opcoes, IReadOnlyList<ItemDoLote<ResultadoDeCheckout>> itens, bool interrompido, CancellationToken cancelar)
    {
        if (interrompido || !frameworks.Age(ProjetosMarcados(opcoes), opcoes.Nome)) return new();

        var trocados = itens.Where(i => i.Resultado.Dados?.Trocou == true).Select(i => i.Projeto);
        var beta = await frameworks.AplicarAsync(trocados, new PedidoDeBeta(opcoes.Nome), cancelar);
        return new()
        {
            Itens = [.. beta.Ajustes.Select(a => new ItemDoLote<ResultadoDeCheckout>(a.Projeto, new(a.Situacao, "[beta] " + a.Mensagem, new(false))))],
            Observacoes = beta.Observacoes,
        };
    }

    IReadOnlyList<Projeto> ProjetosMarcados(OpcoesDeCheckout opcoes) => base.ProjetosAlvo(opcoes);

    async Task<ResultadoDoProjeto<ResultadoDeCheckout>> TrocarAsync(string repo, OpcoesDeCheckout opcoes, CancellationToken cancelar)
    {
        var fezFetch = false;
        while (true)
        {
            var alvo = await ResolverAsync(repo, opcoes, cancelar);
            if (alvo is null && fezFetch)
                return ResultadoDoProjeto<ResultadoDeCheckout>.Falha($"\"{opcoes.Nome}\" não existe (branch local, origin ou tag)", null, null) with { Dados = new(false) };

            if (alvo is not null)
            {
                var troca = await git.ExecutarAsync(ComandoDeTroca(repo, opcoes.Nome, alvo), cancelar);
                if (troca.EhOk) return Trocou(opcoes.Nome, alvo);
                if (fezFetch) return ResultadoDoProjeto<ResultadoDeCheckout>.Falha(new Erro("Checkout falhou", troca.Erro.ToString(), "Verifique alterações locais."), new(false));
            }

            await consultas.AtualizarRefsAsync(repo, cancelar);
            fezFetch = true;
        }
    }

    async Task<RefResolvida?> ResolverAsync(string repo, OpcoesDeCheckout opcoes, CancellationToken cancelar)
    {
        if (!opcoes.SoOrigin) return await consultas.ResolverCheckoutAsync(repo, opcoes.Nome, cancelar);
        if (!await consultas.RefExisteAsync(repo, "refs/remotes/origin/" + opcoes.Nome, cancelar)) return null;

        // já existe a local: troca para ela
        return await consultas.RefExisteAsync(repo, "refs/heads/" + opcoes.Nome, cancelar)
            ? new(opcoes.Nome, TipoDeRef.BranchLocal)
            : new("origin/" + opcoes.Nome, TipoDeRef.BranchEmOrigin);
    }

    static GitArgs ComandoDeTroca(string repo, string nome, RefResolvida alvo) => alvo.Tipo switch
    {
        TipoDeRef.Tag => Git.Em(repo).Comando("checkout").Argumento(alvo.Ref),
        TipoDeRef.BranchLocal => Git.Em(repo).Comando("switch").Argumento(nome),
        _ => Git.Em(repo).Comando("switch").Opcao("-c").Argumento(nome).Opcao("--track").Argumento(alvo.Ref),
    };

    static ResultadoDoProjeto<ResultadoDeCheckout> Trocou(string nome, RefResolvida alvo) =>
        alvo.Tipo == TipoDeRef.Tag
            ? ResultadoDoProjeto<ResultadoDeCheckout>.Aviso($"tag \"{nome}\" - detached HEAD", new(true, alvo.Tipo))
            : ResultadoDoProjeto<ResultadoDeCheckout>.Ok(new(true, alvo.Tipo), $"{nome} ({alvo.Descricao})");

    static ResultadoDoProjeto<ResultadoDeCheckout> ComNotas(ResultadoDoProjeto<ResultadoDeCheckout> resultado, PassoDeAutoStash guardado, PassoDeAutoStash restaurado)
    {
        string[] notas = [.. new[] { guardado.Nota, restaurado.Nota }.OfType<string>()];
        if (notas.Length == 0) return resultado;

        var falhou = guardado.Falhou || restaurado.Falhou;
        return resultado with
        {
            Mensagem = string.Join("; ", [resultado.Mensagem, .. notas]),
            Situacao = falhou && resultado.Situacao == Situacao.Ok ? Situacao.Aviso : resultado.Situacao,
        };
    }
}
