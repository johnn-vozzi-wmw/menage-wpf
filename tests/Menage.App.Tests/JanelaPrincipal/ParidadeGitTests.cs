using Menage.App.Modulos.JanelaPrincipal;
using Menage.Core.Interacao;
using Menage.Core.Projetos;
using Menage.TestKit;

namespace Menage.App.Tests.JanelaPrincipal;

public sealed class ParidadeGitTests
{
    static ConflitoDoGit Conflito() =>
        new("Merge", new Projeto("lavenderecore", @"C:\x\core", []), "Conflito ao mesclar develop", "UU a.txt", null, "Já resolvi", "Pular este projeto", "Abortar tudo");

    [Fact]
    public void Barra_tem_Sync_como_pilula_e_Status_no_menu_Mais()
    {
        BarraDeAcoesViewModel.Definicoes.Select(d => d.Texto).Take(4).Should().Equal("Fetch", "Pull", "Sync", "Checkout");
        BarraDeAcoesViewModel.Definicoes.Single(d => d.Texto == "Stash").IdsDoMenu.Should().NotContain("git.sync");
        BarraDeAcoesViewModel.Definicoes.Single(d => d.Texto == "Mais").IdsDoMenu.Should().Contain("git.status");
    }

    [Theory]
    [InlineData(DecisaoDeConflito.Continuar)]
    [InlineData(DecisaoDeConflito.Pular)]
    [InlineData(DecisaoDeConflito.Abortar)]
    public async Task Resolvedor_devolve_a_decisao_da_janela(DecisaoDeConflito decisao)
    {
        var janelas = new JanelasFalsas { RespostaDoDialogo = decisao };

        var resposta = await new ResolvedorDeConflitosWpf(janelas).DecidirAsync(Conflito());

        resposta.Should().Be(decisao);
        janelas.Abertas.Should().ContainSingle().Which.Id.Should().Be(ResolvedorDeConflitosWpf.JanelaId);
    }

    [Fact]
    public async Task Resolvedor_sem_resposta_aborta_o_caminho_seguro()
    {
        var resposta = await new ResolvedorDeConflitosWpf(new JanelasFalsas { RespostaDoDialogo = null }).DecidirAsync(Conflito());

        resposta.Should().Be(DecisaoDeConflito.Abortar);
    }
}
