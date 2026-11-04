namespace Menage.Tests.Repos;

public class AmbienteSshTests
{
    const string Tortoise = @"C:\T\bin";
    const string Plink = @"C:\T\bin\TortoiseGitPlink.exe";
    const string Chave = @"C:\u\.ssh\id_wmw";

    static AmbienteSsh Criar(RunnerFalso runner, bool tortoise, bool chave, string? gitSshCommand = null) =>
        new(runner, new OpcoesSsh(Chave, Tortoise),
            variavel: nome => nome == "GIT_SSH_COMMAND" ? gitSshCommand : null,
            existe: caminho => (tortoise && caminho == Plink) || (chave && caminho == Chave));

    [Fact]
    public async Task Com_Tortoise_usa_Pageant_via_TortoiseGitPlink()
    {
        var env = await Criar(new RunnerFalso(), tortoise: true, chave: true).ObterAsync();

        env.Should().Contain("GIT_SSH", Plink).And.Contain("GIT_SSH_VARIANT", "tortoiseplink");
        env["GIT_SSH_COMMAND"].Should().BeNull("a variável precisa ser removida do processo");
    }

    [Fact]
    public async Task GIT_SSH_COMMAND_do_usuario_vence_e_nada_e_alterado()
    {
        var runner = new RunnerFalso();

        var env = await Criar(runner, tortoise: true, chave: true, gitSshCommand: "ssh -i x").ObterAsync();

        env.Should().BeEmpty();
        runner.Comandos.Should().BeEmpty("nem precisa consultar o git config");
    }

    [Fact]
    public async Task Core_sshCommand_do_git_config_vence_e_nada_e_alterado()
    {
        var runner = new RunnerFalso(_ => RunnerFalso.Saida(0, "C:/Windows/System32/OpenSSH/ssh.exe\n"));

        var env = await Criar(runner, tortoise: true, chave: true).ObterAsync();

        env.Should().BeEmpty();
        runner.Comandos.Single().Argumentos.Should().Equal("config", "--get", "core.sshCommand");
    }

    [Fact]
    public async Task Sem_Tortoise_mas_com_chave_usa_OpenSSH_com_a_chave()
    {
        var runner = new RunnerFalso(_ => RunnerFalso.Saida(1));

        var env = await Criar(runner, tortoise: false, chave: true).ObterAsync();

        env["GIT_SSH_COMMAND"].Should().Be("ssh -i \"C:/u/.ssh/id_wmw\" -o IdentitiesOnly=yes");
        env.Should().NotContainKey("GIT_SSH");
    }

    [Fact]
    public async Task Sem_nada_nao_altera_o_ambiente()
    {
        var runner = new RunnerFalso(_ => RunnerFalso.Saida(1));

        (await Criar(runner, tortoise: false, chave: false).ObterAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Calcula_uma_unica_vez()
    {
        var runner = new RunnerFalso(_ => RunnerFalso.Saida(1));
        var ssh = Criar(runner, tortoise: true, chave: true);

        await ssh.ObterAsync();
        await ssh.ObterAsync();

        runner.Comandos.Should().HaveCount(1);
    }
}
