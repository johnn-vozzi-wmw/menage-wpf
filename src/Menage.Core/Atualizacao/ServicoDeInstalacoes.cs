namespace Menage.Core.Atualizacao;

public enum CanalDeAtualizacao { Estavel, Beta }

public enum EstadoDaVerificacao { SemOrigem, ForaDoInstalador, EmDia, NovaVersao, Falhou }

public sealed record ResultadoDaVerificacao(EstadoDaVerificacao Estado, string? Versao = null, string? Notas = null, string? Mensagem = null);

/// <summary>Uma linha da lista de versões do feed.</summary>
public sealed record VersaoDoFeed(string Versao, string? Notas, long Tamanho, bool EhAtual, bool EhMaisNova);

public sealed record SituacaoDaInstalacao(string VersaoAtual, bool Instalado, string? Origem, CanalDeAtualizacao Canal, string? OrigemDoAmbiente);

/// <summary>
/// Gestão de versão dentro do app: verificar, atualizar, voltar de versão, trocar origem e canal. Não faz nada sozinho:
/// quem chama decide quando baixar e reiniciar. Falhas voltam como <see cref="Resultado{T}"/>, nunca como exceção.
/// </summary>
public sealed class ServicoDeInstalacoes
{
    readonly IFabricaDeGerente _fabrica;
    readonly string _caminhoOpcoes;
    readonly string _versaoEmExecucao;
    OpcoesDeAtualizacao _opcoes;
    IGerenteDeInstalacao? _gerente;

    public ServicoDeInstalacoes(IFabricaDeGerente fabrica, OpcoesDeAtualizacao opcoes, string caminhoOpcoes, string versaoEmExecucao)
    {
        (_fabrica, _opcoes, _caminhoOpcoes, _versaoEmExecucao) = (fabrica, opcoes, caminhoOpcoes, versaoEmExecucao);
        Recriar();
    }

    public SituacaoDaInstalacao Situacao => new(
        _gerente?.VersaoAtual ?? _versaoEmExecucao, _gerente?.Instalado ?? false, _opcoes.Origem, Canal, OrigemDoAmbiente);

    public CanalDeAtualizacao Canal => string.Equals(_opcoes.Canal, "beta", StringComparison.OrdinalIgnoreCase) ? CanalDeAtualizacao.Beta : CanalDeAtualizacao.Estavel;

    static string? OrigemDoAmbiente =>
        Environment.GetEnvironmentVariable(OpcoesDeAtualizacao.VariavelOrigem) is { Length: > 0 } v ? v : null;

    /// <summary>Grava a origem (nulo ou vazio limpa). A variável de ambiente, se existir, continua tendo prioridade.</summary>
    public void DefinirOrigem(string? origem) => Alterar(_opcoes with { Origem = string.IsNullOrWhiteSpace(origem) ? null : origem.Trim() });

    public void DefinirCanal(CanalDeAtualizacao canal) => Alterar(_opcoes with { Canal = canal == CanalDeAtualizacao.Beta ? "beta" : null });

    public async Task<ResultadoDaVerificacao> VerificarAsync(CancellationToken ct = default)
    {
        if (_gerente is null) return new(EstadoDaVerificacao.SemOrigem);
        if (!_gerente.Instalado) return new(EstadoDaVerificacao.ForaDoInstalador);
        var lista = await ListarAsync(ct);
        if (!lista.EhOk) return new(EstadoDaVerificacao.Falhou, Mensagem: lista.Erro.ToString());
        var maisNova = lista.Valor.FirstOrDefault(v => v.EhMaisNova);
        return maisNova is not null && VersaoSemantica.Comparador.Compare(maisNova.Versao, Situacao.VersaoAtual) > 0
            ? new(EstadoDaVerificacao.NovaVersao, maisNova.Versao, maisNova.Notas)
            : new(EstadoDaVerificacao.EmDia);
    }

    /// <summary>Versões do feed, da mais nova para a mais antiga.</summary>
    public async Task<Resultado<IReadOnlyList<VersaoDoFeed>>> ListarAsync(CancellationToken ct = default)
    {
        if (_gerente is null)
            return Resultado.Falha<IReadOnlyList<VersaoDoFeed>>("Nenhuma origem de atualização configurada.", acao: "Informe a pasta de rede, a URL ou o GitHub em Sobre / Atualizações.");
        try
        {
            var atual = Situacao.VersaoAtual;
            var itens = (await _gerente.ListarAsync(ct)).OrderByDescending(l => l.Versao, VersaoSemantica.Comparador).ToList();
            IReadOnlyList<VersaoDoFeed> linhas = itens.Select((l, i) => new VersaoDoFeed(
                l.Versao, l.Notas, l.Tamanho, VersaoSemantica.Comparador.Compare(l.Versao, atual) == 0, i == 0)).ToList();
            return Resultado.Ok(linhas);
        }
        catch (OperationCanceledException) { return Resultado.Falha<IReadOnlyList<VersaoDoFeed>>(Erro.Cancelado); }
        catch (Exception ex)
        {
            return Resultado.Falha<IReadOnlyList<VersaoDoFeed>>("Não foi possível ler a origem de atualização.", ex.Message, "Confira se a pasta, a URL ou o token estão acessíveis.");
        }
    }

    /// <summary>A maior versão do feed que seja menor que a atual (alvo do "voltar"); nulo se não houver.</summary>
    public string? VersaoAnterior(IEnumerable<VersaoDoFeed> feed) => feed
        .Where(v => VersaoSemantica.Comparador.Compare(v.Versao, Situacao.VersaoAtual) < 0)
        .OrderByDescending(v => v.Versao, VersaoSemantica.Comparador).FirstOrDefault()?.Versao;

    /// <summary>Baixa a versão (a mais nova ou uma anterior) e, se pedido, aplica e reinicia. Sobe ou volta: o feed decide.</summary>
    public async Task<Resultado<Nada>> InstalarAsync(string versao, IProgress<int> progresso, bool reiniciar = true, CancellationToken ct = default)
    {
        if (_gerente is not { Instalado: true })
            return Resultado.Falha<Nada>("Este Menage não foi instalado pelo instalador.", "Só a versão instalada pode ser atualizada ou revertida.");
        try
        {
            var feed = await ListarAsync(ct);
            if (!feed.EhOk) return Resultado.Falha<Nada>(feed.Erro);
            if (feed.Valor.All(v => v.Versao != versao)) return Resultado.Falha<Nada>($"A versão {versao} não está mais na origem.");
            await _gerente.BaixarAsync(versao, progresso, ct);
            if (reiniciar) _gerente.AplicarEReiniciar(versao);
            return Resultado.Ok();
        }
        catch (OperationCanceledException) { return Resultado.Falha<Nada>(Erro.Cancelado); }
        catch (Exception ex)
        {
            return Resultado.Falha<Nada>("A instalação da versão falhou.", ex.Message, "Tente de novo; a versão atual continua intacta.");
        }
    }

    void Alterar(OpcoesDeAtualizacao novas)
    {
        _opcoes = novas;
        _opcoes.Salvar(_caminhoOpcoes);
        Recriar();
    }

    void Recriar() => _gerente = _fabrica.Criar(OrigemDoAmbiente ?? _opcoes.Origem, _opcoes.Canal);
}
