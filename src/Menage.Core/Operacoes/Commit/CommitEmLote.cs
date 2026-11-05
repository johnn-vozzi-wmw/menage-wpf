using System.Text;
using Menage.Core.Estado;
using Menage.Core.Operacoes.Decoradores;
using Menage.Core.Operacoes.Push;

namespace Menage.Core.Operacoes.Commit;

/// <param name="IncluirNovos">Sem a opção só entram arquivos versionados (<c>add -u</c>); com ela, também os novos (<c>add -A</c>).</param>
/// <param name="Push">Envia ao origin logo após cada commit (publica a branch se for nova).</param>
public sealed record OpcoesDeCommit(string Mensagem, bool IncluirNovos = false, bool Push = false) : IComMensagemDeCommit
{
    public string MensagemDeCommit => Mensagem;
}

public sealed record ResultadoDeCommit(string Hash, int Arquivos, bool Enviado);

/// <summary>Projeto na prévia do commit. <see cref="Ignorado"/> = detached, operação em andamento ou erro de leitura.</summary>
public sealed record PreviaDeCommit(Projeto Projeto, string Branch, int Versionados, int NaoVersionados, bool Ignorado, string Situacao, IReadOnlyList<string> Arquivos)
{
    public bool SemAlteracoes => Versionados == 0 && NaoVersionados == 0;
}

/// <summary>Mesma mensagem em vários projetos. A convenção de commit é conferida pelo decorador antes de chegar aqui.</summary>
public sealed class CommitEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, LeitorDeEstado estados, EnvioParaOrigin envio)
    : OperacaoEmLote<OpcoesDeCommit, ResultadoDeCommit>(projetos, dialogos, progresso)
{
    public override string Id => "git.commit";

    public override string Nome => "Commit";

    public async Task<IReadOnlyList<PreviaDeCommit>> ListarAsync(IEnumerable<Projeto> alvo, CancellationToken cancelar = default)
    {
        var lista = new List<PreviaDeCommit>();
        foreach (var projeto in alvo)
        {
            var e = await estados.LerAsync(projeto, cancelar);
            var motivo = MotivoParaIgnorar(e);
            var arquivos = motivo is null && e.Alteracoes + e.NaoVersionados > 0
                ? (await git.ExecutarBrutoAsync(Git.Em(projeto.Caminho).Comando("status").Opcao("--porcelain"), cancelar)).Linhas()
                : [];
            lista.Add(new(projeto, e.Detached ? "" : e.Branch, e.Alteracoes, e.NaoVersionados, motivo is not null, motivo ?? "", arquivos));
        }

        return lista;
    }

    protected override Erro? Validar(OpcoesDeCommit opcoes, IReadOnlyList<Projeto> alvo) =>
        string.IsNullOrWhiteSpace(opcoes.Mensagem) ? new Erro("Informe a mensagem do commit") : base.Validar(opcoes, alvo);

    protected override async Task<ResultadoDoProjeto<ResultadoDeCommit>> ExecutarEmProjeto(Projeto projeto, OpcoesDeCommit opcoes, CancellationToken cancelar)
    {
        var e = await estados.LerAsync(projeto, cancelar);
        if (MotivoParaIgnorar(e) is { } motivo) return ResultadoDoProjeto<ResultadoDeCommit>.Aviso($"ignorado - {motivo}");

        var arquivos = e.Alteracoes + (opcoes.IncluirNovos ? e.NaoVersionados : 0);
        if (arquivos == 0)
            return ResultadoDoProjeto<ResultadoDeCommit>.Aviso("sem alterações para commitar" +
                (e.NaoVersionados > 0 ? $" (há {e.NaoVersionados} arquivo(s) não versionado(s): marque a opção para incluí-los)" : ""));

        var preparo = await git.ExecutarAsync(Git.Em(projeto.Caminho).Comando("add").Opcao(opcoes.IncluirNovos ? "-A" : "-u"), cancelar);
        if (!preparo.EhOk) return ResultadoDoProjeto<ResultadoDeCommit>.Falha(new Erro("Não foi possível preparar os arquivos (git add)", preparo.Erro.ToString()));

        var falha = await CommitarAsync(projeto.Caminho, opcoes.Mensagem, cancelar);
        if (falha is not null) return ResultadoDoProjeto<ResultadoDeCommit>.Falha($"commit falhou - {falha}", null, "Hook ou configuração do git? Veja a Saída.");

        var hash = (await git.ExecutarBrutoAsync(Git.Em(projeto.Caminho).Comando("rev-parse").Opcao("--short").Argumento("HEAD"), cancelar)).PrimeiraLinha().Trim();
        var texto = $"commit {hash} em {e.Branch} ({arquivos} arquivo(s))";
        return opcoes.Push
            ? await EnviarAsync(projeto, e.Branch, hash, arquivos, texto, cancelar)
            : ResultadoDoProjeto<ResultadoDeCommit>.Ok(new(hash, arquivos, false), texto);
    }

    protected override Task<ConclusaoDoLote<ResultadoDeCommit>> ConcluirAsync(
        OpcoesDeCommit opcoes, IReadOnlyList<ItemDoLote<ResultadoDeCommit>> itens, bool interrompido, CancellationToken cancelar) =>
        Task.FromResult(new ConclusaoDoLote<ResultadoDeCommit>
        {
            Observacoes = opcoes.Push ? [] : [new("Os commits ficaram só neste computador. Use o botão Push para publicar.")],
        });

    async Task<ResultadoDoProjeto<ResultadoDeCommit>> EnviarAsync(Projeto projeto, string branch, string hash, int arquivos, string texto, CancellationToken cancelar)
    {
        var enviado = await envio.EnviarAsync(projeto.Caminho, branch, forcar: false, publicarNova: true, cancelar);
        return enviado.EhOk
            ? ResultadoDoProjeto<ResultadoDeCommit>.Ok(new(hash, arquivos, true), $"{texto}  |  push: {enviado.Valor}")
            : ResultadoDoProjeto<ResultadoDeCommit>.Falha($"{texto}  |  push falhou: {enviado.Erro.Causa}");
    }

    // Mensagem num arquivo temporário (várias linhas, sem problema de aspas); hook que recusa devolve o motivo.
    async Task<string?> CommitarAsync(string repo, string mensagem, CancellationToken cancelar)
    {
        var arquivo = Path.Combine(Path.GetTempPath(), "menage-commit-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(arquivo, mensagem.Trim() + "\n", new UTF8Encoding(false), cancelar);
        try
        {
            var saida = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("commit").Opcao("-F").Argumento(arquivo), cancelar);
            if (saida.Sucesso) return null;
            var texto = saida.TextoErro.Trim().Length > 0 ? saida.TextoErro : saida.Texto;
            return texto.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "o git recusou o commit";
        }
        finally
        {
            try { File.Delete(arquivo); } catch (IOException) { }
        }
    }

    static string? MotivoParaIgnorar(EstadoDoProjeto e) =>
        e.Falha.Length > 0 ? e.Falha
        : e.Operacao.Length > 0 ? $"{e.Operacao} - conclua ou aborte antes de commitar"
        : e.Detached ? "detached HEAD - faça checkout de uma branch"
        : null;
}
