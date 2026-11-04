namespace Menage.Core.Diff;

/// <summary>
/// Quem é quem num conflito. No git "ours" é o lado atual e "theirs" o que está entrando, mas no rebase isso se
/// inverte (ours = a nova base, theirs = o seu commit sendo reaplicado): cada lado ganha o nome que faz sentido.
/// </summary>
public sealed record LadosConflito
{
    public string Operacao { get; init; } = "conflito";

    public string TituloEntrando { get; init; } = "Commit";

    public string DetalheEntrando { get; init; } = "";

    public string UsarEntrando { get; init; } = "Usar do commit";

    public string CurtoEntrando { get; init; } = "commit";

    public string TituloAtual { get; init; } = "Branch";

    public string DetalheAtual { get; init; } = "";

    public string UsarAtual { get; init; } = "Usar da branch";

    public string CurtoAtual { get; init; } = "branch";
}

public sealed class LeitorDeLados(IGit git)
{
    public async Task<LadosConflito> LerAsync(string repo, CancellationToken cancelar = default)
    {
        var pastaGit = (await SaidaAsync(repo, cancelar, "rev-parse", "--absolute-git-dir")).Replace('/', Path.DirectorySeparatorChar);
        bool Tem(string nome) => pastaGit.Length > 0 && (File.Exists(Path.Combine(pastaGit, nome)) || Directory.Exists(Path.Combine(pastaGit, nome)));
        Task<string> Commit(string referencia) => SaidaAsync(repo, cancelar, "log", "-1", "--format=%h  %s", referencia);

        if (Tem("rebase-merge") || Tem("rebase-apply"))
        {
            return new()
            {
                Operacao = "rebase",
                DetalheEntrando = await Commit("REBASE_HEAD") + "   (o seu commit sendo reaplicado)",
                TituloAtual = "Nova base",
                UsarAtual = "Usar da nova base",
                CurtoAtual = "nova base",
                DetalheAtual = await Commit("HEAD") + "   (onde o commit está sendo aplicado)",
            };
        }
        var lados = new LadosConflito { DetalheAtual = await SaidaAsync(repo, cancelar, "rev-parse", "--abbrev-ref", "HEAD") + "   (como está hoje)" };
        if (Tem("CHERRY_PICK_HEAD"))
            return lados with { Operacao = "cherry-pick", DetalheEntrando = await Commit("CHERRY_PICK_HEAD") + "   (o que está sendo aplicado)" };
        if (Tem("REVERT_HEAD"))
        {
            return lados with
            {
                Operacao = "revert",
                TituloEntrando = "Reversão",
                UsarEntrando = "Usar da reversão",
                CurtoEntrando = "reversão",
                DetalheEntrando = "desfazendo " + await Commit("REVERT_HEAD"),
            };
        }
        var entrando = lados with { TituloEntrando = "Entrando", UsarEntrando = "Usar do que entra", CurtoEntrando = "o que entra" };
        if (Tem("MERGE_HEAD"))
            return entrando with { Operacao = "merge", DetalheEntrando = PrimeiraLinha(Path.Combine(pastaGit, "MERGE_MSG")) ?? await Commit("MERGE_HEAD") };
        return entrando with { Operacao = "stash", DetalheEntrando = "alteração sendo aplicada (stash pop / apply)" };
    }

    static string? PrimeiraLinha(string caminho) =>
        File.Exists(caminho) ? File.ReadLines(caminho).FirstOrDefault(l => l.Length > 0) : null;

    async Task<string> SaidaAsync(string repo, CancellationToken cancelar, params string[] argumentos)
    {
        var args = Git.Em(repo).Comando(argumentos[0]);
        foreach (var argumento in argumentos.Skip(1)) args = args.Argumento(argumento);
        var saida = await git.ExecutarBrutoAsync(args, cancelar);
        return saida.Sucesso ? saida.Texto.Trim() : "";
    }
}
