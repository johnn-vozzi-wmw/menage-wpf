namespace Menage.Tests.Diff;

public class MontadorVistaTests
{
    static readonly ConstrutorDiff _construtor = new();

    static string Linhas(int n, string fim = "\n") => string.Concat(Enumerable.Range(1, n).Select(i => $"linha {i}{fim}"));

    static ModeloDiff Modelo(string a, string b) => _construtor.Construir(ArquivoTexto.DeTexto(a), ArquivoTexto.DeTexto(b));

    static VistaDiff Vista(ModeloDiff m, bool ignorar = true, bool recolher = true, params int[] abertos) =>
        new MontadorVista(m, ignorar, recolher, abertos.ToHashSet()).Montar();

    [Fact]
    public void Recolhe_o_trecho_igual_e_mostra_quantas_linhas_escondeu()
    {
        var v = Vista(Modelo(Linhas(100), Linhas(100).Replace("linha 50\n", "linha X\n")));

        var dobras = v.Dir.Where(l => l.Tipo == TipoLinha.Dobra).ToList();
        dobras.Select(l => l.Escondidas).Should().Equal(46, 47);
        dobras[0].Texto.Should().Contain("46 linhas iguais").And.Contain("clique para mostrar");
        v.Dir.Should().HaveCount(9); // dobra + 3 de contexto, a mudança, 3 de contexto + dobra
    }

    [Fact]
    public void Numeros_sao_as_linhas_reais_de_cada_lado()
    {
        var antes = Linhas(30);
        var depois = antes.Replace("linha 15\n", "nova a\nnova b\nnova c\nlinha 15\n");

        var v = Vista(Modelo(antes, depois), recolher: false);

        v.Esq.Should().HaveCount(30);
        v.Dir.Should().HaveCount(33);
        v.Esq.Select(l => l.Numero).Should().Equal(Enumerable.Range(1, 30));
        v.Dir.Select(l => l.Numero).Should().Equal(Enumerable.Range(1, 33));
        v.Dir[14].Tipo.Should().Be(TipoLinha.Adicionada);
    }

    [Fact]
    public void Abrir_um_trecho_mostra_as_linhas_dele()
    {
        var m = Modelo(Linhas(100), Linhas(100).Replace("linha 50\n", "linha X\n"));
        var dobra = Vista(m).Dir.First(l => l.Tipo == TipoLinha.Dobra);

        var aberta = Vista(m, abertos: dobra.Dobra);

        aberta.Dir.Count(l => l.Tipo == TipoLinha.Dobra).Should().Be(1);
        aberta.Dir.Should().Contain(l => l.Numero == 20);
    }

    [Fact]
    public void Ignorar_tira_da_contagem_e_deixa_so_a_dica_na_margem()
    {
        var m = Modelo(Linhas(40, "\r\n"), Linhas(40, "\r\n").Replace("linha 10\r\n", "linha 10\n").Replace("linha 20\r\n", "linha VINTE\r\n"));

        var ligado = Vista(m, ignorar: true, recolher: false);
        var desligado = Vista(m, ignorar: false, recolher: false);

        ligado.Blocos.Should().ContainSingle();
        ligado.Dir[9].Should().Match<LinhaVista>(l => l.Tipo == TipoLinha.Igual && l.SoDica && l.Trivial == Trivial.FimDeLinha);
        desligado.Blocos.Should().HaveCount(2);
        desligado.Dir[9].Should().Match<LinhaVista>(l => l.Tipo == TipoLinha.Invisivel && !l.SoDica && l.Nota == "fim de linha CRLF → LF");
        desligado.Blocos[0].Motivo.Should().Contain("só fim de linha (CRLF → LF)");
    }

    [Fact]
    public void Linhas_ignoradas_nao_quebram_o_recolhimento()
    {
        var m = Modelo(Linhas(100), Linhas(100).Replace("linha 50\n", "linha  50\n"));

        var v = Vista(m);

        v.Blocos.Should().BeEmpty();
        v.Dir.Should().ContainSingle().Which.Tipo.Should().Be(TipoLinha.Dobra);
    }

    [Fact]
    public void Barra_r_solto_no_meio_da_linha_nao_quebra_o_alinhamento()
    {
        var v = Vista(Modelo("a\rb\n", "a\rb\n"), recolher: false);

        v.Dir[0].Texto.Should().NotContain("\r");
    }

    [Fact]
    public void Linha_do_arquivo_e_linha_do_painel_vao_e_voltam_mesmo_dentro_de_dobra()
    {
        var v = Vista(Modelo(Linhas(100), Linhas(100).Replace("linha 50\n", "linha X\n")));

        var indiceDobra = v.Dir.ToList().FindIndex(l => l.Tipo == TipoLinha.Dobra);
        v.LinhaDoPainelDireito(20).Should().Be(indiceDobra);
        v.LinhaDoArquivo(indiceDobra).Should().Be(v.Dir[indiceDobra].PrimeiraEscondida);
    }

    [Fact]
    public void Ancoras_crescem_nos_dois_lados_e_terminam_no_total()
    {
        var v = Vista(Modelo(Linhas(60), Linhas(60).Replace("linha 30\n", "a\nb\nc\n")));

        v.Ancoras.Zip(v.Ancoras.Skip(1)).Should().OnlyContain(p => p.First[0] <= p.Second[0] && p.First[1] <= p.Second[1]);
        v.Ancoras[^1].Should().Equal(v.Esq.Count, v.Dir.Count);
    }
}
