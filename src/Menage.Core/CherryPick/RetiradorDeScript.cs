namespace Menage.Core.CherryPick;

public enum DesfechoDoScript { Nenhum, Concluido, SoScript, Parcial }

/// <param name="Mensagem">O que aconteceu, para o detalhe do item; nula se nada foi feito.</param>
public sealed record ResultadoDoScript(DesfechoDoScript Desfecho, string? Mensagem = null, bool Aviso = false)
{
    public static readonly ResultadoDoScript Nada = new(DesfechoDoScript.Nenhum);
}

/// <summary>
/// Tira o script de banco da atividade do commit aplicado na develop (amend mantendo mensagem e linha <c>-x</c>); commit que só tinha o
/// script não é criado; conflito só no script se resolve sozinho. Só mexe na branch atual: a branch da atividade nunca é tocada.
/// </summary>
public sealed class RetiradorDeScript(IGit git, ConsultasGit consultas, IArmazenamento<ConfigDoScript> armazenamento)
{
    ConfigDoScript Config => armazenamento.Carregar() is { EhOk: true } r ? r.Valor : new ConfigDoScript();

    /// <summary>
    /// O cherry-pick parou em conflito e entre os arquivos está o script. Ele fica de fora; se sobrar conflito em outro arquivo,
    /// segue para o usuário (<see cref="DesfechoDoScript.Parcial"/>).
    /// </summary>
    public async Task<ResultadoDoScript> ResolverConflitoAsync(string repo, string hashOrigem, CancellationToken cancelar = default)
    {
        var config = Config;
        if (!config.ExcluirScript || !await consultas.RefVerificaAsync(repo, "CHERRY_PICK_HEAD", cancelar)) return ResultadoDoScript.Nada;
        var destino = await consultas.BranchAtualAsync(repo, cancelar);
        if (!config.DestinoVale(destino)) return ResultadoDoScript.Nada;

        var emConflito = await ListarAsync(repo, cancelar, "diff", "--name-only", "--diff-filter=U");
        var scripts = emConflito.Where(config.EhScript).ToList();
        if (scripts.Count == 0) return ResultadoDoScript.Nada;

        foreach (var s in scripts)
        {
            // Na develop o arquivo não existe: some; se existe (script de outra atividade com o mesmo nome), fica como a develop tem.
            if (await consultas.RefVerificaAsync(repo, "HEAD:" + s, cancelar))
                await git.ExecutarAsync(Git.Em(repo).Comando("checkout").Argumento("HEAD").Caminhos([s]), cancelar);
            else
                await git.ExecutarAsync(Git.Em(repo).Comando("rm").Opcao("-q").Opcao("-f").Caminhos([s]), cancelar);
        }

        var nomes = string.Join(", ", scripts);
        if (emConflito.Count > scripts.Count)
            return new(DesfechoDoScript.Parcial, $"conflito no script da atividade resolvido ({nomes} fica fora de {destino}); restam conflitos em outros arquivos");

        if ((await git.ExecutarBrutoAsync(Git.Em(repo).Comando("diff").Opcao("--cached").Opcao("--quiet").Argumento("HEAD"), cancelar)).Sucesso)
        {
            await git.ExecutarAsync(Git.Em(repo).Comando("cherry-pick").Opcao("--skip"), cancelar);
            return new(DesfechoDoScript.SoScript, $"o commit só mexia no script da atividade ({nomes}): não foi levado para {destino}");
        }

        var continuar = await git.ExecutarAsync(Git.Em(repo).Config("core.editor", "true").Comando("cherry-pick").Opcao("--continue"), cancelar);
        if (!continuar.EhOk) return new(DesfechoDoScript.Parcial, $"conflito só no script ({nomes}) resolvido, mas não foi possível concluir o cherry-pick", true);

        var extra = await RetirarDoCommitAsync(repo, hashOrigem, cancelar);
        return new(DesfechoDoScript.Concluido, $"script da atividade não levado para {destino}: {nomes}" + (extra is { Aviso: true } ? " - " + extra.Mensagem : ""));
    }

