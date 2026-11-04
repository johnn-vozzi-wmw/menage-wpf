namespace Menage.Tests.Base;

public class ResultadoTests
{
    [Fact]
    public void Ok_expoe_valor_e_nao_tem_erro()
    {
        var r = Resultado.Ok(42);

        r.EhOk.Should().BeTrue();
        r.Valor.Should().Be(42);
        r.Invoking(x => x.Erro).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Falha_expoe_causa_explicacao_e_acao_e_nao_tem_valor()
    {
        var r = Resultado.Falha<int>("Causa", "Porque sim", "Faça isto");

        r.EhOk.Should().BeFalse();
        r.Erro.Should().Be(new Erro("Causa", "Porque sim", "Faça isto"));
        r.Erro.ToString().Should().Be("Causa Porque sim Faça isto");
        r.Invoking(x => x.Valor).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Mapear_transforma_o_ok_e_propaga_o_erro()
    {
        Resultado.Ok(2).Mapear(x => x * 3).Valor.Should().Be(6);
        Resultado.Falha<int>("x").Mapear(x => x * 3).Erro.Causa.Should().Be("x");
    }

    [Fact]
    public void Encadear_para_no_primeiro_erro()
    {
        var chamado = false;
        var r = Resultado.Falha<int>("primeiro").Encadear(x => { chamado = true; return Resultado.Ok(x); });

        r.Erro.Causa.Should().Be("primeiro");
        chamado.Should().BeFalse();
        Resultado.Ok(1).Encadear(x => Resultado.Ok(x + 1)).Valor.Should().Be(2);
    }

    [Fact]
    public void ValorOu_devolve_o_padrao_so_na_falha()
    {
        Resultado.Falha<int>("x").ValorOu(7).Should().Be(7);
        Resultado.Ok(1).ValorOu(7).Should().Be(1);
    }
}
