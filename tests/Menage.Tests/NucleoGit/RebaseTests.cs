using Menage.Core.Operacoes.Rebase;

namespace Menage.Tests.NucleoGit;

public sealed class RebaseTests : NucleoGitBase
{
    /// <summary>develop avança no origin e a branch feat sai da main.</summary>
    void MontarFeat(string arquivo = "f.txt", string conteudo = "f\n")
    {
        Repo.Rodar("checkout", "develop");
        Repo.Commitar("base.txt", "base\n", "develop novo");
        Repo.Rodar("push", "origin", "develop");
        Repo.Rodar("checkout", "main");
        Repo.Rodar("checkout", "-b", "feat");
        Repo.Commitar(arquivo, conteudo, "feat web");
    }

    string Historico(int n) => Repo.Rodar("log", $"-{n}", "--format=%s").Replace("\n", "|", StringComparison.Ordinal);

    [Fact]
    public async Task Rebase_limpo_poe_a_branch_sobre_a_base_atualizada()
    {
        MontarFeat();

        var item = Unico(await Rebase().ExecutarAsync(new OpcoesDeRebase("develop")));

        item.Situacao.Should().Be(Situacao.Ok);
        Historico(2).Should().Be("feat web|develop novo");
        Assunto("develop").Should().Be("develop novo");
    }

    [Fact]
    public async Task Alteracoes_locais_vao_para_o_stash_e_voltam_no_final()
    {
        MontarFeat();
        Escrever("README.md", "local sujo\n");
        Escrever("untracked.txt", "u\n");

        Unico(await Rebase().ExecutarAsync(new OpcoesDeRebase("develop")));

        Historico(2).Should().Be("feat web|develop novo");
        File.ReadAllText(Arquivo("README.md")).Should().Be("local sujo\n");
        File.Exists(Arquivo("untracked.txt")).Should().BeTrue();
        Repo.Rodar("stash", "list").Should().BeEmpty();
    }

    [Fact]
    public async Task Conflito_resolvido_e_continuar_termina_o_rebase()
    {
        MontarFeat("base.txt", "conflita\n");
        Resolvedor.Decisao = _ =>
        {
            Escrever("base.txt", "resolvido\n");
            Repo.Rodar("add", "base.txt");
            return DecisaoDeConflito.Continuar;
        };

        var item = Unico(await Rebase().ExecutarAsync(new OpcoesDeRebase("develop")));

        Resolvedor.Perguntas.Should().Be(1);
        item.Mensagem.Should().Contain("com solução manual");
        Historico(2).Should().Be("feat web|develop novo");
        (await Consultas.RebaseEmAndamentoAsync(Repo.Caminho)).Should().BeFalse();
    }

    [Fact]
    public async Task Pular_aborta_so_este_projeto_e_a_branch_fica_intacta()
    {
        MontarFeat("base.txt", "conflita\n");
        var antes = Repo.Rodar("rev-parse", "HEAD");
        Resolvedor.Decisao = _ => DecisaoDeConflito.Pular;

        var r = await Rebase().ExecutarAsync(new OpcoesDeRebase("develop"));

        Unico(r).Mensagem.Should().Contain("abortado pelo usuário");
        r.Valor.Interrompido.Should().BeFalse();
        Repo.Rodar("rev-parse", "HEAD").Should().Be(antes);
        (await Consultas.RebaseEmAndamentoAsync(Repo.Caminho)).Should().BeFalse();
    }

    [Fact]
    public async Task Abortar_nao_processa_os_projetos_restantes()
    {
        MontarFeat("base.txt", "conflita\n");
        var outro = new Projeto("outro", Repo.Raiz);
        Projetos.Adicionar(outro);
        Projetos.Marcar(outro.Caminho, true);

        var r = await Rebase().ExecutarAsync(new OpcoesDeRebase("develop"));

        r.Valor.Interrompido.Should().BeTrue();
        r.Valor.NaoExecutados.Should().Equal("outro");
    }

    [Fact]
    public async Task Base_inexistente_e_erro()
    {
        MontarFeat();

        var item = Unico(await Rebase().ExecutarAsync(new OpcoesDeRebase("nao-existe")));

        item.Situacao.Should().Be(Situacao.Erro);
        item.Mensagem.Should().Contain("a branch nao-existe não existe");
    }

    [Fact]
    public async Task Ja_na_base_faz_so_o_pull_fast_forward()
    {
        Repo.AvancarOrigin();

        var item = Unico(await Rebase().ExecutarAsync(new OpcoesDeRebase("main")));

        item.Mensagem.Should().Contain("já estava em main - pull feito");
        Assunto().Should().Be("Commit remoto");
    }

    [Fact]
    public async Task Base_local_divergente_do_origin_faz_rebase_sobre_origin()
    {
        MontarFeat();
        Repo.Rodar("checkout", "develop");
        Repo.Commitar("d3.txt", "3", "develop 3");
        Repo.Rodar("push", "origin", "develop");
        Repo.Rodar("reset", "--hard", "HEAD~1");
        Repo.Commitar("local.txt", "l", "local nao publicado");
        Repo.Rodar("checkout", "feat");

        var item = Unico(await Rebase().ExecutarAsync(new OpcoesDeRebase("develop")));

        item.Situacao.Should().Be(Situacao.Aviso);
        item.Mensagem.Should().Contain("divergiu do origin");
        Historico(2).Should().Be("feat web|develop 3");
    }

    [Fact]
    public async Task Detached_e_erro()
    {
        Repo.Rodar("checkout", "--detach");

        var item = Unico(await Rebase().ExecutarAsync(new OpcoesDeRebase("main")));

        item.Mensagem.Should().Contain("detached HEAD");
    }

    [Fact]
    public async Task E_destrutivo_e_a_protecao_recusa_em_branch_protegida()
    {
        var protecao = new ExecutorComProtecao(new ExecutorDeOperacoes(), GitExec, RegrasDeProtecao.Padrao);

        var r = await protecao.ExecutarAsync(Rebase(), new OpcoesDeRebase("develop"));

        Rebase().Destrutiva.Should().BeTrue();
        r.EhOk.Should().BeFalse("a main está protegida");
    }
}
