namespace Menage.Core.Operacoes.Stash;

/// <param name="Hash">Identifica o stash mesmo quando a lista se reordena.</param>
public sealed record EntradaDeStash(Projeto Projeto, string Ref, string Hash, string Data, string Mensagem);

/// <param name="Manter">Apply: aplica sem tirar da lista.</param>
public sealed record OpcoesDeStashPop(IReadOnlyList<EntradaDeStash> Itens, bool Manter = false);

public sealed record ResultadoDeStashPop(int Aplicados, int Total);

/// <summary>Aplica os stashes escolhidos, projeto a projeto; no primeiro conflito para aquele projeto e o restante fica na lista.</summary>
public sealed class StashPopEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git)
    : OperacaoEmLote<OpcoesDeStashPop, ResultadoDeStashPop>(projetos, dialogos, progresso)
{
    public override string Id => "git.stash-pop";

    public override string Nome => "Stash pop";

    public override IReadOnlyList<Projeto> ProjetosAlvo(OpcoesDeStashPop opcoes) =>
        [.. opcoes.Itens.Select(i => i.Projeto).DistinctBy(p => p.Caminho)];

    /// <summary>Do mais antigo para o mais novo em cada projeto.</summary>
    public async Task<IReadOnlyList<EntradaDeStash>> ListarAsync(IEnumerable<Projeto> alvo, CancellationToken cancelar = default)
    {
        var entradas = new List<EntradaDeStash>();
        foreach (var projeto in alvo.Where(p => ConsultasGit.EhRepositorio(p.Caminho)))
        {
            var args = Git.Em(projeto.Caminho).Comando("stash").Argumento("list").Opcao("--date=format:%d/%m/%y %H:%M").Opcao("--format=%gd%x1f%H%x1f%cd%x1f%gs");
            foreach (var linha in (await git.ExecutarBrutoAsync(args, cancelar)).Linhas())
                if (linha.Split('\x1f') is { Length: >= 4 } p) entradas.Add(new(projeto, p[0], p[1], p[2], p[3]));
        }

        entradas.Reverse();
        return entradas;
    }

    protected override async Task<ResultadoDoProjeto<ResultadoDeStashPop>> ExecutarEmProjeto(Projeto projeto, OpcoesDeStashPop opcoes, CancellationToken cancelar)
    {
        var entradas = opcoes.Itens.Where(i => i.Projeto.Caminho == projeto.Caminho).ToList();
        var aplicados = 0;
        foreach (var entrada in entradas)
        {
            if (!await AplicarAsync(projeto.Caminho, entrada.Hash, opcoes.Manter, cancelar))
                return ResultadoDoProjeto<ResultadoDeStashPop>.Falha(
                    new Erro($"{aplicados} de {entradas.Count} aplicado(s)", $"Conflito ou falha em \"{entrada.Mensagem}\"; ele continua na lista.", "Resolva os conflitos."),
                    new(aplicados, entradas.Count));
            aplicados++;
        }

        return ResultadoDoProjeto<ResultadoDeStashPop>.Ok(new(aplicados, entradas.Count), $"{aplicados} stash(es) aplicado(s)" + (opcoes.Manter ? " (mantidos na lista)" : ""));
    }

    async Task<bool> AplicarAsync(string repo, string hash, bool manter, CancellationToken cancelar)
    {
        if (!(await git.ExecutarAsync(Git.Em(repo).Comando("stash").Argumento("apply").Argumento(hash), cancelar)).EhOk) return false;
        if (manter) return true;

        var lista = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("stash").Argumento("list").Opcao("--format=%H"), cancelar)).Linhas();
        var indice = lista.ToList().FindIndex(h => h.Trim() == hash);
        if (indice >= 0) await git.ExecutarAsync(Git.Em(repo).Comando("stash").Argumento("drop").Argumento($"stash@{{{indice}}}"), cancelar);
        return true;
    }
}
