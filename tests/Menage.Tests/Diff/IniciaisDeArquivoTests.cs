using Menage.Core.Diff;

namespace Menage.Tests.Diff;

public class IniciaisDeArquivoTests
{
    [Theory]
    [InlineData("src/PedidoService.java", "PS")]
    [InlineData("PedidoBean.java", "PB")]
    [InlineData("a/b/RegraPendenciaMargemRentabilidade.java", "RR")]
    [InlineData("ConfirmacaoPedidoClienteManager.java", "CM")]
    [InlineData("res/Messages_en_US.properties", "ME")]
    [InlineData("Messages_pt.properties", "ME")]
    [InlineData("HTTPClient.java", "HC")]
    [InlineData("Main.java", "MA")]
    [InlineData("pom.xml", "PO")]
    [InlineData("app.js", "AP")]
    [InlineData("tabela_pedido_item.sql", "TI")]
    [InlineData("Dockerfile", "DO")]
    [InlineData("README", "RE")]
    [InlineData(".gitignore", "GI")]
    [InlineData("a.txt", "??")]
    [InlineData("123.txt", "??")]
    [InlineData("", "??")]
    public void Duas_letras(string caminho, string esperado) =>
        Assert.Equal(esperado, IniciaisDeArquivo.De(caminho));
}
