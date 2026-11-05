using Menage.Core.Estado;
using Menage.Core.Operacoes.Checkout;
using Menage.Core.Operacoes.Decoradores;
using Menage.Core.Operacoes.Diagnostico;
using Menage.Core.Operacoes.Ferramentas;
using Menage.Tests.NucleoGit;

namespace Menage.Tests.OperacoesLote;

public sealed class FerramentasTests : NucleoGitBase
{
    [Fact]
    public async Task Tag_em_massa_cria_envia_e_nunca_move_tag_existente()
    {
        var op = new TagEmMassaEmLote(Projetos, Dialogos, Progresso, GitExec, Consultas);

        var primeira = Unico(await op.ExecutarAsync(new OpcoesDeTag("v1.0", "release 1.0")));
        primeira.Situacao.Should().Be(Situacao.Ok, primeira.Mensagem);
        Repo.Rodar("ls-remote", "--tags", "origin", "v1.0").Should().NotBeEmpty();
        var antes = Repo.Rodar("rev-parse", "v1.0^{commit}");

        Repo.Commitar("novo.txt", "n\n", "outro commit");
        var segunda = Unico(await op.ExecutarAsync(new OpcoesDeTag("v1.0")));

        segunda.Situacao.Should().Be(Situacao.Erro);
        Repo.Rodar("rev-parse", "v1.0^{commit}").Should().Be(antes);
    }

    [Fact]
    public async Task Git_doctor_com_stash_de_seguranca_descarta_e_deixa_recuperavel()
    {
        Escrever("README.md", "alterado\n");
        var op = new GitDoctorEmLote(Projetos, Dialogos, Progresso, GitExec, Consultas);

        var item = Unico(await op.ExecutarAsync(new OpcoesDoGitDoctor()));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        Repo.Rodar("status", "--porcelain").Should().BeEmpty();
        Repo.Rodar("stash", "list").Should().Contain("Menage Git Doctor");
        Repo.Rodar("stash", "show", "-p", "stash@{0}").Should().Contain("+alterado");
    }

    [Fact]
    public async Task Git_doctor_nao_roda_sem_confirmacao_e_nao_apaga_novos_por_padrao()
    {
        Escrever("README.md", "alterado\n");
        Escrever("novo.txt", "n\n");
        Dialogos.RespostaDaConfirmacao = false;
        var op = new GitDoctorEmLote(Projetos, Dialogos, Progresso, GitExec, Consultas);

        (await op.ExecutarAsync(new OpcoesDoGitDoctor())).EhOk.Should().BeFalse();
        Repo.Rodar("status", "--porcelain").Should().Contain("README.md");

        Dialogos.RespostaDaConfirmacao = true;
        Unico(await op.ExecutarAsync(new OpcoesDoGitDoctor()));
        File.Exists(Arquivo("novo.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Push_em_varios_remotos_envia_para_os_que_existem_e_aponta_o_que_falta()
    {
        var outro = Path.Combine(Repo.Raiz, "backup.git");
        Repo.Rodar("init", "--bare", "-b", "main", outro);
        Repo.Rodar("remote", "add", "backup", outro);
        Repo.Rodar("checkout", "-b", "feat/m");
        var op = new PushMultiRemotoEmLote(Projetos, Dialogos, Progresso, GitExec, Consultas, RegrasDeProtecao.Padrao);

        var item = Unico(await op.ExecutarAsync(new OpcoesDePushMulti(["backup", "inexistente"])));

        item.Situacao.Should().Be(Situacao.Erro);
        item.Dados!.Enviados.Should().Equal("backup");
        item.Mensagem.Should().Contain("inexistente");
        Repo.Rodar("ls-remote", "--heads", "backup", "feat/m").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Push_em_varios_remotos_respeita_a_protecao()
    {
        var op = new PushMultiRemotoEmLote(Projetos, Dialogos, Progresso, GitExec, Consultas, RegrasDeProtecao.Padrao);

        var item = Unico(await op.ExecutarAsync(new OpcoesDePushMulti(["origin"])));

        item.Situacao.Should().Be(Situacao.Aviso);
        item.Mensagem.Should().Contain("protegida");
    }

    [Fact]
    public async Task Divergencia_e_nao_enviados_enxergam_commits_de_cada_lado()
    {
        Repo.AvancarOrigin();
        Repo.Commitar("local.txt", "l\n", "so local");
        var estados = new LeitorDeEstado(GitExec, Consultas);

        var diverg = Unico(await new DivergenciaEmLote(Projetos, Dialogos, Progresso, GitExec, estados).ExecutarAsync(new OpcoesDeDivergencia()));
        var nao = Unico(await new CommitsNaoEnviadosEmLote(Projetos, Dialogos, Progresso, GitExec).ExecutarAsync(new OpcoesDeNaoEnviados()));

        diverg.Mensagem.Should().Contain("divergiu");
        diverg.Dados!.Linhas.Should().Contain(l => l.Contains("so local", StringComparison.Ordinal)).And.Contain(l => l.Contains("Commit remoto", StringComparison.Ordinal));
        nao.Dados!.Linhas.Should().Contain(l => l.StartsWith("main:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Arquivos_alterados_filtra_por_situacao_e_texto()
    {
        Escrever("README.md", "x\n");
        Escrever("novo.txt", "n\n");
        var op = new ArquivosAlteradosEmLote(Projetos, Dialogos, Progresso, GitExec);

        var todos = Unico(await op.ExecutarAsync(new OpcoesDeArquivosAlterados()));
        var novos = Unico(await op.ExecutarAsync(new OpcoesDeArquivosAlterados(Situacoes: "?")));
        var porTexto = Unico(await op.ExecutarAsync(new OpcoesDeArquivosAlterados("readme")));

        todos.Dados!.Linhas.Should().HaveCount(2);
        novos.Dados!.Linhas.Should().ContainSingle().Which.Should().Contain("novo.txt");
        porTexto.Dados!.Linhas.Should().ContainSingle().Which.Should().Contain("README.md");
    }

    [Fact]
    public async Task Mudar_a_branch_de_todos_ignora_a_marcacao()
    {
        Projetos.Marcar(Projeto.Caminho, false);

        var resultado = await Checkout().ExecutarAsync(new OpcoesDeCheckout("develop", TodosOsProjetos: true));

        Unico(resultado).Situacao.Should().Be(Situacao.Ok);
        Repo.BranchAtual.Should().Be("develop");
    }

    [Theory]
    [InlineData("git@github.com:wmw/app.git", "https://github.com/wmw/app")]
    [InlineData("https://user:senha@host.com/org/repo.git", "https://host.com/org/repo")]
    [InlineData("ssh://git@bitbucket.org/wmw/app.git", "https://bitbucket.org/wmw/app")]
    public void Url_do_repositorio_e_normalizada_sem_credenciais(string remoto, string esperado) =>
        CopiaDosMarcados.NormalizarUrl(remoto).Should().Be(esperado);

    [Fact]
    public void Link_de_pr_segue_o_servidor()
    {
        CopiaDosMarcados.LinkDePr("https://github.com/wmw/app", "feat/x").Should().Be("https://github.com/wmw/app/pull/new/feat/x");
        CopiaDosMarcados.LinkDePr("https://bitbucket.org/wmw/app", "feat/x").Should().Contain("pull-requests/new?source=feat%2Fx");
    }
}
