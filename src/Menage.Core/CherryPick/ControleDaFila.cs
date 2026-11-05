namespace Menage.Core.CherryPick;

public enum Retomada { Pronto, AindaConflito, Marcadores, Erro }

/// <summary>O que o usuário decide depois que a fila pausou num conflito ou erro.</summary>
public sealed class ControleDaFila(ServicoDaFila servico, IGit git, ConsultasGit consultas, RetiradorDeScript script)
{
    FilaDeCherryPick Fila => servico.Fila;

    /// <summary>
    /// "Já resolvi": confere que não há arquivo em conflito nem marcadores <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>/<c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> e conclui com
    /// <c>--continue</c>. Se o cherry-pick já não está em andamento, vê se o usuário concluiu pelo git; senão o item volta a pendente.
    /// </summary>
    public async Task<(Retomada Resultado, string Mensagem)> RetomarAsync(bool ignorarMarcadores = false, CancellationToken cancelar = default)
    {
        if (Fila.ItemAtual is not { } item) return (Retomada.Pronto, "");
        var repo = item.Caminho;
        if (!await consultas.RefVerificaAsync(repo, "CHERRY_PICK_HEAD", cancelar)) return (await ConcluidoPorForaAsync(item, cancelar), "");

        var conflitos = await ArquivosEmConflitoAsync(repo, cancelar);
        if (conflitos.Count > 0)
        {
            item.Arquivos = [.. conflitos];
            servico.Salvar();
            return (Retomada.AindaConflito, $"Ainda há {conflitos.Count} arquivo(s) em conflito: {Resumo(conflitos)}. Resolva e faça git add.");
        }

        if (!ignorarMarcadores && await ComMarcadoresAsync(repo, cancelar) is { Count: > 0 } marcados)
            return (Retomada.Marcadores, $"Sobraram marcadores de conflito (<<<<<<< / >>>>>>>) em: {Resumo(marcados)}.");

        var continuar = await git.ExecutarAsync(Git.Em(repo).Config("core.editor", "true").Comando("cherry-pick").Opcao("--continue"), cancelar);
        if (continuar.EhOk)
        {
            var retirada = await script.RetirarDoCommitAsync(repo, item.Hash, cancelar);
            Concluir(item, EstadoDoItem.Aplicado, "aplicado com solução manual" + (retirada?.Mensagem is { } m ? " - " + m : ""));
            return (Retomada.Pronto, "");
        }

        if (await consultas.IndiceVazioAsync(repo, cancelar))
        {
            await git.ExecutarAsync(Git.Em(repo).Comando("cherry-pick").Opcao("--skip"), cancelar);
            Concluir(item, EstadoDoItem.Pulado, "resultado vazio: o conteúdo já existia");
            return (Retomada.Pronto, "");
        }

        return (Retomada.Erro, "Não foi possível concluir o cherry-pick: " + continuar.Erro.Causa);
    }

    /// <summary>Descarta o cherry-pick em andamento do item e segue.</summary>
    public async Task PularAsync(CancellationToken cancelar = default)
    {
        if (Fila.ItemAtual is not { } item) return;
        if (await consultas.RefVerificaAsync(item.Caminho, "CHERRY_PICK_HEAD", cancelar))
            await git.ExecutarAsync(Git.Em(item.Caminho).Comando("cherry-pick").Opcao("--skip"), cancelar);
        Concluir(item, EstadoDoItem.Pulado, "pulado por você");
    }

    /// <summary>Item com erro volta a pendente. Se o git ficou em cherry-pick, ele é abortado para o item recomeçar limpo.</summary>
    public async Task TentarDeNovoAsync(CancellationToken cancelar = default)
    {
        if (Fila.ItemAtual is not { } item) return;
        if (await consultas.RefVerificaAsync(item.Caminho, "CHERRY_PICK_HEAD", cancelar))
            await git.ExecutarAsync(Git.Em(item.Caminho).Comando("cherry-pick").Opcao("--abort"), cancelar);
        (item.Estado, item.Detalhe, item.Arquivos) = (EstadoDoItem.Pendente, "", []);
        Fila.Estado = EstadoDaFila.Parada;
        servico.Salvar();
    }

    /// <summary>
    /// Encerra a fila: aborta o cherry-pick em andamento e, se pedido, volta o destino de cada projeto ao HEAD de antes (só se o projeto
    /// ainda está nele e o HEAD inicial é ancestral). Devolve um relatório por projeto.
    /// </summary>
    public async Task<IReadOnlyList<string>> AbortarAsync(bool restaurar, CancellationToken cancelar = default)
    {
        var relatorio = new List<string>();
        foreach (var grupo in Fila.Itens.GroupBy(i => i.Caminho, StringComparer.OrdinalIgnoreCase))
            relatorio.Add(await AbortarProjetoAsync(grupo.Key, grupo.First().Projeto, restaurar, cancelar));

        foreach (var item in Fila.Itens.Where(i => !i.Resolvido)) (item.Estado, item.Arquivos) = (EstadoDoItem.Pendente, []);
        Fila.Estado = EstadoDaFila.Abortada;
        servico.Salvar();
        return relatorio;
    }

