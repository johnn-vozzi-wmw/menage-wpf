namespace Menage.Core.Diff;

/// <summary>Lê as alterações de um repositório e monta o diff HEAD x pasta de um arquivo. Só lê: nunca escreve no repositório.</summary>
public sealed class ServicoDiff(IGit git)
{
    const long LimiteDeBytes = 32L * 1024 * 1024;

    readonly ConstrutorDiff _construtor = new();

    public async Task<Resultado<IReadOnlyList<ArquivoAlterado>>> ListarAsync(string repo, CancellationToken cancelar = default)
    {
        var saida = await git.ExecutarAsync(Git.Em(repo).Comando("status").Opcao("--porcelain").Opcao("-z").Opcao("-uall"), cancelar);
        return saida.Mapear<IReadOnlyList<ArquivoAlterado>>(s => OrdenarPorPasta(ArquivoAlterado.LerStatus(s.Texto)));
    }

    /// <summary>Diff do arquivo inteiro. Novo ou excluído: um dos lados vazio. Renomeado: o lado esquerdo é o caminho antigo no HEAD.</summary>
    public async Task<Resultado<ModeloDiff>> MontarAsync(string repo, ArquivoAlterado arquivo, CancellationToken cancelar = default)
    {
        var antes = await VersaoNoHeadAsync(repo, arquivo.Antigo ?? arquivo.Caminho, cancelar);
        var caminho = Path.Combine(repo, arquivo.Caminho.Replace('/', Path.DirectorySeparatorChar));
        if (TamanhoDe(caminho) > LimiteDeBytes || antes?.LongLength > LimiteDeBytes)
            return Resultado.Ok(ModeloDiff.SemConteudo(SemTexto.MuitoGrande));

        var atual = LerSeExistir(caminho, out var erro);
        if (erro is not null) return Resultado.Falha<ModeloDiff>($"Não foi possível ler {arquivo.Nome}.", erro);
        var (novo, excluido) = (antes is null && atual is not null, atual is null && antes is not null);
        if ((atual is not null && Codificacoes.Binario(atual)) || (antes is not null && Codificacoes.Binario(antes)))
            return Resultado.Ok(ModeloDiff.SemConteudo(SemTexto.Binario, novo, excluido));

        return Resultado.Ok(await Task.Run(
            () => _construtor.Construir(antes is null ? null : ArquivoTexto.De(antes), atual is null ? null : ArquivoTexto.De(atual)), cancelar));
    }

    static List<ArquivoAlterado> OrdenarPorPasta(IEnumerable<ArquivoAlterado> arquivos) =>
        arquivos.OrderBy(a => a.Pasta, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Nome, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Conteúdo como um checkout gravaria (aplica core.autocrlf e .gitattributes). Com o conteúdo cru do HEAD,
    /// todo arquivo de um repositório com autocrlf apareceria com todas as linhas mudando o fim de linha.
    /// </summary>
    async Task<byte[]?> VersaoNoHeadAsync(string repo, string arquivo, CancellationToken cancelar)
    {
        var saida = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("cat-file").Opcao("--filters").Argumento($"HEAD:{arquivo}"), cancelar);
        return saida.Sucesso ? saida.Saida : null;
    }

    static long TamanhoDe(string caminho) => File.Exists(caminho) ? new FileInfo(caminho).Length : 0;

    static byte[]? LerSeExistir(string caminho, out string? erro)
    {
        erro = null;
        if (!File.Exists(caminho)) return null;
        try
        {
            using var arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[arquivo.Length];
            arquivo.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            erro = e.Message;
            return null;
        }
    }
}
