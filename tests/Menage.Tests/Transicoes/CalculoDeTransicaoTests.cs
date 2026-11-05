using FluentAssertions;
using Menage.Core.Transicoes;
using Xunit;

namespace Menage.Tests.Transicoes;

public class CalculoDeTransicaoTests
{
    [Theory]
    [InlineData(null, 2, Sentido.Nenhum)]
    [InlineData(1, 2, Sentido.Avancar)]
    [InlineData(3, 1, Sentido.Voltar)]
    [InlineData(2, 2, Sentido.Nenhum)]
    public void Direcao_segue_a_ordem_dos_indices(int? anterior, int novo, Sentido esperado) =>
        CalculoDeTransicao.Direcao(anterior, novo).Should().Be(esperado);

    [Fact]
    public void Pagina_que_entra_vem_do_lado_do_sentido_e_a_que_sai_vai_para_o_oposto()
    {
        CalculoDeTransicao.EntradaDePagina(Sentido.Avancar).Deslocamento.Should().BeGreaterThan(0);
        CalculoDeTransicao.SaidaDePagina(Sentido.Avancar).Deslocamento.Should().BeLessThan(0);
        CalculoDeTransicao.EntradaDePagina(Sentido.Voltar).Deslocamento.Should().BeLessThan(0);
        CalculoDeTransicao.EntradaDePagina(Sentido.Nenhum).Deslocamento.Should().Be(0);
    }

    [Fact]
    public void Do_inicio_a_duracao_e_a_cheia()
    {
        var origem = CalculoDeTransicao.EntradaDePagina(Sentido.Avancar);
        var passo = CalculoDeTransicao.Planejar(origem, EstadoVisual.Repouso, origem, CalculoDeTransicao.EntradaPaginaMs, NivelDeMovimento.Completo);
        passo.Instantaneo.Should().BeFalse();
        passo.Duracao.TotalMilliseconds.Should().Be(CalculoDeTransicao.EntradaPaginaMs);
    }

    [Fact]
    public void Transicao_que_chega_no_meio_parte_do_estado_atual_e_dura_so_o_que_falta()
    {
        var meio = new EstadoVisual(0.5, 12, 1);
        var passo = CalculoDeTransicao.Planejar(meio, EstadoVisual.Repouso, CalculoDeTransicao.EntradaDePagina(Sentido.Avancar),
            CalculoDeTransicao.EntradaPaginaMs, NivelDeMovimento.Completo);
        passo.De.Should().Be(meio);
        passo.Duracao.TotalMilliseconds.Should().BeApproximately(CalculoDeTransicao.EntradaPaginaMs / 2.0, 1);
    }

    [Fact]
    public void Duracao_nunca_cai_abaixo_do_minimo()
    {
        var quaseLa = new EstadoVisual(0.95, 1, 1);
        var passo = CalculoDeTransicao.Planejar(quaseLa, EstadoVisual.Repouso, CalculoDeTransicao.EntradaDePagina(Sentido.Avancar),
            CalculoDeTransicao.EntradaPaginaMs, NivelDeMovimento.Completo);
        passo.Duracao.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(CalculoDeTransicao.MinimoMs);
    }

    [Fact]
    public void Ja_no_alvo_e_instantaneo()
    {
        var passo = CalculoDeTransicao.Planejar(EstadoVisual.Repouso, EstadoVisual.Repouso, CalculoDeTransicao.EntradaDeJanela,
            CalculoDeTransicao.EntradaJanelaMs, NivelDeMovimento.Completo);
        passo.Instantaneo.Should().BeTrue();
        passo.Duracao.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Sem_animacao_do_sistema_aplica_o_alvo_na_hora()
    {
        var passo = CalculoDeTransicao.Planejar(CalculoDeTransicao.EntradaDeJanela, EstadoVisual.Repouso, CalculoDeTransicao.EntradaDeJanela,
            CalculoDeTransicao.EntradaJanelaMs, NivelDeMovimento.Nenhum);
        passo.Instantaneo.Should().BeTrue();
        passo.Para.Should().Be(EstadoVisual.Repouso);
    }

    [Fact]
    public void Movimento_reduzido_anima_so_a_opacidade()
    {
        var passo = CalculoDeTransicao.Planejar(CalculoDeTransicao.EntradaDeJanela, EstadoVisual.Repouso, CalculoDeTransicao.EntradaDeJanela,
            CalculoDeTransicao.EntradaJanelaMs, NivelDeMovimento.Reduzido);
        passo.De.Escala.Should().Be(1);
        passo.De.Deslocamento.Should().Be(0);
        passo.De.Opacidade.Should().Be(0);
        passo.Duracao.TotalMilliseconds.Should().Be(CalculoDeTransicao.EntradaJanelaMs);
    }

    [Theory]
    [InlineData(true, false, NivelDeMovimento.Completo)]
    [InlineData(true, true, NivelDeMovimento.Reduzido)]
    [InlineData(false, false, NivelDeMovimento.Nenhum)]
    [InlineData(false, true, NivelDeMovimento.Nenhum)]
    public void Resolver_combina_sistema_e_preferencia(bool sistema, bool reduzir, NivelDeMovimento esperado) =>
        CalculoDeTransicao.Resolver(sistema, reduzir).Should().Be(esperado);

    [Fact]
    public void Duracoes_cheias_ficam_entre_120_e_220_ms()
    {
        int[] todas = [CalculoDeTransicao.EntradaPaginaMs, CalculoDeTransicao.SaidaPaginaMs, CalculoDeTransicao.EntradaJanelaMs, CalculoDeTransicao.SaidaJanelaMs];
        todas.Should().OnlyContain(ms => ms >= 120 && ms <= 220);
    }
}
