using Menage.Core.Atualizacao;
using Velopack.Sources;

namespace Menage.Tests;

public class FabricaDeOrigemTests
{
    [Theory]
    [InlineData("github:dono/repo", typeof(GithubSource))]
    [InlineData("https://exemplo.com/feed", typeof(SimpleWebSource))]
    [InlineData(@"C:\feed", typeof(SimpleFileSource))]
    public void Cada_forma_de_origem_vira_a_fonte_certa(string origem, Type esperado) =>
        Assert.IsType(esperado, FabricaDeOrigem.Criar(origem));
}
