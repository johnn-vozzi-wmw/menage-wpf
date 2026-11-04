using System.Text;

namespace Menage.Core.Diff;

/// <summary>Arquivo em conflito pronto para a tela. Lado atual = ":2", entrando = ":3", base = ":1".</summary>
public sealed record ArquivoEmConflito(
    string Caminho,
    IReadOnlyList<string> Atual,
    IReadOnlyList<string> Entrando,
    JuncaoConflito Juncao,
    FormatoArquivo Formato,
    LadosConflito Lados);

/// <summary>Como um arquivo em conflito pode ser resolvido.</summary>
public enum FormaDeResolver { Texto, EscolherLado, ManterOuExcluir, Excluir }

public enum LadoEscolhido { Entrando, Atual, Manter, Excluir }

/// <summary>Lê os lados de um conflito e grava a solução. Só o repositório informado é tocado.</summary>
public sealed class ServicoConflito(IGit git)
{
    readonly LeitorDeLados _lados = new(git);

    public static string Descricao(string codigo) => codigo switch
    {
        "UU" => "alterado dos dois lados",
        "AA" => "criado dos dois lados",
        "DU" or "UD" => "excluído de um lado, alterado do outro",
        "AU" or "UA" => "criado de um lado",
        "DD" => "excluído dos dois lados",
        _ => codigo,
    };

    public Task<LadosConflito> LerLadosAsync(string repo, CancellationToken cancelar = default) => _lados.LerAsync(repo, cancelar);

    public async Task<Resultado<IReadOnlyList<ArquivoAlterado>>> ListarAsync(string repo, CancellationToken cancelar = default)
    {
        var saida = await git.ExecutarAsync(Git.Em(repo).Comando("status").Opcao("--porcelain").Opcao("-z"), cancelar);
        return saida.Mapear<IReadOnlyList<ArquivoAlterado>>(
            s => ArquivoAlterado.LerStatus(s.Texto).Where(a => a.Tipo == TipoAlteracao.EmConflito).ToList());
    }

    /// <summary>Conflito de texto abre o editor; binário ou excluído de um lado pede a escolha de quem fica.</summary>
    public async Task<FormaDeResolver> ClassificarAsync(string repo, ArquivoAlterado arquivo, CancellationToken cancelar = default)
    {
        if (arquivo.CodigoStatus == "DD") return FormaDeResolver.Excluir;
        if (arquivo.CodigoStatus is not ("UU" or "AA")) return FormaDeResolver.ManterOuExcluir;
        var atual = await EstagioAsync(repo, 2, arquivo.Caminho, cancelar);
        var entrando = await EstagioAsync(repo, 3, arquivo.Caminho, cancelar);
        return Codificacoes.Binario(atual ?? []) || Codificacoes.Binario(entrando ?? []) ? FormaDeResolver.EscolherLado : FormaDeResolver.Texto;
    }

    public async Task<Resultado<ArquivoEmConflito>> CarregarAsync(string repo, string arquivo, CancellationToken cancelar = default)
    {
        var atual = await EstagioAsync(repo, 2, arquivo, cancelar) ?? [];
        var entrando = await EstagioAsync(repo, 3, arquivo, cancelar) ?? [];
        var origem = await EstagioAsync(repo, 1, arquivo, cancelar) ?? [];
        var (a, e, b) = (ArquivoTexto.De(atual), ArquivoTexto.De(entrando), ArquivoTexto.De(origem));
        var juncao = await JuntarAsync(a, b, e, cancelar);
        var lados = await _lados.LerAsync(repo, cancelar);
        return juncao.Mapear(j => new ArquivoEmConflito(arquivo, a.Linhas, e.Linhas, j, FormatoArquivo.De(atual), lados));
    }

    /// <summary>Grava como o arquivo era e marca resolvido.</summary>
    public async Task<Resultado<Nada>> GravarAsync(string repo, ArquivoEmConflito arquivo, IReadOnlyList<string> linhas, CancellationToken cancelar = default)
    {
        var pendentes = JuncaoConflito.Pendentes(linhas).Count;
        if (pendentes > 0)
            return Resultado.Falha<Nada>($"Ainda há {pendentes} conflito(s) sem escolha.", acao: "Escolha um lado (ou edite o resultado) em todos antes de salvar.");
        try
        {
            await File.WriteAllBytesAsync(CaminhoDe(repo, arquivo.Caminho), arquivo.Formato.Montar(linhas), cancelar);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Resultado.Falha<Nada>($"Não foi possível gravar {arquivo.Caminho}.", e.Message);
        }
        var add = await git.ExecutarAsync(Git.Em(repo).Comando("add").Caminhos([arquivo.Caminho]), cancelar);
        return add.EhOk ? Resultado.Ok() : Resultado.Falha<Nada>("O arquivo foi gravado, mas não deu para marcá-lo como resolvido (git add).", add.Erro.ToString());
    }

