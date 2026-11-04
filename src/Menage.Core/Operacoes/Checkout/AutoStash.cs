namespace Menage.Core.Operacoes.Checkout;

/// <param name="Branch">Branch cujas pendências foram guardadas; nula se nada foi guardado.</param>
/// <param name="Nota">O que dizer no resumo; nula se não há o que dizer.</param>
/// <param name="Falhou">Vira aviso no resumo.</param>
public sealed record PassoDeAutoStash(string? Branch, string? Nota, bool Falhou = false);

/// <summary>
/// Ao sair de uma branch com pendências, guarda tudo (inclusive arquivos novos) num stash marcado
/// <c>GM-AUTO|branch|data</c>; ao voltar para ela, restaura. Em conflito o stash fica guardado.
/// </summary>
public sealed class AutoStash(IGit git, ConsultasGit consultas)
{
    const string Marca = "GM-AUTO";

    public async Task<PassoDeAutoStash> GuardarAsync(string repo, string destino, CancellationToken cancelar = default)
    {
        var atual = await consultas.BranchAtualAsync(repo, cancelar);
        if (atual.Length == 0 || atual == destino) return new(null, null);
        if ((await consultas.OperacaoEmAndamentoAsync(repo, cancelar)).Length > 0) return new(null, null);
        if (!(await consultas.PendenciasAsync(repo, cancelar)).Any(l => !l.StartsWith("!!", StringComparison.Ordinal))) return new(null, null);

        var mensagem = $"{Marca}|{atual}|{DateTime.Now:yyyyMMdd-HHmmss}";
        var push = await git.ExecutarAsync(Git.Em(repo).Comando("stash").Argumento("push").Opcao("-u").Opcao("-m", mensagem), cancelar);
        return push.EhOk
            ? new(atual, $"pendências de \"{atual}\" guardadas (auto-stash); voltam no checkout de \"{atual}\"")
            : new(null, "auto-stash falhou; o checkout segue sem guardar as pendências", Falhou: true);
    }

    public async Task<PassoDeAutoStash> RestaurarAsync(string repo, string branch, CancellationToken cancelar = default)
    {
        var referencia = await AcharAsync(repo, branch, cancelar);
        if (referencia is null) return new(null, null);

        var pop = await git.ExecutarAsync(Git.Em(repo).Comando("stash").Argumento("pop").Argumento(referencia), cancelar);
        return pop.EhOk
            ? new(null, $"pendências guardadas de \"{branch}\" foram restauradas")
            : new(null, $"auto-stash de \"{branch}\" NÃO restaurado (conflito); continua em git stash list", Falhou: true);
    }

    // O primeiro da lista é o mais recente.
    async Task<string?> AcharAsync(string repo, string branch, CancellationToken cancelar)
    {
        var lista = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("stash").Argumento("list").Opcao("--format=%gd%x09%gs"), cancelar);
        foreach (var linha in lista.Linhas())
        {
            var partes = linha.Split('\t');
            if (partes.Length < 2) continue;
            var inicio = partes[1].IndexOf(Marca + "|", StringComparison.Ordinal);
            if (inicio < 0) continue;
            var marca = partes[1][inicio..].Split('|');
            if (marca.Length >= 2 && marca[1] == branch) return partes[0];
        }

        return null;
    }
}
