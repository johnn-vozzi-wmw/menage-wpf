using Menage.Core.Atualizacao;

namespace Menage.Tests;

public class OpcoesDeAtualizacaoTests
{
    [Fact]
    public void Salvar_e_carregar_preserva_origem_e_versao_ignorada()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"menage-op-{Guid.NewGuid():N}", "a.json");
        try
        {
            new OpcoesDeAtualizacao(@"\srv\rel", "1.0.1").Salvar(caminho);
            var lida = OpcoesDeAtualizacao.Carregar(caminho);
            Assert.Equal(@"\srv\rel", lida.Origem);
            Assert.Equal("1.0.1", lida.VersaoIgnorada);
        }
        finally { Directory.Delete(Path.GetDirectoryName(caminho)!, true); }
    }

    [Fact]
    public void Carregar_sem_arquivo_devolve_vazio()
    {
        var lida = OpcoesDeAtualizacao.Carregar(Path.Combine(Path.GetTempPath(), "nao-existe.json"));
        Assert.Null(lida.VersaoIgnorada);
    }
}
