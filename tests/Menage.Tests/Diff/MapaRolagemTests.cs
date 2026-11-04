namespace Menage.Tests.Diff;

public class MapaRolagemTests
{
    const double H = 15;
    const double Tela = 600;

    /// <summary>100 linhas iguais, um bloco de 10 linhas só na direita, mais 100 iguais.</summary>
    static MapaRolagem Insercao() => new([[100, 100], [100, 110]], [200, 210], H, Tela);

    [Fact]
    public void Todos_os_paineis_chegam_ao_comeco_e_ao_fim_juntos()
    {
        var m = Insercao();

        m.Y(0, 0).Should().Be(0);
        m.Y(1, 0).Should().Be(0);
        m.Y(0, m.Maximo).Should().Be((200 * H) - Tela);
        m.Y(1, m.Maximo).Should().Be((210 * H) - Tela);
    }

    [Fact]
    public void Nenhum_painel_anda_para_tras_em_nenhum_passo()
    {
        var m = Insercao();
        var anterior = new[] { -1.0, -1.0 };

        for (var u = 0.0; u <= m.Maximo; u += 0.5)
        {
            for (var p = 0; p < 2; p++)
            {
                m.Y(p, u).Should().BeGreaterThanOrEqualTo(anterior[p]);
                anterior[p] = m.Y(p, u);
            }
        }
    }

    [Fact]
    public void Trecho_igual_anda_igual_nos_dois_lados()
    {
        var m = Insercao();

        var (a, b) = (500.0, 520.0);
        (m.Y(0, b) - m.Y(0, a)).Should().BeApproximately(m.Y(1, b) - m.Y(1, a), 0.001);
    }

    [Fact]
    public void Em_trecho_igual_a_diferenca_entre_paineis_e_um_numero_inteiro_de_linhas()
    {
        var m = new MapaRolagem([[1000, 1000], [1000, 1040], [2000, 2040], [2015, 2040]], [6000, 6025], H, Tela);

        for (var u = 0.0; u <= m.Maximo; u += 7.3)
        {
            var meio = (m.Y(0, u) + (Tela / 2)) / H;
            if (meio is < 990 or (>= 1000 and < 1010) or (>= 2000 and < 2030)) continue;
            var diferenca = m.Y(1, u) - m.Y(0, u);
            (diferenca % H).Should().BeApproximately(0, 0.0001, $"em u={u} a diferença foi {diferenca}");
        }
    }

    [Fact]
    public void Painel_mais_curto_que_a_tela_fica_parado_e_o_outro_rola()
    {
        var m = new MapaRolagem([[10, 500]], [10, 500], H, Tela);

        m.Maximo.Should().Be((500 * H) - Tela);
        m.Y(0, m.Maximo / 2).Should().Be(0);
        m.Y(1, m.Maximo).Should().Be((500 * H) - Tela);
    }

    [Fact]
    public void Conteudo_so_a_direita_rola_a_direita_inteira()
    {
        var m = new MapaRolagem([[0, 0], [0, 800]], [0, 800], H, Tela);

        m.Y(0, m.Maximo).Should().Be(0);
        m.Y(1, m.Maximo).Should().Be((800 * H) - Tela);
        m.Y(1, m.Maximo / 2).Should().BeApproximately(m.Maximo / 2, 1);
    }

    [Fact]
    public void Tudo_cabe_na_tela_nao_ha_rolagem()
    {
        var m = new MapaRolagem([], [5, 7], H, Tela);

        m.Maximo.Should().Be(0);
        m.Y(1, 100).Should().Be(0);
    }

    [Fact]
    public void Inverso_volta_a_mesma_posicao()
    {
        var m = Insercao();

        foreach (var u in new[] { 0.0, 100, 700, 1500, m.Maximo })
            m.Y(1, m.UDeY(1, m.Y(1, u))).Should().BeApproximately(m.Y(1, u), 0.001);
    }

    [Fact]
    public void Tres_paineis_usam_o_mesmo_mapa()
    {
        var m = new MapaRolagem([[50, 50, 50], [80, 50, 51]], [130, 100, 101], H, Tela);

        m.Altura.Should().Be(130 * H);
        m.Y(2, m.Maximo).Should().Be((101 * H) - Tela);
    }
}