    async Task<string> AbortarProjetoAsync(string repo, string nome, bool restaurar, CancellationToken cancelar)
    {
        if (!ConsultasGit.EhRepositorio(repo)) return $"{nome}: pasta não encontrada";
        var partes = new List<string>();
        if (await consultas.RefVerificaAsync(repo, "CHERRY_PICK_HEAD", cancelar))
        {
            await git.ExecutarAsync(Git.Em(repo).Comando("cherry-pick").Opcao("--abort"), cancelar);
            partes.Add("cherry-pick em andamento abortado");
        }

        if (restaurar && Fila.HeadInicial.TryGetValue(repo, out var head)) partes.Add(await RestaurarAsync(repo, head, cancelar));
        return $"{nome}: {(partes.Count > 0 ? string.Join("; ", partes) : "nada a desfazer")}";
    }

    async Task<string> RestaurarAsync(string repo, string head, CancellationToken cancelar)
    {
        var atual = await consultas.BranchAtualAsync(repo, cancelar);
        var hoje = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-parse").Argumento("HEAD"), cancelar)).PrimeiraLinha();
        if (atual != Fila.Destino) return $"não restaurado (não está mais na branch \"{Fila.Destino}\")";
        if (hoje == head) return "já estava no HEAD inicial";
        if (!await consultas.EhAncestralAsync(repo, head, "HEAD", cancelar)) return "não restaurado (o HEAD inicial não é mais ancestral da branch)";
        if (!(await git.ExecutarAsync(Git.Em(repo).Comando("reset").Opcao("--hard").Argumento(head), cancelar)).EhOk) return "não foi possível restaurar";

        foreach (var item in Fila.Itens.Where(i => string.Equals(i.Caminho, repo, StringComparison.OrdinalIgnoreCase) && i.Estado == EstadoDoItem.Aplicado))
            (item.Estado, item.Detalhe) = (EstadoDoItem.Desfeito, "desfeito ao abortar a fila");
        return $"\"{Fila.Destino}\" voltou ao HEAD inicial";
    }

    // Sem cherry-pick em andamento: o usuário concluiu pelo git (commit com a linha -x) ou abortou.
    async Task<Retomada> ConcluidoPorForaAsync(ItemDaFila item, CancellationToken cancelar)
    {
        var repo = item.Caminho;
        var corpo = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("log").Opcao("-1").Opcao("--format=%B"), cancelar)).Texto;
        var head = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-parse").Argumento("HEAD"), cancelar)).PrimeiraLinha();
        if (head != item.HeadAntes && corpo.Contains(item.Hash, StringComparison.OrdinalIgnoreCase))
        {
            var retirada = await script.RetirarDoCommitAsync(repo, item.Hash, cancelar);
            Concluir(item, EstadoDoItem.Aplicado, "aplicado (concluído por você no git)" + (retirada?.Mensagem is { } m ? " - " + m : ""));
        }
        else
        {
            (item.Estado, item.Detalhe, item.Arquivos) = (EstadoDoItem.Pendente, "", []);
            Fila.Estado = EstadoDaFila.Parada;
            servico.Salvar();
        }

        return Retomada.Pronto;
    }

    void Concluir(ItemDaFila item, EstadoDoItem estado, string detalhe)
    {
        (item.Estado, item.Detalhe, item.Arquivos) = (estado, detalhe, []);
        Fila.Atual = Math.Min(Fila.Itens.IndexOf(item) + 1, Fila.Itens.Count);
        Fila.Estado = EstadoDaFila.Parada;
        servico.Salvar();
    }

    async Task<List<string>> ArquivosEmConflitoAsync(string repo, CancellationToken cancelar) =>
        [.. (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("diff").Opcao("--name-only").Opcao("--diff-filter=U"), cancelar)).Linhas()];

    // Arquivos preparados que ainda têm marcadores de conflito.
    async Task<List<string>> ComMarcadoresAsync(string repo, CancellationToken cancelar)
    {
        var preparados = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("diff").Opcao("--cached").Opcao("--name-only"), cancelar)).Linhas();
        if (preparados.Count == 0) return [];
        var args = Git.Em(repo).Comando("grep").Opcao("--cached").Opcao("-l").Opcao("-I").Opcao("-E").Argumento("^(<<<<<<<|>>>>>>>) ").Caminhos(preparados);
        return [.. (await git.ExecutarBrutoAsync(args, cancelar)).Linhas()];
    }

    static string Resumo(List<string> arquivos) => string.Join(", ", arquivos.Take(4)) + (arquivos.Count > 4 ? "..." : "");
}
