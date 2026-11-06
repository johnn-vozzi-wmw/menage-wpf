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

    static readonly string[] Temas = ["escuro", "claro", "azul"];

    [Theory]
    [InlineData(10, 50)]
    [InlineData(75, 75)]
    [InlineData(500, 200)]
    public void Zoom_e_limitado_entre_50_e_200(int pedido, int esperado) =>
        new IconesPersonalizados().ComIconeDoApp("escuro", "a.png").ComZoomDoApp("escuro", pedido).ZoomDoApp("escuro").Should().Be(esperado);

    [Fact]
    public void Zoom_100_nao_ocupa_espaco_e_sem_icone_e_ignorado()
    {
        var i = new IconesPersonalizados().ComIconeDoApp("escuro", "a.png").ComZoomDoApp("escuro", 150).ComZoomDoApp("escuro", 100);
        i.ZoomPorTema.Should().BeEmpty();
        new IconesPersonalizados().ComZoomDoApp("escuro", 150).ZoomPorTema.Should().BeEmpty();
    }

    [Fact]
    public void Zoom_acompanha_o_icone_que_vale_para_o_tema()
    {
        var i = new IconesPersonalizados().ComIconeDoApp("*", "g.png").ComZoomDoApp("*", 60).ComIconeDoApp("claro", "c.png").ComZoomDoApp("claro", 180);

        i.ZoomDoApp("claro").Should().Be(180);
        i.ZoomDoApp("escuro").Should().Be(60, "o tema sem ícone próprio usa o geral e o zoom dele");
        new IconesPersonalizados().ZoomDoApp("escuro").Should().Be(100);
    }

    [Fact]
    public void Trocar_o_icone_mantem_o_zoom_e_remover_apaga_os_dois()
    {
        var i = new IconesPersonalizados().ComIconeDoApp("escuro", "a.png").ComZoomDoApp("escuro", 140);
        i.ComIconeDoApp("escuro", "b.png").ZoomDoApp("escuro").Should().Be(140);
        i.ComIconeDoApp("escuro", null).ZoomPorTema.Should().BeEmpty();
        i.ZoomPorTema.Should().ContainKey("escuro", "o original não muda");
    }

    [Fact]
    public void Usar_em_todos_copia_icone_e_zoom_para_os_outros_e_nao_toca_no_geral()
    {
        var i = new IconesPersonalizados().ComIconeDoApp("escuro", "a.gif").ComZoomDoApp("escuro", 120)
            .ComIconeDoApp("claro", "velho.png").ComZoomDoApp("claro", 70);

        var r = i.UsarEmTodos("escuro", [.. Temas, IconesPersonalizados.TodosOsTemas]);

        foreach (var id in Temas)
        {
            r.DoApp(id).Should().Be("a.gif");
            r.ZoomDoApp(id).Should().Be(120);
        }

        r.PorTema.Should().NotContainKey(IconesPersonalizados.TodosOsTemas);
        i.UsarEmTodos("azul", Temas).Should().BeSameAs(i, "origem sem ícone: nada muda");
    }

    [Fact]
    public void Preferencias_serializadas_guardam_o_zoom_e_aparencia_antiga_abre_com_100()
    {
        var p = new PreferenciasDeAparencia { Icones = new IconesPersonalizados().ComIconeDoApp("escuro", "a.png").ComZoomDoApp("escuro", 90) };
        var volta = System.Text.Json.JsonSerializer.Deserialize<PreferenciasDeAparencia>(System.Text.Json.JsonSerializer.Serialize(p))!;
        volta.Icones.ZoomDoApp("escuro").Should().Be(90);

        System.Text.Json.JsonSerializer.Deserialize<PreferenciasDeAparencia>("{\"Icones\":{\"PorTema\":{\"escuro\":\"a.png\"}}}")!
            .Icones.ZoomDoApp("escuro").Should().Be(100);
    }

    [Fact]
    public void Aparencia_antiga_sem_icones_carrega_com_padrao()
    {
        var p = System.Text.Json.JsonSerializer.Deserialize<PreferenciasDeAparencia>("{\"Densidade\":\"Compacta\"}")!;
        p.Icones.PorTema.Should().BeEmpty();
        p.Icones.AplicarNoMenuEPilulas.Should().BeFalse();
    }
}
