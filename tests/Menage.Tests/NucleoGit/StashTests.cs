using Menage.Core.Operacoes.Stash;

namespace Menage.Tests.NucleoGit;

public sealed class StashTests : NucleoGitBase
{
    StashEmLote Operacao() => new(Projetos, Dialogos, Progresso, GitExec, Consultas);

    OpcoesDeStash Opcoes(string nome, bool naoVersionados = false) => new([new ItemDeStash(Projeto, nome)], naoVersionados);

    [Fact]
    public async Task Previa_so_lista_projetos_com_alteracoes()
    {
        var limpo = Operacao();
        (await limpo.ListarPendenciasAsync([Projeto])).Should().BeEmpty();

        Escrever("README.md", "mod\n");
        Escrever("novo.txt", "n\n");

        var pendencias = await limpo.ListarPendenciasAsync([Projeto]);
        pendencias.Single().Branch.Should().Be("main");
        pendencias.Single().Arquivos.Should().HaveCount(2);
    }

    [Fact]
    public async Task Stash_com_nome_guarda_o_versionado_e_deixa_o_novo()
    {
        Escrever("README.md", "mod\n");
        Escrever("novo.txt", "n\n");

        var item = Unico(await Operacao().ExecutarAsync(Opcoes("VENDAS-1 wip")));

        item.Situacao.Should().Be(Situacao.Ok);
        Repo.Rodar("stash", "list").Should().Contain("VENDAS-1 wip");
        Repo.Rodar("status", "--short").Should().Be("?? novo.txt");
    }

    [Fact]
    public async Task So_arquivo_novo_sem_a_opcao_avisa_que_nada_foi_guardado()
    {
        Escrever("novo.txt", "n\n");

        var item = Unico(await Operacao().ExecutarAsync(Opcoes("so novo")));

        item.Situacao.Should().Be(Situacao.Aviso);
        item.Mensagem.Should().Contain("nada guardado");
    }

    [Fact]
    public async Task Com_a_opcao_o_arquivo_novo_tambem_e_guardado()
    {
        Escrever("novo.txt", "n\n");

        await Operacao().ExecutarAsync(Opcoes("geral", naoVersionados: true));

        Repo.Rodar("stash", "list").Should().Contain("geral");
        Repo.Rodar("status", "--short").Should().BeEmpty();
    }

    [Fact]
    public async Task Nome_vazio_pula_o_projeto()
    {
        Escrever("README.md", "mod\n");

        var item = Unico(await Operacao().ExecutarAsync(Opcoes("  ")));

        item.Mensagem.Should().Contain("pulado");
        Repo.Rodar("stash", "list").Should().BeEmpty();
    }

    [Fact]
    public async Task Stash_pop_devolve_e_remove_da_lista_e_apply_mantem()
    {
        Escrever("README.md", "mod\n");
        await Operacao().ExecutarAsync(Opcoes("um"));
        var pop = new StashPopEmLote(Projetos, Dialogos, Progresso, GitExec);

        var entradas = await pop.ListarAsync([Projeto]);
        var apply = Unico(await pop.ExecutarAsync(new OpcoesDeStashPop(entradas, Manter: true)));

        apply.Mensagem.Should().Contain("mantidos na lista");
        File.ReadAllText(Arquivo("README.md")).Should().Be("mod\n");
        Repo.Rodar("stash", "list").Should().NotBeEmpty();

        Repo.Rodar("checkout", "--", "README.md");
        Unico(await pop.ExecutarAsync(new OpcoesDeStashPop(entradas)));

        File.ReadAllText(Arquivo("README.md")).Should().Be("mod\n");
        Repo.Rodar("stash", "list").Should().BeEmpty();
    }
}
