namespace Menage.Core.CherryPick;

/// <summary>
/// Aplica um item: <c>cherry-pick -x --empty=drop</c>. O estado do item fica Aplicado, Pulado (já existe no destino, só tinha o
/// script, ou é merge), Conflito (git em cherry-pick em andamento) ou Erro.
/// </summary>
public sealed class AplicadorDaFila(IGit git, ConsultasGit consultas, RetiradorDeScript script)
{
    public async Task AplicarAsync(ItemDaFila item, CancellationToken cancelar = default)
    {
        var repo = item.Caminho;
        if (!await consultas.RefVerificaAsync(repo, item.Hash + "^{commit}", cancelar))
        {
            Definir(item, EstadoDoItem.Erro, "commit não encontrado neste projeto (faça um fetch)");
            return;
        }

        var pais = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-list").Opcao("--parents").Opcao("-n", "1").Argumento(item.Hash), cancelar)).PrimeiraLinha();
        if (pais.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 2)
        {
            Definir(item, EstadoDoItem.Pulado, "commit de merge: não é aplicado pela fila");
            return;
        }

        if (await JaAplicadoAsync(repo, item.Hash, cancelar))
        {
            Definir(item, EstadoDoItem.Pulado, "o conteúdo já existe no destino");
            return;
        }

        var antes = await HeadAsync(repo, cancelar);
        item.HeadAntes = antes;
        var resultado = await git.ExecutarAsync(Git.Em(repo).Comando("cherry-pick").Opcao("-x").Opcao("--empty=drop").Argumento(item.Hash), cancelar);
        if (resultado.EhOk) await AposSucessoAsync(item, antes, cancelar);
        else await AposFalhaAsync(item, resultado.Erro, cancelar);
    }

    // Mesmo patch já no destino (outro hash): git cherry marca com "-".
    async Task<bool> JaAplicadoAsync(string repo, string hash, CancellationToken cancelar)
    {
        if (!await consultas.RefVerificaAsync(repo, hash + "^", cancelar)) return false;
        var saida = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("cherry").Argumento("HEAD").Argumento(hash).Argumento(hash + "^"), cancelar);
        return saida.Linhas().Any(l => l.StartsWith("- ", StringComparison.Ordinal));
    }

    async Task AposSucessoAsync(ItemDaFila item, string antes, CancellationToken cancelar)
    {
        if (await HeadAsync(item.Caminho, cancelar) == antes)
        {
            Definir(item, EstadoDoItem.Pulado, "o conteúdo já existe no destino");
            return;
        }

        var retirada = await script.RetirarDoCommitAsync(item.Caminho, item.Hash, cancelar);
        var vazio = await HeadAsync(item.Caminho, cancelar) == antes;
        if (vazio) Definir(item, EstadoDoItem.Pulado, retirada?.Mensagem ?? "o conteúdo já existe no destino");
        else Definir(item, EstadoDoItem.Aplicado, retirada?.Mensagem is { } m ? "aplicado - " + m : "aplicado");
    }

    async Task AposFalhaAsync(ItemDaFila item, Erro erro, CancellationToken cancelar)
    {
        var repo = item.Caminho;
        var tratado = await script.ResolverConflitoAsync(repo, item.Hash, cancelar);
        if (tratado.Desfecho == DesfechoDoScript.Concluido)
        {
            Definir(item, EstadoDoItem.Aplicado, "aplicado - " + tratado.Mensagem);
            return;
        }

        if (tratado.Desfecho == DesfechoDoScript.SoScript)
        {
            Definir(item, EstadoDoItem.Pulado, tratado.Mensagem ?? "");
            return;
        }

        if (await consultas.RefVerificaAsync(repo, "CHERRY_PICK_HEAD", cancelar))
        {
            var arquivos = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("diff").Opcao("--name-only").Opcao("--diff-filter=U"), cancelar)).Linhas();
            if (arquivos.Count > 0)
            {
                item.Arquivos = [.. arquivos];
                Definir(item, EstadoDoItem.Conflito, $"{arquivos.Count} arquivo(s) em conflito");
                return;
            }
        }

        var primeira = erro.ToString().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "falha no cherry-pick";
        Definir(item, EstadoDoItem.Erro, primeira.Length > 140 ? primeira[..137] + "..." : primeira);
    }

    async Task<string> HeadAsync(string repo, CancellationToken cancelar) =>
        (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-parse").Argumento("HEAD"), cancelar)).PrimeiraLinha();

    static void Definir(ItemDaFila item, EstadoDoItem estado, string detalhe) => (item.Estado, item.Detalhe) = (estado, detalhe);
}
