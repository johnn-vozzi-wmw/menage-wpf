using Menage.App.Controles;

namespace Menage.App.Tests;

public class CalculoDaBarraTests
{
    static readonly double[] _completas = [100, 100, 100];
    static readonly double[] _compactas = [40, 40, 40];

    static LayoutDaBarra Calcular(double largura) => CalculoDaBarra.Calcular(_completas, _compactas, largura, 32, 6);

    [Fact]
    public void Cabendo_com_texto_divide_a_sobra_e_encosta_na_borda()
    {
        var layout = Calcular(612);

        layout.Modo.Should().Be(ModoDaBarra.Capsula);
        layout.Posicoes.Should().OnlyContain(p => p.Largura == 200);
        var ultima = layout.Posicoes[^1];
        (ultima.X + ultima.Largura).Should().Be(612);
    }

    [Fact]
    public void Exatamente_no_limite_ainda_e_capsula() => Calcular(312).Modo.Should().Be(ModoDaBarra.Capsula);

    [Fact]
    public void Um_pixel_a_menos_vira_so_icone() => Calcular(311).Modo.Should().Be(ModoDaBarra.SoIcone);

    [Fact]
    public void Sem_espaco_nem_para_os_icones_quebra_em_linhas()
    {
        var layout = Calcular(90);

        layout.Modo.Should().Be(ModoDaBarra.Linhas);
        layout.Posicoes.Select(p => p.Linha).Should().Equal(0, 0, 1);
        layout.Posicoes[2].Y.Should().Be(38);
        layout.Altura.Should().Be(70);
    }

    [Fact]
    public void Mais_estreita_que_uma_pilula_fica_uma_por_linha() =>
        Calcular(30).Posicoes.Select(p => p.Linha).Should().Equal(0, 1, 2);

    [Fact]
    public void Largura_infinita_usa_o_tamanho_natural()
    {
        var layout = Calcular(double.PositiveInfinity);

        layout.Modo.Should().Be(ModoDaBarra.Capsula);
        layout.Largura.Should().Be(312);
    }

    [Fact]
    public void Barra_vazia_nao_ocupa_altura() =>
        CalculoDaBarra.Calcular([], [], 300, 32, 6).Altura.Should().Be(0);
}
