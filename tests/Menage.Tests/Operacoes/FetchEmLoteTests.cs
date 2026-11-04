namespace Menage.Tests.Operacoes;

public sealed class FetchEmLoteTests : IDisposable
{
    readonly RepositorioDescartavel _repo = RepositorioDescartavel.Criar();
    readonly DialogosFalsos _dialogos = new();
    readonly ProjetosEmMemoria _projetos = new();
    readonly GitExecutor _git;

    public FetchEmLoteTests() =>
        _git = GitDeTeste.Criar();

    public void Dispose() => _repo.Dispose();

    FetchEmLote Operacao() => new(_projetos, _dialogos, new ProgressoNulo(), _git);

    [Fact]
    public async Task Repositorio_atualizado_fica_ok_sem_novidades()
    {
        _projetos.Adicionar(_repo.ComoProjeto("p1"));
        _projetos.Marcar(_repo.Caminho, true);

        var r = await Operacao().ExecutarAsync(new OpcoesDeFetch());

        var item = r.Valor.Itens.Single().Resultado;
        item.Situacao.Should().Be(Situacao.Ok);
        item.Dados!.TrouxeNovidades.Should().BeFalse();
        item.Mensagem.Should().Be("Já estava atualizado");
    }

    [Fact]
    public async Task Commit_novo_no_origin_e_baixado_de_verdade()
    {
        _repo.AvancarOrigin();
        _projetos.Adicionar(_repo.ComoProjeto());
        _projetos.Marcar(_repo.Caminho, true);

        var r = await Operacao().ExecutarAsync(new OpcoesDeFetch());

        r.Valor.Itens.Single().Resultado.Dados!.TrouxeNovidades.Should().BeTrue();
        _repo.Rodar("rev-list", "--count", "main..origin/main").Should().Be("1");
        _repo.Rodar("log", "-1", "--format=%s", "main").Should().Be("Commit inicial", "fetch não mexe na branch local");
    }

    [Fact]
    public async Task Prune_remove_referencia_de_branch_apagada_no_origin()
    {
        _repo.Rodar("push", "origin", "--delete", "feature/exemplo");
        _projetos.Adicionar(_repo.ComoProjeto());
        _projetos.Marcar(_repo.Caminho, true);

        await Operacao().ExecutarAsync(new OpcoesDeFetch(Prune: true));

        _repo.Rodar("branch", "-r").Should().NotContain("feature/exemplo");
    }

    [Fact]
    public async Task Projeto_sem_remoto_vira_aviso()
    {
        _repo.Rodar("remote", "remove", "origin");
        _projetos.Adicionar(_repo.ComoProjeto());
        _projetos.Marcar(_repo.Caminho, true);

        var r = await Operacao().ExecutarAsync(new OpcoesDeFetch());

        r.Valor.Itens.Single().Resultado.Should().Match<ResultadoDoProjeto<ResultadoDeFetch>>(x =>
            x.Situacao == Situacao.Aviso && x.Mensagem == "Sem remoto configurado");
    }

    [Fact]
    public async Task Pasta_que_nao_e_repositorio_vira_erro_explicado_e_o_lote_segue()
    {
        var vazia = Path.Combine(_repo.Raiz, "vazia");
        Directory.CreateDirectory(vazia);
        _projetos.Adicionar(new Projeto("quebrado", vazia));
        _projetos.Adicionar(_repo.ComoProjeto("bom"));
        _projetos.Marcar(vazia, true);
        _projetos.Marcar(_repo.Caminho, true);

        var r = await Operacao().ExecutarAsync(new OpcoesDeFetch());

        r.Valor.Itens.Select(i => i.Resultado.Situacao).Should().Equal(Situacao.Erro, Situacao.Ok);
        r.Valor.Itens[0].Resultado.Erro!.Causa.Should().Be("Pasta não é um repositório Git");
    }

    [Fact]
    public async Task Modulo_registra_o_comando_que_roda_o_fetch_pelo_executor()
    {
        _projetos.Adicionar(_repo.ComoProjeto());
        _projetos.Marcar(_repo.Caminho, true);
        var fetch = Operacao();
        var catalogo = CatalogoDeComandos.Montar([new FetchModulo(fetch, new ExecutorDeOperacoes())]);

        await catalogo.Obter("git.fetch")!.ExecutarAsync();

        _dialogos.Resumos.Single().Oks.Should().Be(1);
    }
}
