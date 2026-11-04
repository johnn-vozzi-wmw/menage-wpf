using Menage.Core.Operacoes.CherryPick;

namespace Menage.Tests.NucleoGit;

public sealed class CherryPickTests : NucleoGitBase
{
    /// <summary>feat tem 3 commits; o primeiro já foi aplicado na main com outro hash.</summary>
    void MontarFeat()
    {
        Repo.Rodar("checkout", "-b", "feat");
        Repo.Commitar("f1.txt", "f1\n", "VENDAS-1 cria f1");
        Repo.Commitar("o.txt", "o\n", "VENDAS-2 outro");
        Repo.Commitar("f3.txt", "f3\n", "vendas-1 cria f3");
        Repo.Rodar("push", "origin", "feat");
        Repo.Rodar("checkout", "main");
        Repo.Rodar("cherry-pick", "feat~2");
    }

    [Fact]
    public async Task Previa_ignora_o_que_ja_foi_aplicado_por_patch_id_e_nao_diferencia_caixa()
    {
        MontarFeat();

        var plano = (await Preparador().PrepararAsync([Projeto], "VENDAS-1")).Single();

        plano.Falha.Should().BeNull();
        plano.Commits.Select(c => c.Assunto).Should().Equal("vendas-1 cria f3");
        plano.Commits[0].Origem.Should().Contain("feat");
        plano.Destino.Should().Be("main (branch atual)");
    }

    [Fact]
    public async Task Commit_repetido_em_duas_branches_aparece_uma_vez()
    {
        MontarFeat();
        Repo.Rodar("checkout", "-b", "hotfix", "main");
        Repo.Rodar("cherry-pick", "feat");
        Repo.Rodar("push", "origin", "hotfix");
        Repo.Rodar("checkout", "main");

        var plano = (await Preparador().PrepararAsync([Projeto], "VENDAS-1")).Single();

        plano.Commits.Should().ContainSingle(c => c.Assunto == "vendas-1 cria f3");
    }

    [Fact]
    public async Task Detached_sem_destino_e_destino_tag_viram_falha_do_plano()
    {
        Repo.Rodar("tag", "t1");
        Repo.Rodar("checkout", "--detach");

        (await Preparador().PrepararAsync([Projeto], "x")).Single().Falha.Should().Contain("detached");
        (await Preparador().PrepararAsync([Projeto], "x", "t1")).Single().Falha.Should().Contain("é uma tag");
        (await Preparador().PrepararAsync([Projeto], "x", "naoexiste")).Single().Falha.Should().Contain("não existe");
    }

    [Fact]
    public async Task Sem_commits_o_plano_fica_vazio_e_o_resumo_avisa()
    {
        var plano = (await Preparador().PrepararAsync([Projeto], "nao-existe-xyz")).Single();

        plano.Commits.Should().BeEmpty();
        Unico(await CherryPick().ExecutarAsync(new OpcoesDeCherryPick([plano]))).Situacao.Should().Be(Situacao.Aviso);
    }

    [Fact]
    public async Task Aplica_com_x_e_registra_a_origem()
    {
        MontarFeat();
        var plano = await Preparador().PrepararAsync([Projeto], "VENDAS-1");

        var item = Unico(await CherryPick().ExecutarAsync(new OpcoesDeCherryPick(plano)));

        item.Situacao.Should().Be(Situacao.Ok);
        item.Mensagem.Should().Be("1 aplicado(s), 0 pulado(s), 0 não executado(s)");
        Assunto().Should().Be("vendas-1 cria f3");
        Repo.Rodar("log", "-1", "--format=%b").Should().Contain("cherry picked from commit");
    }

    void MontarConflito()
    {
        Repo.Rodar("checkout", "-b", "feat");
        Repo.Commitar("README.md", "feat\n", "VENDAS-9 altera readme");
        Repo.Rodar("push", "origin", "feat");
        Repo.Rodar("checkout", "main");
        Repo.Commitar("README.md", "main\n", "main muda readme");
    }

    [Fact]
    public async Task Conflito_resolvido_e_continuar_conclui_o_commit()
    {
        MontarConflito();
        var plano = await Preparador().PrepararAsync([Projeto], "VENDAS-9");
        Resolvedor.Decisao = _ =>
        {
            Repo.Rodar("checkout", "--theirs", "--", "README.md");
            Repo.Rodar("add", "README.md");
            return DecisaoDeConflito.Continuar;
        };

        var item = Unico(await CherryPick().ExecutarAsync(new OpcoesDeCherryPick(plano)));

        Resolvedor.Perguntas.Should().Be(1);
        item.Situacao.Should().Be(Situacao.Ok);
        Assunto().Should().Be("VENDAS-9 altera readme");
        File.ReadAllText(Arquivo("README.md")).Should().Be("feat\n");
    }

    [Fact]
    public async Task Pular_segue_sem_o_commit_e_deixa_o_repositorio_limpo()
    {
        MontarConflito();
        var plano = await Preparador().PrepararAsync([Projeto], "VENDAS-9");
        Resolvedor.Decisao = _ => DecisaoDeConflito.Pular;

        var item = Unico(await CherryPick().ExecutarAsync(new OpcoesDeCherryPick(plano)));

        item.Situacao.Should().Be(Situacao.Aviso);
        Assunto().Should().Be("main muda readme");
        Repo.Rodar("status", "--short").Should().BeEmpty();
    }

    [Fact]
    public async Task Abortar_limpa_o_estado_e_interrompe_o_lote()
    {
        MontarConflito();
        var plano = await Preparador().PrepararAsync([Projeto], "VENDAS-9");

        var r = await CherryPick().ExecutarAsync(new OpcoesDeCherryPick(plano));

        var item = Unico(r);
        item.Situacao.Should().Be(Situacao.Erro);
        item.Mensagem.Should().Contain("1 não executado");
        r.Valor.Interrompido.Should().BeTrue();
        Repo.Rodar("status", "--short").Should().BeEmpty();
        (await Consultas.RefVerificaAsync(Repo.Caminho, "CHERRY_PICK_HEAD")).Should().BeFalse();
    }

    [Fact]
    public async Task Continuar_depois_de_abortar_por_fora_refaz_o_mesmo_commit()
    {
        MontarConflito();
        var plano = await Preparador().PrepararAsync([Projeto], "VENDAS-9");
        Resolvedor.Decisao = _ =>
        {
            if (Resolvedor.Perguntas == 1)
            {
                Repo.Rodar("cherry-pick", "--abort");
                return DecisaoDeConflito.Continuar;
            }

            Repo.Rodar("checkout", "--theirs", "--", "README.md");
            Repo.Rodar("add", "README.md");
            return DecisaoDeConflito.Continuar;
        };

        await CherryPick().ExecutarAsync(new OpcoesDeCherryPick(plano));

        Resolvedor.Perguntas.Should().Be(2);
        Assunto().Should().Be("VENDAS-9 altera readme");
    }

    [Fact]
    public async Task Destino_informado_troca_para_a_branch_e_aplica_la()
    {
        MontarFeat();
        Repo.Rodar("branch", "-D", "develop");
        var plano = await Preparador().PrepararAsync([Projeto], "VENDAS-2", "develop");

        var item = Unico(await CherryPick().ExecutarAsync(new OpcoesDeCherryPick(plano, "develop")));

        item.Situacao.Should().Be(Situacao.Ok);
        Repo.BranchAtual.Should().Be("develop");
        Assunto().Should().Be("VENDAS-2 outro");
    }
}
