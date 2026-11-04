namespace Menage.Tests.Diff;

public class ConstrutorDiffTests
{
    static readonly ConstrutorDiff _construtor = new();

    static ModeloDiff Comparar(string a, string b, bool bomEsq = false, bool bomDir = false) =>
        _construtor.Construir(ArquivoTexto.DeTexto(a, temBom: bomEsq), ArquivoTexto.DeTexto(b, temBom: bomDir));

    static string Linhas(int n, string fim = "\n") => string.Concat(Enumerable.Range(1, n).Select(i => $"linha {i}{fim}"));

    [Fact]
    public void Separa_bom_fim_de_linha_e_espaco_da_mudanca_real()
    {
        var antes = Linhas(40, "\r\n");
        var depois = antes.Replace("linha 10\r\n", "linha 10\n").Replace("linha 20\r\n", "linha VINTE\r\n").Replace("linha 30\r\n", "linha  30\r\n");

        var m = Comparar(antes, depois, bomDir: true);

        m.Segmentos.Count(s => !s.Igual && !s.SoInvisivel).Should().Be(1);
        m.SoFimDeLinha.Should().Be(1);
        m.SoEspaco.Should().Be(1);
        m.GanhouBom.Should().BeTrue();
        m.Esquerda!.FimDominante.Should().Be("CRLF");
        m.Direita!.FimDominante.Should().Be("misto");
    }

    [Fact]
    public void Destaca_so_a_palavra_que_mudou()
    {
        var m = Comparar(Linhas(40), Linhas(40).Replace("linha 20\n", "linha VINTE\n"));

        var par = m.Segmentos.Single(s => !s.Igual).Pares.Single();
        par.PalavrasEsq.Should().ContainSingle();
        par.PalavrasDir.Should().ContainSingle();
        var t = par.PalavrasDir[0];
        m.Dir[par.Dir].Substring(t.Inicio, t.Tamanho).Should().Be("VINTE");
    }

    [Fact]
    public void Arquivo_inteiro_que_so_mudou_o_fim_de_linha_continua_alinhado()
    {
        var m = Comparar(Linhas(5000, "\r\n"), Linhas(5000));

        m.Segmentos.Should().OnlyContain(s => s.SoInvisivel);
        m.SoFimDeLinha.Should().Be(5000);
    }

    [Fact]
    public void Reindentar_o_arquivo_nao_vira_mudanca_real()
    {
        var m = Comparar("\tum\n\tdois\n", "    um\n    dois\n");

        m.SoEspaco.Should().Be(2);
        m.Segmentos.Should().OnlyContain(s => s.SoInvisivel);
    }

    [Fact]
    public void Chave_inserida_nao_casa_com_a_chave_antiga_de_outra_indentacao()
    {
        var m = Comparar("if (a) {
    b();
}
", "if (a) {
    if (c) {
        d();
    }
    b();
}
");

        m.SoEspaco.Should().Be(0);
        m.Segmentos.Single(s => !s.Igual).DLen.Should().Be(3);
    }

    [Fact]
    public void Reindentar_com_linha_nova_no_meio_continua_achando_as_reindentadas()
    {
        var m = Comparar("	um
	dois
	tres
", "    um
    NOVA
    dois
    tres
");

        m.SoEspaco.Should().Be(3);
        m.Segmentos.Single(s => !s.SoInvisivel && !s.Igual).DLen.Should().Be(1);
    }

    [Fact]
    public void Falta_de_quebra_no_fim_e_mudanca_de_fim_de_linha()
    {
        var m = Comparar("a\nb\n", "a\nb");

        m.SoFimDeLinha.Should().Be(1);
        NotasDiff.Nota(m, m.Segmentos.Single(s => s.SoInvisivel).Pares[0]).Should().Be("fim de linha LF → sem quebra");
    }

    [Fact]
    public void Primeira_linha_com_bom_novo_e_classificada_como_bom()
    {
        var m = Comparar("a\nb\n", "a\nb\n", bomDir: true);

        m.Segmentos.Single(s => s.SoInvisivel).Pares[0].Tipo.Should().Be(Trivial.Bom);
        NotasDiff.Aviso(m).Should().Contain("ganhou BOM");
    }

    [Fact]
    public void Codificacao_diferente_nos_lados_nao_vira_mudanca_de_texto()
    {
        var antes = ArquivoTexto.De(Codificacoes.Windows1252.GetBytes("ação\n"));
        var depois = ArquivoTexto.De(Codificacoes.Utf8SemBom.GetBytes("ação\n"));

        var m = _construtor.Construir(antes, depois);

        m.Segmentos.Should().OnlyContain(s => s.Igual);
        m.MudouCodificacao.Should().BeTrue();
        NotasDiff.Aviso(m).Should().Contain("Windows-1252 → UTF-8");
    }

    [Fact]
    public void Arquivo_novo_e_excluido_ocupam_um_lado_so()
    {
        var novo = _construtor.Construir(null, ArquivoTexto.DeTexto("a\nb\n"));
        var excluido = _construtor.Construir(ArquivoTexto.DeTexto("a\nb\n"), null);

        novo.Novo.Should().BeTrue();
        novo.Esq.Should().BeEmpty();
        novo.Dir.Should().HaveCount(2);
        excluido.Excluido.Should().BeTrue();
        excluido.Dir.Should().BeEmpty();
    }

    [Fact]
    public void Segmentos_cobrem_os_dois_arquivos_sem_buracos()
    {
        var m = Comparar("a\nb\nc\nd\ne\n", "a\nX\nY\nc\ne\nf\n");

        var (e, d) = (0, 0);
        foreach (var s in m.Segmentos)
        {
            s.EIni.Should().Be(e);
            s.DIni.Should().Be(d);
            (e, d) = (e + s.ELen, d + s.DLen);
        }
        e.Should().Be(m.Esq.Count);
        d.Should().Be(m.Dir.Count);
    }

    [Fact]
    public void Linha_longa_com_palavra_trocada_no_fim_acha_o_trecho()
    {
        var longa = string.Join(' ', Enumerable.Range(0, 3000).Select(i => "campo" + i));

        var m = Comparar(longa + " fim\n", longa + " FIM\n");

        var t = m.Segmentos.Single(s => !s.Igual).Pares[0].PalavrasDir.Single();
        m.Dir[0].Substring(t.Inicio, t.Tamanho).Should().Be("FIM");
    }
}
