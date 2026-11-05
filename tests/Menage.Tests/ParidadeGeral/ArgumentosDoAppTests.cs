using Menage.Core.Inicializacao;

namespace Menage.Tests.ParidadeGeral;

public class ArgumentosDoAppTests
{
    [Fact]
    public void Sem_argumentos_tudo_padrao()
    {
        var a = ArgumentosDoApp.Ler([]);
        (a.PastaDeDados, a.Tema, a.Minimizado, a.ArquivoDeMedicao, a.Ajuda).Should().Be((null, null, false, null, false));
    }

    [Fact]
    public void Le_dados_tema_bandeja_e_medicao()
    {
        var a = ArgumentosDoApp.Ler(["--tema", "claro", "--bandeja", "--dados", @"C:\x\y", "--medir-inicio", "m.json"]);
        a.PastaDeDados.Should().Be(@"C:\x\y");
        a.Tema.Should().Be("claro");
        a.Minimizado.Should().BeTrue();
        a.ArquivoDeMedicao.Should().Be("m.json");
    }

    [Fact]
    public void Pasta_do_Git_Manager_antigo_vale_como_dados()
    {
        ArgumentosDoApp.Ler(["--pasta", @"C:\teste"]).PastaDeDados.Should().Be(@"C:\teste");
    }

    [Fact]
    public void Valor_que_parece_outra_opcao_nao_e_aceito()
    {
        var a = ArgumentosDoApp.Ler(["--dados", "--bandeja"]);
        a.PastaDeDados.Should().BeNull();
        a.Minimizado.Should().BeTrue();
    }

    [Theory]
    [InlineData("--ajuda")]
    [InlineData("--help")]
    [InlineData("/?")]
    public void Pede_ajuda(string opcao) => ArgumentosDoApp.Ler([opcao]).Ajuda.Should().BeTrue();

    [Fact]
    public void Texto_de_ajuda_cita_todas_as_opcoes()
    {
        foreach (var opcao in new[] { "--dados", "--tema", "--bandeja", "--diff", "--conflito", "--medir-inicio", "--ajuda" })
            ArgumentosDoApp.Texto.Should().Contain(opcao);
    }

    [Fact]
    public void Instancia_e_por_pasta_de_dados_sem_diferenciar_maiusculas_nem_barra_final()
    {
        IdentidadeDaInstancia.Nome(@"C:\Dados\Menage").Should().Be(IdentidadeDaInstancia.Nome(@"c:\dados\menage\"));
        IdentidadeDaInstancia.Nome(@"C:\Dados\Menage").Should().NotBe(IdentidadeDaInstancia.Nome(@"C:\Dados\Outra"));
        IdentidadeDaInstancia.NomeDoSinal(@"C:\a").Should().StartWith(IdentidadeDaInstancia.NomeDoMutex(@"C:\a"));
    }
}
