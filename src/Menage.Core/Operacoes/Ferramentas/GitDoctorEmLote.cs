namespace Menage.Core.Operacoes.Ferramentas;

/// <param name="ReverterVersionados"><c>reset --hard</c>: descarta alterações em arquivos versionados.</param>
/// <param name="LimparNaoVersionados"><c>clean -fd</c>: apaga arquivos e pastas novos (ignorados pelo .gitignore ficam).</param>
/// <param name="StashDeSeguranca">Guarda tudo num stash recuperável antes de descartar.</param>
public sealed record OpcoesDoGitDoctor(bool ReverterVersionados = true, bool LimparNaoVersionados = false, bool StashDeSeguranca = true);

public sealed record ResultadoDoGitDoctor(bool Descartou, string? Stash);

/// <summary>Descarta alterações locais (reset --hard e/ou clean -fd). Por padrão guarda tudo antes num stash; sem ele, só com confirmação.</summary>
public sealed class GitDoctorEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas)
    : OperacaoEmLote<OpcoesDoGitDoctor, ResultadoDoGitDoctor>(projetos, dialogos, progresso)
{
    public override string Id => "ferramentas.git-doctor";

    public override string Nome => "Git Doctor";

    protected override Erro? Validar(OpcoesDoGitDoctor opcoes, IReadOnlyList<Projeto> alvo) =>
        !opcoes.ReverterVersionados && !opcoes.LimparNaoVersionados ? new Erro("Escolha o que descartar") : base.Validar(opcoes, alvo);

    protected override Task<Previa?> MontarPreviaAsync(OpcoesDoGitDoctor opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar)
    {
        var acoes = string.Join(" e ", new[] { opcoes.ReverterVersionados ? "descartar as alterações dos arquivos versionados" : "", opcoes.LimparNaoVersionados ? "apagar os arquivos novos" : "" }.Where(a => a.Length > 0));
        var seguranca = opcoes.StashDeSeguranca ? "Antes, tudo é guardado num stash (recuperável com Stash pop)." : "SEM stash de segurança: o que for descartado NÃO volta.";
        return Task.FromResult<Previa?>(new Previa("Git Doctor", $"Vai {acoes}. {seguranca}", [.. alvo.Select(p => p.Nome)]));
    }

    protected override async Task<ResultadoDoProjeto<ResultadoDoGitDoctor>> ExecutarEmProjeto(Projeto projeto, OpcoesDoGitDoctor opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var pendencias = await consultas.PendenciasAsync(repo, cancelar);
        var versionados = pendencias.Any(l => !l.StartsWith("??", StringComparison.Ordinal));
        var novos = pendencias.Any(l => l.StartsWith("??", StringComparison.Ordinal));
        if (!(opcoes.ReverterVersionados && versionados) && !(opcoes.LimparNaoVersionados && novos))
            return ResultadoDoProjeto<ResultadoDoGitDoctor>.Aviso("nada para descartar");

        string? stash = null;
        if (opcoes.StashDeSeguranca)
        {
            var guardado = await GuardarAsync(repo, opcoes.LimparNaoVersionados, cancelar);
            if (!guardado.EhOk) return ResultadoDoProjeto<ResultadoDoGitDoctor>.Falha(guardado.Erro);
            stash = guardado.Valor;
        }

        if (opcoes.ReverterVersionados)
        {
            var reset = await git.ExecutarAsync(Git.Em(repo).Comando("reset").Opcao("--hard"), cancelar);
            if (!reset.EhOk) return ResultadoDoProjeto<ResultadoDoGitDoctor>.Falha(new Erro("o reset --hard falhou", reset.Erro.ToString()));
        }

        if (opcoes.LimparNaoVersionados)
        {
            var limpar = await git.ExecutarAsync(Git.Em(repo).Comando("clean").Opcao("-fd"), cancelar);
            if (!limpar.EhOk) return ResultadoDoProjeto<ResultadoDoGitDoctor>.Falha(new Erro("o clean -fd falhou", limpar.Erro.ToString()));
        }

        return ResultadoDoProjeto<ResultadoDoGitDoctor>.Ok(new(true, stash), "alterações descartadas" + (stash is null ? " (sem stash)" : $" | guardadas no stash \"{stash}\""));
    }

    // Só versionados: "stash create + store" guarda sem mexer na pasta. Com novos: stash push -u (a pasta fica limpa).
    async Task<Resultado<string?>> GuardarAsync(string repo, bool comNovos, CancellationToken cancelar)
    {
        var mensagem = $"Menage Git Doctor {DateTime.Now:yyyy-MM-dd HH:mm}";
        if (comNovos)
        {
            var guardar = await git.ExecutarAsync(Git.Em(repo).Comando("stash").Argumento("push").Opcao("--include-untracked").Opcao("-m", mensagem), cancelar);
            return guardar.EhOk ? Resultado.Ok<string?>(mensagem) : Resultado.Falha<string?>(new Erro("não foi possível guardar o stash de segurança", guardar.Erro.ToString()));
        }

        var criado = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("stash").Argumento("create"), cancelar)).PrimeiraLinha().Trim();
        if (criado.Length == 0) return Resultado.Ok<string?>(null);

        var guardado = await git.ExecutarAsync(Git.Em(repo).Comando("stash").Argumento("store").Opcao("-m", mensagem).Argumento(criado), cancelar);
        return guardado.EhOk ? Resultado.Ok<string?>(mensagem) : Resultado.Falha<string?>(new Erro("não foi possível guardar o stash de segurança", guardado.Erro.ToString()));
    }
}
