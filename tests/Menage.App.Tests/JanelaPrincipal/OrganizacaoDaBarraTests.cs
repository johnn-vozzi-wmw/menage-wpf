using Menage.App.Modulos.JanelaPrincipal;

namespace Menage.App.Tests.JanelaPrincipal;

public sealed class OrganizacaoDaBarraTests
{
    static readonly string[] Todas = ["Fetch", "Pull", "Sync", "Mais"];

    static OrganizacaoDaBarra Padrao() => OrganizacaoDaBarra.Padrao(Todas);

    [Fact]
    public void Tirar_e_mostrar_mudam_so_a_visibilidade()
    {
        var org = Padrao().Tirar("Pull");

        org.Visiveis.Should().Equal("Fetch", "Sync", "Mais");
        org.Mostrar("Pull").Visiveis.Should().Equal(Todas);
    }

    [Fact]
    public void Mais_nunca_sai_da_barra()
    {
        Padrao().Tirar("Mais").Visiveis.Should().Contain("Mais");
    }

    [Fact]
    public void Mover_pula_as_ocultas_e_respeita_as_pontas()
    {
        var org = Padrao().Tirar("Pull").Mover("Sync", -1);

        org.Visiveis.Should().Equal("Sync", "Fetch", "Mais");
        org.Mover("Sync", -1).Visiveis.Should().Equal("Sync", "Fetch", "Mais");
        org.Mostrar("Pull").Ordem.Should().Contain("Pull");
    }

    [Fact]
    public void Normalizar_descarta_o_que_nao_existe_e_acrescenta_o_novo()
    {
        var salva = new OrganizacaoDaBarra(["Sync", "Antiga", "Fetch"], ["Antiga", "Pull"]);

        var org = salva.Normalizar(Todas);

        org.Ordem.Should().Equal("Sync", "Fetch", "Pull", "Mais");
        org.Ocultas.Should().Equal("Pull");
    }
}
