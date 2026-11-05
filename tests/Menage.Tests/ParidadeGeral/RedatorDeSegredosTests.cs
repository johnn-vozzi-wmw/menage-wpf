using Menage.Core.Inicializacao;

namespace Menage.Tests.ParidadeGeral;

public class RedatorDeSegredosTests
{
    [Theory]
    [InlineData("push https://ana:s3nha@github.com/x/y.git feito", "https://***@github.com")]
    [InlineData("Authorization: Bearer abc.def.ghi", "Authorization: Bearer ***")]
    [InlineData("GET /rest token=abcdef123 ok", "token=***")]
    [InlineData("{\"senha\": \"minha senha\"}", "\"senha\": \"***")]
    [InlineData("password: hunter2 e mais", "password: ***")]
    [InlineData("usando ghp_abcdefghijklmnopqrstuvwxyz0123456789 agora", "usando *** agora")]
    [InlineData("api_key=XYZ&outro=1", "api_key=***&outro=1")]
    public void Tira_segredos_e_preserva_o_resto(string entrada, string esperado) =>
        RedatorDeSegredos.Limpar(entrada).Should().Contain(esperado);

    [Theory]
    [InlineData("s3nha")]
    [InlineData("hunter2")]
    [InlineData("abc.def.ghi")]
    [InlineData("abcdef123")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789")]
    public void O_valor_do_segredo_nunca_sobra(string segredo)
    {
        var entradas = new[]
        {
            $"https://u:{segredo}@h/x", $"Authorization: Bearer {segredo}", $"token={segredo}", $"\"senha\":\"{segredo}\"", $"password: {segredo}", $"x {segredo} y",
        };
        foreach (var e in entradas)
            if (!e.StartsWith("x ", StringComparison.Ordinal) || segredo.StartsWith("ghp_", StringComparison.Ordinal))
                RedatorDeSegredos.Limpar(e).Should().NotContain(segredo, because: e);
    }

    [Fact]
    public void Texto_sem_segredo_fica_igual_e_nulo_vira_vazio()
    {
        RedatorDeSegredos.Limpar("Operação git.fetch terminou em 120 ms: 3 ok").Should().Be("Operação git.fetch terminou em 120 ms: 3 ok");
        RedatorDeSegredos.Limpar("origin https://github.com/wmw/menage.git").Should().Be("origin https://github.com/wmw/menage.git");
        RedatorDeSegredos.Limpar(null).Should().BeEmpty();
    }
}
