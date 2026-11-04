using Menage.Core.ModoBeta;

namespace Menage.Core.Operacoes.CriarBranch;

/// <param name="Base">Vazia cria a partir do commit atual; senão origin, depois local, depois tag.</param>
/// <param name="Publicar">Faz push da branch criada.</param>
public sealed record OpcoesDeCriarBranch(string Nome, string Base = "", bool Publicar = false);

/// <param name="Criada">Falso nos itens do modo beta e quando não foi possível criar.</param>
public sealed record ResultadoDeCriarBranch(bool Criada, bool Publicada = false);

/// <summary>A branch nova nunca acompanha a base: <c>--no-track</c>, e o push -u liga ao origin dela mesma.</summary>
public sealed class CriarBranchEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas, AjustadorDeFrameworks frameworks)
    : OperacaoEmLote<OpcoesDeCriarBranch, ResultadoDeCriarBranch>(projetos, dialogos, progresso)
{
    public override string Id => "git.criar-branch";

    public override string Nome => "Nova branch";

    public override IReadOnlyList<Projeto> ProjetosAlvo(OpcoesDeCriarBranch opcoes) =>
        frameworks.SemFrameworks(base.ProjetosAlvo(opcoes), null);

    protected override async Task<Erro?> ValidarAsync(OpcoesDeCriarBranch opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
        await consultas.NomeDeBranchValidoAsync(opcoes.Nome, cancelar)
            ? Validar(opcoes, alvo)
            : new Erro($"Nome de branch inválido: \"{opcoes.Nome}\"");

    protected override async Task<ResultadoDoProjeto<ResultadoDeCriarBranch>> ExecutarEmProjeto(Projeto projeto, OpcoesDeCriarBranch opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        await consultas.AtualizarRefsAsync(repo, cancelar);
        if (await consultas.BranchExisteAsync(repo, opcoes.Nome, cancelar))
            return ResultadoDoProjeto<ResultadoDeCriarBranch>.Falha($"a branch \"{opcoes.Nome}\" já existe", null, "Use o checkout.");

        var pedida = opcoes.Base.Trim();
        var referencia = pedida.Length == 0 ? "HEAD" : (await consultas.ResolverBaseAsync(repo, pedida, cancelar))?.Ref;
        if (referencia is null) return ResultadoDoProjeto<ResultadoDeCriarBranch>.Falha($"base \"{pedida}\" não existe");

        var criar = await git.ExecutarAsync(Git.Em(repo).Comando("checkout").Opcao("--no-track").Opcao("-b").Argumento(opcoes.Nome).Argumento(referencia), cancelar);
        if (!criar.EhOk)
            return ResultadoDoProjeto<ResultadoDeCriarBranch>.Falha(new Erro("Não foi possível criar a branch", criar.Erro.ToString(), "Verifique alterações locais."));

        return opcoes.Publicar
            ? await PublicarAsync(repo, opcoes.Nome, cancelar)
            : ResultadoDoProjeto<ResultadoDeCriarBranch>.Ok(new(true), $"{opcoes.Nome} criada");
    }

    protected override async Task<ConclusaoDoLote<ResultadoDeCriarBranch>> ConcluirAsync(
        OpcoesDeCriarBranch opcoes, IReadOnlyList<ItemDoLote<ResultadoDeCriarBranch>> itens, bool interrompido, CancellationToken cancelar)
    {
        if (interrompido || !frameworks.Age(base.ProjetosAlvo(opcoes), null)) return new();

        var criados = itens.Where(i => i.Resultado.Dados?.Criada == true).Select(i => i.Projeto);
        var beta = await frameworks.AplicarAsync(criados, new PedidoDeBeta(opcoes.Nome, Criar: true, opcoes.Publicar), cancelar);
        return new()
        {
            Itens = [.. beta.Ajustes.Select(a => new ItemDoLote<ResultadoDeCriarBranch>(a.Projeto, new(a.Situacao, "[beta] " + a.Mensagem, new(false))))],
            Observacoes = beta.Observacoes,
        };
    }

    async Task<ResultadoDoProjeto<ResultadoDeCriarBranch>> PublicarAsync(string repo, string nome, CancellationToken cancelar)
    {
        var push = await git.ExecutarAsync(Git.Em(repo).Comando("push").Opcao("-u").Argumento("origin").Argumento(nome).ComTimeout(ConsultasGit.TempoLongo), cancelar);
        return push.EhOk
            ? ResultadoDoProjeto<ResultadoDeCriarBranch>.Ok(new(true, true), $"{nome} criada e publicada")
            : ResultadoDoProjeto<ResultadoDeCriarBranch>.Aviso($"{nome} criada, mas o push falhou: {push.Erro}", new(true));
    }
}
