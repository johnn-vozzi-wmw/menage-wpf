using Menage.Core.Personalizacao;

namespace Menage.Tests.Busca;

public sealed class IconesPersonalizadosTests
{
    [Fact]
    public void Icone_do_tema_vence_o_geral_e_sem_nenhum_vale_o_padrao()
    {
        var i = new IconesPersonalizados().ComIconeDoApp("*", "geral.png").ComIconeDoApp("escuro", "escuro.gif");

        i.DoApp("escuro").Should().Be("escuro.gif");
        i.DoApp("claro").Should().Be("geral.png");
        new IconesPersonalizados().DoApp("claro").Should().BeNull();
    }

    [Fact]
    public void Escopos_so_valem_com_a_caixa_marcada()
    {
        var i = new IconesPersonalizados().ComIconeDe(AlvoDeIcone.Comando, "git.fetch", "f.png").ComIconeDe(AlvoDeIcone.Grupo, "WEB", "w.gif");

        i.De(AlvoDeIcone.Comando, "git.fetch").Should().BeNull();
        (i with { AplicarNoMenuEPilulas = true }).De(AlvoDeIcone.Comando, "git.fetch").Should().Be("f.png");
        i.De(AlvoDeIcone.Grupo, "WEB").Should().BeNull();
        (i with { AplicarEmGruposEProjetos = true }).De(AlvoDeIcone.Grupo, "WEB").Should().Be("w.gif");
    }

    [Fact]
    public void Remover_nao_altera_o_original_e_caminho_vazio_apaga()
    {
        var i = new IconesPersonalizados().ComIconeDoApp("escuro", "a.png");
        var sem = i.ComIconeDoApp("escuro", "");

        i.PorTema.Should().ContainKey("escuro");
        sem.PorTema.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a.PNG", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.exe", false)]
    public void So_aceita_imagens(string caminho, bool esperado) => IconesPersonalizados.ExtensaoValida(caminho).Should().Be(esperado);

    [Fact]
    public void Aparencia_antiga_sem_icones_carrega_com_padrao()
    {
        var p = System.Text.Json.JsonSerializer.Deserialize<PreferenciasDeAparencia>("{\"Densidade\":\"Compacta\"}")!;
        p.Icones.PorTema.Should().BeEmpty();
        p.Icones.AplicarNoMenuEPilulas.Should().BeFalse();
    }
}
