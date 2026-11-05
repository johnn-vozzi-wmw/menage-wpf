using Menage.Core.Operacoes.Decoradores;

namespace Menage.Core.Operacoes.Ferramentas;

/// <param name="Remotes">Nomes dos remotes (origin, upstream, backup...).</param>
/// <param name="Tags">Envia também as tags (<c>push --tags</c>).</param>
/// <param name="Forcar"><c>--force-with-lease</c>, com confirmação.</param>
public sealed record OpcoesDePushMulti(IReadOnlyList<string> Remotes, bool Tags = false, bool Forcar = false);

public sealed record ResultadoDePushMulti(IReadOnlyList<string> Enviados);

/// <summary>Push da branch atual para vários remotes. Remote inexistente ou com falha vira erro só daquele remote naquele projeto.</summary>
public sealed class PushMultiRemotoEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas, IRegrasDeProtecao regras)
    : OperacaoEmLote<OpcoesDePushMulti, ResultadoDePushMulti>(projetos, dialogos, progresso)
{
    public override string Id => "ferramentas.push-multi";

    public override string Nome => "Push em vários remotos";

    protected override Erro? Validar(OpcoesDePushMulti opcoes, IReadOnlyList<Projeto> alvo) =>
        opcoes.Remotes.Count == 0 ? new Erro("Informe ao menos um remote") : base.Validar(opcoes, alvo);

    protected override Task<Previa?> MontarPreviaAsync(OpcoesDePushMulti opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
        Task.FromResult<Previa?>(opcoes.Forcar
            ? new Previa("Push forçado em vários remotos", $"Enviar com --force-with-lease para: {string.Join(", ", opcoes.Remotes)}?", [.. alvo.Select(p => p.Nome)])
            : null);

    protected override async Task<ResultadoDoProjeto<ResultadoDePushMulti>> ExecutarEmProjeto(Projeto projeto, OpcoesDePushMulti opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var branch = await consultas.BranchAtualAsync(repo, cancelar);
        if (branch.Length == 0) return ResultadoDoProjeto<ResultadoDePushMulti>.Aviso("detached HEAD - ignorado");
        if (regras.ProtegidaParaPush(branch)) return ResultadoDoProjeto<ResultadoDePushMulti>.Aviso($"BLOQUEADO: branch protegida ({branch})");

        var existentes = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("remote"), cancelar)).Linhas().Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);
        var (ok, falhas) = (new List<string>(), new List<string>());
        foreach (var remote in opcoes.Remotes)
        {
            if (!existentes.Contains(remote)) { falhas.Add($"{remote}: remote não existe neste projeto"); continue; }

            var push = await git.ExecutarAsync(Montar(repo, remote, branch, opcoes), cancelar);
            if (push.EhOk) ok.Add(remote); else falhas.Add($"{remote}: {push.Erro.Causa}");
        }

        var dados = new ResultadoDePushMulti(ok);
        var texto = (ok.Count > 0 ? $"{branch} enviada para {string.Join(", ", ok)}" : $"{branch} não foi enviada") + (falhas.Count > 0 ? " | falhou: " + string.Join("; ", falhas) : "");
        return falhas.Count == 0 ? ResultadoDoProjeto<ResultadoDePushMulti>.Ok(dados, texto) : ResultadoDoProjeto<ResultadoDePushMulti>.Falha(texto) with { Dados = dados };
    }

    static GitArgs Montar(string repo, string remote, string branch, OpcoesDePushMulti opcoes)
    {
        var args = Git.Em(repo).Comando("push");
        if (opcoes.Forcar) args = args.Opcao("--force-with-lease");
        args = args.Argumento(remote).Argumento(branch);
        return (opcoes.Tags ? args.Opcao("--tags") : args).ComTimeout(ConsultasGit.TempoLongo);
    }
}
