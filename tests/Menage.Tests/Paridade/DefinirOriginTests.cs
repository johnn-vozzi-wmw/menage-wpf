using Menage.Core.Operacoes.Decoradores;
using Menage.Core.Operacoes.DefinirOrigin;
using Menage.Core.Operacoes.Push;
using Menage.Tests.NucleoGit;

namespace Menage.Tests.Paridade;

public sealed class DefinirOriginTests : NucleoGitBase
{
    DefinirOriginEmLote Operacao(IRegrasDeProtecao? regras = null) =>
        new(Projetos, Dialogos, Progresso, GitExec, Consultas, new EnvioParaOrigin(GitExec, regras ?? RegrasDeProtecao.Padrao));

    string Upstream() => Repo.Rodar("rev-parse", "--abbrev-ref", "@{u}");

    [Fact]
    public async Task Branch_que_ja_tem_origin_e_ignorada_com_aviso()
    {
        var item = Unico(await Operacao().ExecutarAsync(new OpcoesDeDefinirOrigin()));

        item.Situacao.Should().Be(Situacao.Aviso);
        item.Mensagem.Should().Contain("já tem origin");
    }

    [Fact]
    public async Task Branch_que_existe_no_origin_passa_a_acompanha_la()
    {
        Repo.Rodar("branch", "--unset-upstream");

        var item = Unico(await Operacao().ExecutarAsync(new OpcoesDeDefinirOrigin(Publicar: false)));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        Upstream().Should().Be("origin/main");
    }

    [Fact]
    public async Task Branch_nova_e_publicada_com_push_u()
    {
        Repo.Rodar("checkout", "-b", "feature/so-local");

        var item = Unico(await Operacao(new RegrasDeProtecao([])).ExecutarAsync(new OpcoesDeDefinirOrigin()));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        Repo.Rodar("ls-remote", "--heads", "origin", "feature/so-local").Should().NotBeEmpty();
        Upstream().Should().Be("origin/feature/so-local");
    }

    [Fact]
    public async Task Sem_publicar_a_branch_nova_so_avisa_e_nada_vai_ao_origin()
    {
        Repo.Rodar("checkout", "-b", "feature/so-local");

        var item = Unico(await Operacao().ExecutarAsync(new OpcoesDeDefinirOrigin(Publicar: false)));

        item.Situacao.Should().Be(Situacao.Aviso);
        Repo.Rodar("ls-remote", "--heads", "origin", "feature/so-local").Should().BeEmpty();
    }

    [Fact]
    public async Task Branch_protegida_nao_e_publicada()
    {
        Repo.Rodar("checkout", "-b", "release/9.0");

        var item = Unico(await Operacao().ExecutarAsync(new OpcoesDeDefinirOrigin()));

        item.Situacao.Should().Be(Situacao.Erro);
        Repo.Rodar("ls-remote", "--heads", "origin", "release/9.0").Should().BeEmpty();
    }

    [Fact]
    public async Task Detached_head_e_ignorado()
    {
        Repo.Rodar("checkout", "--detach");

        var item = Unico(await Operacao().ExecutarAsync(new OpcoesDeDefinirOrigin()));

        item.Situacao.Should().Be(Situacao.Aviso);
        item.Mensagem.Should().Contain("detached");
    }
}