    /// <summary>Binário ou excluído de um lado: fica um lado inteiro, ou o arquivo sai.</summary>
    public async Task<Resultado<Nada>> ResolverAsync(string repo, ArquivoAlterado arquivo, LadoEscolhido lado, CancellationToken cancelar = default)
    {
        var caminho = new[] { arquivo.Caminho };
        if (lado == LadoEscolhido.Excluir)
            return Vazio(await git.ExecutarAsync(Git.Em(repo).Comando("rm").Opcao("--quiet").Caminhos(caminho), cancelar));
        if (lado == LadoEscolhido.Manter && File.Exists(CaminhoDe(repo, arquivo.Caminho)))
            return Vazio(await git.ExecutarAsync(Git.Em(repo).Comando("add").Caminhos(caminho), cancelar));

        var opcao = lado switch
        {
            LadoEscolhido.Entrando => "--theirs",
            LadoEscolhido.Atual => "--ours",
            _ => arquivo.CodigoStatus is "DU" or "AU" ? "--theirs" : "--ours",
        };
        var checkout = await git.ExecutarAsync(Git.Em(repo).Comando("checkout").Opcao(opcao).Caminhos(caminho), cancelar);
        if (!checkout.EhOk) return Vazio(checkout);
        return Vazio(await git.ExecutarAsync(Git.Em(repo).Comando("add").Caminhos(caminho), cancelar));
    }

    static Resultado<Nada> Vazio(Resultado<SaidaProcesso> saida) => saida.Mapear(_ => default(Nada));

    static string CaminhoDe(string repo, string arquivo) => Path.Combine(repo, arquivo.Replace('/', Path.DirectorySeparatorChar));

    async Task<byte[]?> EstagioAsync(string repo, int estagio, string arquivo, CancellationToken cancelar)
    {
        var saida = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("show").Argumento($":{estagio}:{arquivo}"), cancelar);
        return saida.Sucesso ? saida.Saida : null;
    }

    /// <summary>
    /// <c>git merge-file</c> com as três versões em UTF-8 e LF: lados com codificação ou fim de linha diferentes não
    /// viram conflito falso, e o resultado volta ao formato do lado atual só ao gravar.
    /// </summary>
    async Task<Resultado<JuncaoConflito>> JuntarAsync(ArquivoTexto atual, ArquivoTexto origem, ArquivoTexto entrando, CancellationToken cancelar)
    {
        var pasta = Directory.CreateTempSubdirectory("mg-merge-").FullName;
        try
        {
            await EscreverAsync(pasta, "atual", atual, cancelar);
            await EscreverAsync(pasta, "base", origem, cancelar);
            await EscreverAsync(pasta, "entrando", entrando, cancelar);
            var args = Git.Em(pasta).Comando("merge-file").Opcao("-p").Opcao("--diff3")
                .Opcao($"--marker-size={JuncaoConflito.TamanhoDoMarcador}")
                .Opcao("-L", "atual").Opcao("-L", "base").Opcao("-L", "entrando")
                .Argumento("atual").Argumento("base").Argumento("entrando");
            var saida = await git.ExecutarBrutoAsync(args, cancelar);
            // merge-file devolve o número de conflitos; só código negativo (ou 255) é erro de verdade.
            if (saida.FalhaAoIniciar is not null || saida.CodigoDeSaida is < 0 or >= 127)
                return Resultado.Falha<JuncaoConflito>("Não foi possível juntar as versões do arquivo.", saida.TextoErro);
            return Resultado.Ok(JuncaoConflito.Ler(ArquivoTexto.DeTexto(Encoding.UTF8.GetString(saida.Saida)).Linhas));
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    static Task EscreverAsync(string pasta, string nome, ArquivoTexto arquivo, CancellationToken cancelar)
    {
        var texto = arquivo.Linhas.Count == 0 ? "" : string.Join('\n', arquivo.Linhas) + "\n";
        return File.WriteAllTextAsync(Path.Combine(pasta, nome), texto, Codificacoes.Utf8SemBom, cancelar);
    }
}
