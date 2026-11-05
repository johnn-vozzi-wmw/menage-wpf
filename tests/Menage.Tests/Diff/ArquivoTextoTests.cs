using System.Text;

namespace Menage.Tests.Diff;

public class ArquivoTextoTests
{
    [Fact]
    public void Separa_linhas_e_guarda_o_fim_de_cada_uma()
    {
        var a = ArquivoTexto.DeTexto("um\r\ndois\ntres");

        a.Linhas.Should().Equal("um", "dois", "tres");
        a.Fins.Should().Equal(FimDeLinha.CrLf, FimDeLinha.Lf, FimDeLinha.Nenhum);
        a.FimDominante.Should().Be("misto");
    }

    [Theory]
    [InlineData("a\nb\n", "LF")]
    [InlineData("a\r\nb\r\n", "CRLF")]
    [InlineData("a\r\nb", "CRLF")]
    public void Ultima_linha_sem_quebra_nao_conta_no_fim_dominante(string texto, string esperado) =>
        ArquivoTexto.DeTexto(texto).FimDominante.Should().Be(esperado);

    [Fact]
    public void Detecta_utf8_windows1252_bom_e_binario()
    {
        var latin = Codificacoes.Windows1252.GetBytes("ação\n");
        byte[] comBom = [0xEF, 0xBB, 0xBF, (byte)'a', (byte)'\n'];

        Codificacoes.Detectar(latin).CodePage.Should().Be(1252);
        Codificacoes.Detectar(comBom).CodePage.Should().Be(65001);
        Codificacoes.TemBom(comBom).Should().BeTrue();
        Codificacoes.Texto(comBom, Encoding.UTF8).Should().Be("a\n");
        Codificacoes.Binario([1, 0, 2]).Should().BeTrue();
        Codificacoes.Binario(latin).Should().BeFalse();
    }

    [Fact]
    public void Grava_de_volta_bom_crlf_da_maioria_linha_lf_e_falta_de_quebra_no_fim()
    {
        const string misto = "um\r\ndois\nação\r\n}\r\nfim sem quebra";
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(misto)];

        var formato = FormatoArquivo.De(original);

        formato.Bom.Should().BeTrue();
        formato.Crlf.Should().BeTrue();
        formato.QuebraNoFim.Should().BeFalse();
        formato.Montar(misto.Replace("\r\n", "\n").Split('\n')).Should().Equal(original);
    }

    [Fact]
    public void Arquivo_windows1252_continua_em_1252()
    {
        var original = Codificacoes.Windows1252.GetBytes("não\nsim\n");

        var formato = FormatoArquivo.De(original);

        formato.Codificacao.CodePage.Should().Be(1252);
        formato.Montar(["não", "sim"]).Should().Equal(original);
    }

    [Fact]
    public void Lista_de_alterados_le_o_status_com_nul_e_nome_antigo()
    {
        const string saida = " M src/a/B.java\0?? novo.txt\0R  pasta/novo nome.txt\0velho.txt\0UU c.java\0 D x.txt\0A  y.txt\0?? ação.txt\0!! ignorado\0";

        var lidos = ArquivoAlterado.LerStatus(saida);

        lidos.Select(a => a.Letra).Should().Equal('M', 'N', 'R', 'U', 'D', 'A', 'N');
        lidos[0].Nome.Should().Be("B.java");
        lidos[0].Pasta.Should().Be("src/a");
        lidos[2].Caminho.Should().Be("pasta/novo nome.txt");
        lidos[2].Antigo.Should().Be("velho.txt");
        lidos[3].CodigoStatus.Should().Be("UU");
        lidos[6].Caminho.Should().Be("ação.txt");
    }

    [Fact]
    public void Proxima_mudanca_anda_a_partir_do_item_na_tela_e_da_a_volta()
    {
        int[] linhas = [10, 50, 90];

        Navegacao.Proxima(linhas, -1, 0, 30, 1).Should().Be(0);
        Navegacao.Proxima(linhas, 0, 0, 30, 1).Should().Be(1);
        Navegacao.Proxima(linhas, 0, 60, 30, 1).Should().Be(2);
        Navegacao.Proxima(linhas, 2, 80, 30, 1).Should().Be(0);
        Navegacao.Proxima(linhas, 0, 0, 30, -1).Should().Be(2);
        Navegacao.Proxima(linhas, -1, 60, 30, -1).Should().Be(1);
        Navegacao.Proxima([], -1, 0, 30, 1).Should().Be(-1);
    }
}
