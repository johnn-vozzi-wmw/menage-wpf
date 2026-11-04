namespace Menage.Tests.Operacoes;

public class RegrasDeProtecaoTests
{
    [Theory]
    [InlineData("main", true)]
    [InlineData("MAIN", true)]
    [InlineData("release/1.2", true)]
    [InlineData("release", true)]
    [InlineData("release-9.1", true)]
    [InlineData("homologacao", true)]
    [InlineData("feature/main", false)]
    [InlineData("VENDAS-123", false)]
    public void Aceitam_nome_exato_e_curinga(string branch, bool protegida)
    {
        RegrasDeProtecao.Padrao.Protegida(branch).Should().Be(protegida);
    }
}
