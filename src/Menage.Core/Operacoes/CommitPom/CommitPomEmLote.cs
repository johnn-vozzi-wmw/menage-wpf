using Menage.Core.Operacoes.Push;

namespace Menage.Core.Operacoes.CommitPom;

public sealed record OpcoesDeCommitPom(string Mensagem = "pom.xml", bool Push = false);

public sealed record ResultadoDeCommitPom(string Hash, bool Enviado);

/// <summary>Projeto com pom.xml alterado, para a prévia (arquivos e estatística do diff).</summary>
public sealed record PreviaDeCommitPom(Projeto Projeto, string Branch, IReadOnlyList<string> Arquivos, string Estatistica);

/// <summary>Commita só o(s) pom.xml alterado(s) (<c>commit -- *pom.xml</c>), sem levar mais nada que esteja em staging; push opcional.</summary>
public sealed class CommitPomEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas, EnvioParaOrigin envio)
    : OperacaoEmLote<OpcoesDeCommitPom, ResultadoDeCommitPom>(projetos, dialogos, progresso)
{
    const string Pom = "*pom.xml";

    public override string Id => "git.commit-pom";

    public override string Nome => "Commit pom.xml";

    /// <summary>Só os projetos que têm pom.xml alterado.</summary>
    public async Task<IReadOnlyList<PreviaDeCommitPom>> ListarAsync(IEnumerable<Projeto> alvo, CancellationToken cancelar = default)
    {
        var lista = new List<PreviaDeCommitPom>();
        foreach (var projeto in alvo)
        {
            var repo = projeto.Caminho;
            var arquivos = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("status").Opcao("--short").Caminhos([Pom]), cancelar)).Linhas();
            if (arquivos.Count == 0) continue;

            var estatistica = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("diff").Opcao("--stat").Argumento("HEAD").Caminhos([Pom]), cancelar)).Texto.TrimEnd();
            lista.Add(new(projeto, await consultas.BranchAtualAsync(repo, cancelar), arquivos, estatistica));
        }

        return lista;
    }

    protected override Erro? Validar(OpcoesDeCommitPom opcoes, IReadOnlyList<Projeto> alvo) =>
        string.IsNullOrWhiteSpace(opcoes.Mensagem) ? new Erro("Informe a mensagem do commit") : base.Validar(opcoes, alvo);

    protected override async Task<ResultadoDoProjeto<ResultadoDeCommitPom>> ExecutarEmProjeto(Projeto projeto, OpcoesDeCommitPom opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var branch = await consultas.BranchAtualAsync(repo, cancelar);
        if (branch.Length == 0) return ResultadoDoProjeto<ResultadoDeCommitPom>.Aviso("detached HEAD - ignorado");
        if (await consultas.OperacaoEmAndamentoAsync(repo, cancelar) is { Length: > 0 } andamento) return ResultadoDoProjeto<ResultadoDeCommitPom>.Aviso($"{andamento} - ignorado");
        if ((await git.ExecutarBrutoAsync(Git.Em(repo).Comando("status").Opcao("--short").Caminhos([Pom]), cancelar)).Linhas().Count == 0)
            return ResultadoDoProjeto<ResultadoDeCommitPom>.Aviso("nenhum pom.xml alterado");

        var add = await git.ExecutarAsync(Git.Em(repo).Comando("add").Caminhos([Pom]), cancelar);
        if (!add.EhOk) return ResultadoDoProjeto<ResultadoDeCommitPom>.Falha(new Erro("não foi possível preparar o pom.xml", add.Erro.ToString()));

        var commit = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("commit").Opcao("-m", opcoes.Mensagem.Trim()).Caminhos([Pom]), cancelar);
        if (!commit.Sucesso)
            return ResultadoDoProjeto<ResultadoDeCommitPom>.Falha("commit falhou - " + (commit.TextoErro + commit.Texto).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0), null, "Hook ou configuração do git? Veja a Saída.");

        var hash = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-parse").Opcao("--short").Argumento("HEAD"), cancelar)).PrimeiraLinha().Trim();
        if (!opcoes.Push) return ResultadoDoProjeto<ResultadoDeCommitPom>.Ok(new(hash, false), $"commit {hash} criado (local)");

        var enviado = await envio.EnviarAsync(repo, branch, forcar: false, publicarNova: true, cancelar);
        return enviado.EhOk
            ? ResultadoDoProjeto<ResultadoDeCommitPom>.Ok(new(hash, true), $"commit {hash} criado e publicado")
            : ResultadoDoProjeto<ResultadoDeCommitPom>.Falha($"commit {hash} criado, mas o push falhou - ficou só local: {enviado.Erro.Causa}");
    }
}
