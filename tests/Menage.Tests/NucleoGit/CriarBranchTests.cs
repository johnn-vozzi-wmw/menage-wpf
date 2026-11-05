using Menage.Core.Operacoes.CriarBranch;

namespace Menage.Tests.NucleoGit;

public sealed class CriarBranchTests : NucleoGitBase
{
    CriarBranchEmLote Operacao() => new(Projetos, Dialogos, Progresso, GitExec, Consultas, Frameworks());

    [Fact]
    public async Task Base_no_origin_com_publicacao_acompanha_a_propria_branch()
    {
        var item = Unico(await Operacao().ExecutarAsync(new OpcoesDeCriarBranch("nova", "develop", Publicar: true)));

        item.Situacao.Should().Be(Situacao.Ok);
        item.Mensagem.Should().Contain("criada e publicada");
        Repo.BranchAtual.Should().Be("nova");
        Assunto().Should().Be("Commit de develop");
        Repo.Rodar("rev-parse", "--abbrev-ref", "@{u}").Should().Be("origin/nova", "a branch nova não acompanha a base");
        Repo.Rodar("ls-remote", "--heads", "origin", "nova").Should().Contain("refs/heads/nova");
    }

    [Fact]
    public async Task Base_local_vence_quando_nao_ha_origin_com_o_nome()
    {
        Repo.Rodar("checkout", "-b", "so-local");
        Repo.Commitar("l.txt", "l", "so local");
        Repo.Rodar("checkout", "main");

        await Operacao().ExecutarAsync(new OpcoesDeCriarBranch("nova", "so-local"));

        Assunto().Should().Be("so local");
    }

    [Fact]
    public async Task Base_tag_funciona()
    {
        Repo.Commitar("t.txt", "t", "tag 9.1");
        Repo.Rodar("tag", "9.1");
        Repo.Commitar("u.txt", "u", "depois da tag");

        await Operacao().ExecutarAsync(new OpcoesDeCriarBranch("h1", "9.1"));

        Assunto().Should().Be("tag 9.1");
    }

    [Fact]
    public async Task Base_vazia_parte_do_commit_atual()
    {
        await Operacao().ExecutarAsync(new OpcoesDeCriarBranch("h3"));

        Repo.BranchAtual.Should().Be("h3");
        Assunto().Should().Be("Commit inicial");
    }

    [Fact]
    public async Task Base_inexistente_e_erro_e_nao_troca_de_branch()
    {
        var item = Unico(await Operacao().ExecutarAsync(new OpcoesDeCriarBranch("h2", "naoexiste")));

        item.Situacao.Should().Be(Situacao.Erro);
        item.Mensagem.Should().Contain("base \"naoexiste\" não existe");
        Repo.BranchAtual.Should().Be("main");
    }

    [Fact]
    public async Task Branch_que_ja_existe_no_origin_e_erro()
    {
        var item = Unico(await Operacao().ExecutarAsync(new OpcoesDeCriarBranch("feature/exemplo")));

        item.Situacao.Should().Be(Situacao.Erro);
        item.Mensagem.Should().Contain("já existe");
    }

    [Fact]
    public async Task Nome_invalido_nao_executa_nada()
    {
        var r = await Operacao().ExecutarAsync(new OpcoesDeCriarBranch("nome invalido..x"));

        r.EhOk.Should().BeFalse();
        r.Erro.Causa.Should().Contain("Nome de branch inválido");
        (await Consultas.NomeDeBranchValidoAsync("VENDAS-1")).Should().BeTrue();
    }

    [Fact]
    public async Task Origin_barra_nome_como_base_usa_o_remoto()
    {
        Repo.Rodar("checkout", "develop");
        Repo.Commitar("d.txt", "d", "so local em develop");
        Repo.Rodar("checkout", "main");

        await Operacao().ExecutarAsync(new OpcoesDeCriarBranch("nova", "origin/develop"));

        Assunto().Should().Be("Commit de develop");
    }
}
