using Menage.Core.Operacoes.Checkout;

namespace Menage.Tests.NucleoGit;

public sealed class CheckoutTests : NucleoGitBase
{
    [Fact]
    public async Task Branch_so_no_origin_vira_local_acompanhando_o_remoto()
    {
        Repo.Rodar("branch", "-D", "develop");

        var item = Unico(await Checkout().ExecutarAsync(new OpcoesDeCheckout("develop")));

        item.Situacao.Should().Be(Situacao.Ok);
        item.Mensagem.Should().Contain("branch em origin");
        Repo.BranchAtual.Should().Be("develop");
        Repo.Rodar("rev-parse", "--abbrev-ref", "@{u}").Should().Be("origin/develop");
    }

    [Fact]
    public async Task Branch_local_faz_switch()
    {
        var item = Unico(await Checkout().ExecutarAsync(new OpcoesDeCheckout("develop")));

        item.Mensagem.Should().Contain("branch local");
        Repo.BranchAtual.Should().Be("develop");
    }

    [Fact]
    public async Task Branch_que_so_existe_depois_do_clone_chega_por_fetch()
    {
        Repo.Rodar("push", "origin", "main:refs/heads/nova-no-origin");
        Repo.Rodar("update-ref", "-d", "refs/remotes/origin/nova-no-origin");

        var item = Unico(await Checkout().ExecutarAsync(new OpcoesDeCheckout("nova-no-origin")));

        item.Situacao.Should().Be(Situacao.Ok);
        Repo.BranchAtual.Should().Be("nova-no-origin");
    }

    [Fact]
    public async Task So_tag_deixa_detached_com_aviso()
    {
        Repo.Rodar("tag", "v1");

        var item = Unico(await Checkout().ExecutarAsync(new OpcoesDeCheckout("v1")));

        item.Situacao.Should().Be(Situacao.Aviso);
        item.Mensagem.Should().Contain("detached HEAD");
        (await Consultas.BranchAtualAsync(Repo.Caminho)).Should().BeEmpty();
    }

    [Fact]
    public async Task Inexistente_vira_erro_depois_de_tentar_o_fetch()
    {
        var item = Unico(await Checkout().ExecutarAsync(new OpcoesDeCheckout("nao-existe")));

        item.Situacao.Should().Be(Situacao.Erro);
        item.Mensagem.Should().Contain("\"nao-existe\" não existe");
    }

    [Fact]
    public async Task Branch_local_e_tag_com_o_mesmo_nome_usa_a_branch()
    {
        Repo.Rodar("tag", "9.0");
        Repo.Rodar("checkout", "-b", "9.0");
        Repo.Commitar("x.txt", "x", "na branch");
        Repo.Rodar("checkout", "main");

        await Checkout().ExecutarAsync(new OpcoesDeCheckout("9.0"));

        (await Consultas.BranchAtualAsync(Repo.Caminho)).Should().Be("9.0");
        Assunto().Should().Be("na branch");
    }

    [Fact]
    public async Task Origin_barra_nome_usa_exatamente_a_branch_do_remoto()
    {
        Repo.Rodar("branch", "-D", "develop");

        var item = Unico(await Checkout().ExecutarAsync(new OpcoesDeCheckout("origin/develop")));

        item.Situacao.Should().Be(Situacao.Ok);
        Repo.BranchAtual.Should().Be("develop");
    }

    [Fact]
    public async Task Alteracao_local_que_bloqueia_vira_erro_e_continua_na_branch()
    {
        Repo.Rodar("checkout", "develop");
        Repo.Commitar("README.md", "outra\n", "muda README em develop");
        Repo.Rodar("checkout", "main");
        Escrever("README.md", "sujo\n");

        var item = Unico(await Checkout().ExecutarAsync(new OpcoesDeCheckout("develop")));

        item.Situacao.Should().Be(Situacao.Erro);
        item.Mensagem.Should().Contain("Checkout falhou");
        Repo.BranchAtual.Should().Be("main");
    }

    [Fact]
    public async Task Auto_stash_guarda_ao_sair_e_restaura_ao_voltar()
    {
        Escrever("README.md", "mexido\n");
        Escrever("novo.txt", "n\n");

        var ida = Unico(await Checkout().ExecutarAsync(new OpcoesDeCheckout("develop", AutoStash: true)));

        ida.Mensagem.Should().Contain("guardadas (auto-stash)");
        Repo.Rodar("stash", "list").Should().Contain("GM-AUTO|main|");
        Repo.Rodar("status", "--short").Should().BeEmpty();

        var volta = Unico(await Checkout().ExecutarAsync(new OpcoesDeCheckout("main", AutoStash: true)));

        volta.Mensagem.Should().Contain("foram restauradas");
        Repo.Rodar("stash", "list").Should().BeEmpty();
        File.ReadAllText(Arquivo("README.md")).Should().Be("mexido\n");
        File.Exists(Arquivo("novo.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Sem_a_opcao_o_auto_stash_nao_age()
    {
        Escrever("novo.txt", "n\n");

        await Checkout().ExecutarAsync(new OpcoesDeCheckout("develop"));

        Repo.Rodar("stash", "list").Should().BeEmpty();
        File.Exists(Arquivo("novo.txt")).Should().BeTrue();
    }
}