    /// <summary>
    /// Depois de um cherry-pick concluído (o HEAD é o commit aplicado): tira o script do commit se o destino pede. Confere pela linha
    /// <c>-x</c> que o HEAD é mesmo o commit do cherry-pick. Nulo se nada foi feito.
    /// </summary>
    public async Task<ResultadoDoScript?> RetirarDoCommitAsync(string repo, string hashOrigem, CancellationToken cancelar = default)
    {
        var config = Config;
        if (!config.ExcluirScript || string.IsNullOrEmpty(hashOrigem)) return null;
        var destino = await consultas.BranchAtualAsync(repo, cancelar);
        if (!config.DestinoVale(destino)) return null;
        var mensagem = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("log").Opcao("-1").Opcao("--format=%B"), cancelar)).Texto;
        if (!mensagem.Contains(hashOrigem, StringComparison.OrdinalIgnoreCase)) return null;
        if (!await consultas.RefVerificaAsync(repo, "HEAD~1", cancelar)) return null;
        var pais = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-list").Opcao("--parents").Opcao("-n", "1").Argumento("HEAD"), cancelar)).PrimeiraLinha();
        if (pais.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 2) return null;

        var scripts = await ScriptsDoCommitAsync(repo, config, cancelar);
        if (scripts.Count == 0) return null;
        foreach (var (status, caminho) in scripts)
        {
            // Arquivo novo no commit: sai; arquivo que já existia na develop: volta ao que estava.
            var tirar = status == "A"
                ? Git.Em(repo).Comando("rm").Opcao("-q").Opcao("-f").Caminhos([caminho])
                : Git.Em(repo).Comando("checkout").Argumento("HEAD~1").Caminhos([caminho]);
            if (!(await git.ExecutarAsync(tirar, cancelar)).EhOk)
                return new(DesfechoDoScript.Parcial, $"não foi possível tirar o script {caminho} do commit - confira a branch {destino}", true);
        }

        var nomes = string.Join(", ", scripts.Select(s => s.Caminho));
        if ((await git.ExecutarBrutoAsync(Git.Em(repo).Comando("diff").Opcao("--cached").Opcao("--quiet").Argumento("HEAD~1"), cancelar)).Sucesso)
        {
            await git.ExecutarAsync(Git.Em(repo).Comando("reset").Opcao("-q").Opcao("--hard").Argumento("HEAD~1"), cancelar);
            return new(DesfechoDoScript.SoScript, $"o commit só tinha o script da atividade ({nomes}): não foi levado para {destino}");
        }

        var refeito = await git.ExecutarAsync(Git.Em(repo).Config("core.editor", "true").Comando("commit").Opcao("--amend").Opcao("--no-edit").Opcao("-q"), cancelar);
        return refeito.EhOk
            ? new(DesfechoDoScript.Concluido, $"script da atividade não levado para {destino}: {nomes}")
            : new(DesfechoDoScript.Parcial, $"o script ({nomes}) saiu da área de preparação mas o commit não foi refeito - confira a branch {destino}", true);
    }

    async Task<List<(string Status, string Caminho)>> ScriptsDoCommitAsync(string repo, ConfigDoScript config, CancellationToken cancelar)
    {
        var args = Git.Em(repo).Config("core.quotepath", "false").Comando("diff-tree").Opcao("--no-commit-id").Opcao("--name-status").Opcao("-r").Opcao("--no-renames").Argumento("HEAD");
        var scripts = new List<(string, string)>();
        foreach (var linha in (await git.ExecutarBrutoAsync(args, cancelar)).Linhas())
        {
            var tab = linha.IndexOf('\t');
            if (tab >= 1 && linha[(tab + 1)..].Trim() is var caminho && config.EhScript(caminho)) scripts.Add((linha[..tab].Trim(), caminho));
        }

        return scripts;
    }

    async Task<List<string>> ListarAsync(string repo, CancellationToken cancelar, params string[] comando)
    {
        var args = Git.Em(repo).Config("core.quotepath", "false").Comando(comando[0]);
        foreach (var c in comando.Skip(1)) args = args.Opcao(c);
        return [.. (await git.ExecutarBrutoAsync(args, cancelar)).Linhas().Select(l => l.Trim()).Where(l => l.Length > 0)];
    }
}
