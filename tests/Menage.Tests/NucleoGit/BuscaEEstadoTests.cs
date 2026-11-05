using Menage.Core.Estado;
using Menage.Core.Historico;
using Menage.Core.Operacoes.BuscarCommit;

namespace Menage.Tests.NucleoGit;

public sealed class BuscaEEstadoTests : NucleoGitBase
{
    BuscarCommitEmLote Busca() => new(Projetos, Dialogos, Progresso, GitExec, new LeitorDeHistorico(GitExec));

    [Fact]
    public async Task Busca_por_mensagem_sem_diferenciar_caixa_e_mostra_as_branches()
    {
        Repo.Rodar("checkout", "-b", "f1");
        Repo.Commitar("a.txt", "a", "VENDAS-55 ajuste");

        var dados = Unico(await Busca().ExecutarAsync(new OpcoesDeBuscaDeCommit("vendas-55"))).Dados!;

        var achado = dados.Achados.Single();
        achado.Commit.Assunto.Should().Be("VENDAS-55 ajuste");
        achado.Branches.Should().Contain("f1");
    }

    [Fact]
    public async Task Busca_por_hash_acha_o_commit_exato()
    {
        var hash = Repo.Rodar("rev-parse", "HEAD");

        var dados = Unico(await Busca().ExecutarAsync(new OpcoesDeBuscaDeCommit(hash[..10]))).Dados!;

        dados.Achados.Single().Commit.Hash.Should().Be(hash);
    }

    [Fact]
    public async Task Busca_sem_resultado_e_ok_e_vazia()
    {
        var item = Unico(await Busca().ExecutarAsync(new OpcoesDeBuscaDeCommit("nao-existe-xyz")));

        item.Situacao.Should().Be(Situacao.Ok);
        item.Dados!.Achados.Should().BeEmpty();
    }

    [Fact]
    public async Task Termo_vazio_nao_executa()
    {
        (await Busca().ExecutarAsync(new OpcoesDeBuscaDeCommit(" "))).EhOk.Should().BeFalse();
    }

    LeitorDeEstado Leitor() => new(GitExec);

    [Fact]
    public async Task Estado_separa_alteracao_versionada_de_arquivo_novo()
    {
        Escrever("README.md", "x\n");
        Escrever("n.txt", "x\n");

        var estado = await Leitor().LerAsync(Projeto);

        estado.Branch.Should().Be("main");
        estado.Alteracoes.Should().Be(1);
        estado.NaoVersionados.Should().Be(1);
    }

    [Fact]
    public async Task So_arquivo_novo_nao_conta_como_alteracao()
    {
        Escrever("so-novo.txt", "x\n");

        var estado = await Leitor().LerAsync(Projeto);

        estado.Alteracoes.Should().Be(0);
        estado.NaoVersionados.Should().Be(1);
    }

    [Fact]
    public async Task Estado_detached_traz_o_head_curto()
    {
        Repo.Rodar("checkout", "--detach");

        var estado = await Leitor().LerAsync(Projeto);

        estado.Detached.Should().BeTrue();
        estado.Head.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Estado_conta_commits_a_frente_e_atras_do_origin()
    {
        Repo.Commitar("a1.txt", "a", "a1");
        Repo.AvancarOrigin();
        await Consultas.AtualizarRefsAsync(Repo.Caminho);

        var estado = await Leitor().LerAsync(Projeto);

        estado.TemUpstream.Should().BeTrue();
        (estado.Ahead, estado.Behind).Should().Be((1, 1));
    }

    [Fact]
    public async Task Estado_mostra_operacao_em_andamento()
    {
        Repo.Rodar("checkout", "-b", "feat");
        Repo.Commitar("README.md", "feat\n", "feat");
        Repo.Rodar("checkout", "main");
        Repo.Commitar("README.md", "main\n", "main");
        var cherry = await GitExec.ExecutarBrutoAsync(Git.Em(Repo.Caminho).Comando("cherry-pick").Argumento("feat"));
        cherry.Sucesso.Should().BeFalse();

        (await Leitor().LerAsync(Projeto)).Operacao.Should().Be("cherry-pick em andamento");
    }

    [Fact]
    public async Task Pasta_que_nao_e_repositorio_vira_falha_e_a_leitura_em_lote_segue()
    {
        var vazia = new Projeto("vazia", Path.Combine(Repo.Raiz, "vazia"));

        var estados = await Leitor().LerTodosAsync([vazia, Projeto]);

        estados[0].Falha.Should().Be("pasta não encontrada");
        estados[1].Branch.Should().Be("main");
    }

    [Fact]
    public async Task Sugestoes_de_branches_nao_trazem_head_e_as_bases_trazem_origin_e_tags()
    {
        Repo.Rodar("tag", "v1");
        var sugestoes = new SugestoesDeBranches(GitExec);

        var branches = await sugestoes.BranchesAsync([Projeto]);
        var bases = await sugestoes.BasesAsync([Projeto]);

        branches.Should().Contain(["main", "develop"]).And.NotContain("HEAD");
        bases.Should().Contain(["origin/develop", "v1"]);
    }
}
