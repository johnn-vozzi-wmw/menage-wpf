namespace Menage.Core.Operacoes.DesfazerCommit;

/// <param name="TirarDaStaging"><c>--mixed</c>: as alterações ficam só na área de trabalho; senão <c>--soft</c> (continuam na staging).</param>
/// <param name="EnviadosPermitidos">Caminhos dos projetos cujo commit já está no origin e que o usuário aceitou desfazer.</param>
/// <param name="Esperados">Hash visto na prévia, por caminho: se o HEAD mudou depois, nada é desfeito naquele projeto.</param>
public sealed record OpcoesDeDesfazer(
    bool TirarDaStaging = false, IReadOnlySet<string>? EnviadosPermitidos = null, IReadOnlyDictionary<string, string>? Esperados = null);

public sealed record ResultadoDeDesfazer(string Curto, string Assunto);

/// <summary>Último commit de um projeto. <see cref="Erro"/> preenchido = ignorado (detached, sem commit anterior, operação em andamento...).</summary>
public sealed record PreviaDeDesfazer(
    Projeto Projeto, string Branch, string Sha, string Curto, string Autor, string Data, string Assunto, bool Merge, bool Enviado, string Erro = "", string Aviso = "");

/// <summary>
/// <c>reset --soft HEAD~1</c> (ou <c>--mixed</c>) em vários projetos. Ignora primeiro commit, detached e rebase/merge em
/// andamento; commit que já está no origin só é desfeito se o usuário marcou (o próximo push exigirá force).
/// </summary>
public sealed class DesfazerCommitEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas)
    : OperacaoEmLote<OpcoesDeDesfazer, ResultadoDeDesfazer>(projetos, dialogos, progresso)
{
    public override string Id => "git.desfazer-commit";

    public override string Nome => "Desfazer último commit";

    public async Task<IReadOnlyList<PreviaDeDesfazer>> ListarAsync(IEnumerable<Projeto> alvo, CancellationToken cancelar = default)
    {
        var lista = new List<PreviaDeDesfazer>();
        foreach (var projeto in alvo) lista.Add(await AvaliarAsync(projeto, cancelar));
        return lista;
    }

    protected override async Task<ResultadoDoProjeto<ResultadoDeDesfazer>> ExecutarEmProjeto(Projeto projeto, OpcoesDeDesfazer opcoes, CancellationToken cancelar)
    {
        var previa = await AvaliarAsync(projeto, cancelar);
        if (previa.Erro.Length > 0) return ResultadoDoProjeto<ResultadoDeDesfazer>.Aviso(previa.Erro);
        if (opcoes.Esperados?.TryGetValue(projeto.Caminho, out var esperado) == true && esperado != previa.Sha)
            return ResultadoDoProjeto<ResultadoDeDesfazer>.Falha("o HEAD mudou depois da prévia; nada foi desfeito");
        if (previa.Enviado && opcoes.EnviadosPermitidos?.Contains(projeto.Caminho) != true)
            return ResultadoDoProjeto<ResultadoDeDesfazer>.Aviso($"{previa.Curto} já está no origin - não desfeito (marque para permitir; o próximo push exigirá force)");

        var reset = await git.ExecutarAsync(Git.Em(projeto.Caminho).Comando("reset").Opcao(opcoes.TirarDaStaging ? "--mixed" : "--soft").Argumento("HEAD~1"), cancelar);
        if (!reset.EhOk) return ResultadoDoProjeto<ResultadoDeDesfazer>.Falha(new Erro("o reset falhou", reset.Erro.ToString()));

        var onde = opcoes.TirarDaStaging ? "na área de trabalho" : "na staging (prontas para novo commit)";
        var texto = $"desfeito {previa.Curto} \"{previa.Assunto}\" - alterações mantidas {onde} | para refazer: git reset --soft {previa.Curto}";
        return previa.Enviado
            ? ResultadoDoProjeto<ResultadoDeDesfazer>.Aviso(texto + " | o commit continua no origin: o próximo push precisará de --force-with-lease", new(previa.Curto, previa.Assunto))
            : ResultadoDoProjeto<ResultadoDeDesfazer>.Ok(new(previa.Curto, previa.Assunto), texto);
    }

    async Task<PreviaDeDesfazer> AvaliarAsync(Projeto projeto, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        PreviaDeDesfazer Ignorada(string branch, string motivo) => new(projeto, branch, "", "", "", "", "", false, false, motivo);
        if (!ConsultasGit.EhRepositorio(repo)) return Ignorada("", "pasta não encontrada");

        var branch = await consultas.BranchAtualAsync(repo, cancelar);
        if (branch.Length == 0) return Ignorada("", "detached HEAD: faça checkout de uma branch");
        if (await consultas.OperacaoEmAndamentoAsync(repo, cancelar) is { Length: > 0 } andamento) return Ignorada(branch, $"{andamento} - conclua ou aborte antes");

        var log = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("log").Opcao("-1").Opcao("--format=%H%x09%h%x09%an%x09%ad%x09%s").Opcao("--date=format:%d/%m/%Y %H:%M"), cancelar);
        var p = log.PrimeiraLinha().Split('\t');
        if (!log.Sucesso || p.Length < 5) return Ignorada(branch, "não há commits nesta branch");

        var pais = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-list").Opcao("--parents").Opcao("-n", "1").Argumento("HEAD"), cancelar))
            .PrimeiraLinha().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length - 1;
        if (pais <= 0) return Ignorada(branch, "é o primeiro commit do repositório (não há commit anterior)");

        var enviado = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("branch").Opcao("-r").Opcao("--contains").Argumento("HEAD"), cancelar)).Linhas().Any(l => !l.Contains("->", StringComparison.Ordinal));
        var avisos = new List<string>();
        if (enviado) avisos.Add("já está no origin: depois de desfazer, o push exigirá force");
        if (pais > 1) avisos.Add("commit de merge: volta para o 1º pai");
        return new(projeto, branch, p[0], p[1], p[2], p[3], p[4], pais > 1, enviado, "", string.Join("; ", avisos));
    }
}
