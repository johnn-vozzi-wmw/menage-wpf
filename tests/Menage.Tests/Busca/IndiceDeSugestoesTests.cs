using System.Diagnostics;
using Menage.Core.Busca;

namespace Menage.Tests.Busca;

public sealed class IndiceDeSugestoesTests
{
    static readonly string[] Branches =
    [
        "VENDAS-24803-replicacao-histconfirmaped", "origin/VENDAS-24803-replicacao-histconfirmaped",
        "develop", "main", "feature/Ação-nova", "origin/develop", "release/1.2.0",
    ];

    [Fact]
    public void Casa_a_cada_letra_e_destaca_o_trecho_digitado()
    {
        var r = new IndiceDeSugestoes(Branches).Buscar("VENDAS-24803-re");

        r.Select(s => s.Texto).Should().Equal(Branches[0], Branches[1]);
        r[0].Destaques.Should().Equal(new TrechoDestacado(0, 15));
        r[1].Destaques.Should().Equal(new TrechoDestacado(7, 15));
    }

    [Fact]
    public void Ignora_acento_e_caixa_e_mantem_o_tamanho_do_texto()
    {
        new IndiceDeSugestoes(Branches).Buscar("acao").Single(s => s.Texto.Contains("Ação")).Destaques
            .Should().Equal(new TrechoDestacado(8, 4));
    }

    [Fact]
    public void Quem_comeca_com_o_texto_vem_antes_de_quem_so_contem()
    {
        new IndiceDeSugestoes(Branches).Buscar("dev").Select(s => s.Texto).Should().Equal("develop", "origin/develop");
    }

    [Fact]
    public void Varias_palavras_precisam_casar_todas()
    {
        new IndiceDeSugestoes(Branches).Buscar("origin repl").Select(s => s.Texto).Should().Equal(Branches[1]);
    }

    [Fact]
    public void Vazio_devolve_o_comeco_da_lista_e_respeita_o_maximo()
    {
        var indice = new IndiceDeSugestoes(Branches);
        indice.Buscar("", 3).Should().HaveCount(3);
        indice.Buscar(null, 100).Should().HaveCount(Branches.Length);
    }

    [Fact]
    public void Cem_mil_itens_respondem_em_poucos_milissegundos()
    {
        var indice = new IndiceDeSugestoes(Enumerable.Range(0, 100_000).Select(i => $"origin/feature/TAREFA-{i}-descricao-longa"));
        indice.Buscar("tarefa-9"); // aquece
        var t = Stopwatch.StartNew();
        var r = indice.Buscar("tarefa-99 desc");
        t.Stop();

        r.Should().HaveCount(50);
        t.ElapsedMilliseconds.Should().BeLessThan(250);
    }
}
