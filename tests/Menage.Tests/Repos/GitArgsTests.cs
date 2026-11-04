namespace Menage.Tests.Repos;

public class GitArgsTests
{
    [Fact]
    public void Monta_comando_opcoes_e_argumentos_na_ordem()
    {
        var args = Git.Em(@"C:\x").Comando("push").Opcao("-u").Argumento("origin").Argumento("main");

        args.Repositorio.Should().Be(@"C:\x");
        args.Argumentos.Should().Equal("push", "-u", "origin", "main");
    }

    [Fact]
    public void Opcao_com_valor_vai_como_dois_argumentos_sem_aspas()
    {
        var args = Git.Em("r").Comando("commit").Opcao("-m", "mensagem com espaços e \"aspas\"");

        args.Argumentos.Should().Equal("commit", "-m", "mensagem com espaços e \"aspas\"");
    }

    [Fact]
    public void Caminhos_vem_depois_de_dois_tracos()
    {
        var args = Git.Em("r").Comando("add").Caminhos(["-estranho.txt", "a b.txt"]);

        args.Argumentos.Should().Equal("add", "--", "-estranho.txt", "a b.txt");
    }

    [Fact]
    public void Config_vai_antes_do_comando_mesmo_se_chamada_depois()
    {
        var args = Git.Em("r").Comando("status").Config("core.quotepath", "false");

        args.Argumentos.Should().Equal("-c", "core.quotepath=false", "status");
    }

    [Fact]
    public void E_imutavel_cada_chamada_gera_um_novo_objeto()
    {
        var baseArgs = Git.Em("r").Comando("fetch");
        var comPrune = baseArgs.Opcao("--prune");

        baseArgs.Argumentos.Should().Equal("fetch");
        comPrune.Argumentos.Should().Equal("fetch", "--prune");
    }

    [Fact]
    public void Timeout_e_preservado_ao_encadear()
    {
        var args = Git.Em("r").ComTimeout(TimeSpan.FromSeconds(3)).Comando("fetch");

        args.Timeout.Should().Be(TimeSpan.FromSeconds(3));
    }
}
