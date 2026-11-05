namespace Menage.Core.Estado;

/// <summary>Rebase, cherry-pick ou merge pela metade, descobertos pelos arquivos da pasta .git (zero processos).</summary>
public static class OperacaoEmAndamento
{
    public static string Detectar(string repo)
    {
        var git = PastaGit(repo);
        if (git is null) return "";
        if (Directory.Exists(Path.Combine(git, "rebase-merge")) || Directory.Exists(Path.Combine(git, "rebase-apply"))) return "rebase em andamento";
        if (File.Exists(Path.Combine(git, "CHERRY_PICK_HEAD"))) return "cherry-pick em andamento";
        return File.Exists(Path.Combine(git, "MERGE_HEAD")) ? "merge em andamento" : "";
    }

    /// <summary>A pasta .git; em worktree e submódulo, <c>.git</c> é um arquivo "gitdir: caminho".</summary>
    static string? PastaGit(string repo)
    {
        var marcador = Path.Combine(repo, ".git");
        if (Directory.Exists(marcador)) return marcador;
        if (!File.Exists(marcador)) return null;
        try
        {
            var linha = File.ReadLines(marcador).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal));
            return linha is null ? null : Path.GetFullPath(linha["gitdir:".Length..].Trim(), repo);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
