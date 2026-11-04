namespace Menage.Tests.Repos;

public class GitExecutorTests
{
    sealed class SshFixo(Dictionary<string, string?> variaveis) : IAmbienteSsh
    {
        public Task<IReadOnlyDictionary<string, string?>> ObterAsync(CancellationToken cancelar = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string?>>(variaveis);
    }

    [Fact]
    public async Task Envia_as_variaveis_que_impedem_pergunta_interativa()
    {
        var runner = new RunnerFalso();
        var git = new GitExecutor(runner, new SshFixo([]));

        await git.ExecutarAsync(Git.Em("repo").Comando("status"));

        var comando = runner.Comandos.Single();
        comando.Arquivo.Should().Be("git");
        comando.PastaDeTrabalho.Should().Be("repo");
        comando.Ambiente.Should().Contain("GIT_TERMINAL_PROMPT", "0")
            .And.Contain("GIT_EDITOR", "true")
            .And.Contain("GIT_SEQUENCE_EDITOR", "true")
            .And.Contain("GIT_PAGER", "cat");
    }

    [Fact]
    public async Task Junta_as_variaveis_de_ssh_e_as_do_git_valem_mais()
    {
        var runner = new RunnerFalso();
        var ssh = new SshFixo(new() { ["GIT_SSH"] = "plink", ["GIT_PAGER"] = "less" });

        await new GitExecutor(runner, ssh).ExecutarAsync(Git.Em("r").Comando("fetch"));

        runner.Comandos.Single().Ambiente.Should().Contain("GIT_SSH", "plink").And.Contain("GIT_PAGER", "cat");
    }

    [Fact]
    public async Task Timeout_padrao_e_o_do_GitArgs_quando_informado()
    {
        var runner = new RunnerFalso();
        var git = new GitExecutor(runner, new SshFixo([]));

        await git.ExecutarAsync(Git.Em("r").Comando("a"));
        await git.ExecutarAsync(Git.Em("r").Comando("b").ComTimeout(TimeSpan.FromSeconds(9)));

        runner.Comandos[0].Timeout.Should().Be(TimeSpan.FromMinutes(5));
        runner.Comandos[1].Timeout.Should().Be(TimeSpan.FromSeconds(9));
    }

    [Theory]
    [InlineData("fatal: not a git repository (or any of the parent directories): .git", "Pasta não é um repositório Git")]
    [InlineData("git@github.com: Permission denied (publickey).", "Chave SSH recusada")]
    [InlineData("fatal: Could not resolve host: github.com", "Sem conexão com o remoto")]
    [InlineData("ssh: Could not resolve hostname: getaddrinfo: atch", "Configuração de SSH incompatível")]
    public async Task Classifica_erros_conhecidos(string stderr, string causa)
    {
        var git = new GitExecutor(new RunnerFalso(_ => RunnerFalso.Saida(128, erro: stderr)), new SshFixo([]));

        var r = await git.ExecutarAsync(Git.Em("r").Comando("fetch"));

        r.EhOk.Should().BeFalse();
        r.Erro.Causa.Should().Be(causa);
        r.Erro.AcaoSugerida.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Erro_desconhecido_carrega_o_texto_do_git()
    {
        var git = new GitExecutor(new RunnerFalso(_ => RunnerFalso.Saida(1, erro: "algo estranho")), new SshFixo([]));

        var r = await git.ExecutarAsync(Git.Em("r").Comando("gc"));

        r.Erro.Causa.Should().Be("git gc falhou (código 1)");
        r.Erro.Explicacao.Should().Be("algo estranho");
    }

    [Fact]
    public async Task Git_em_repositorio_real_devolve_a_branch_e_o_erro_fora_de_repositorio()
    {
        using var repo = RepositorioDescartavel.Criar();
        var git = GitDeTeste.Criar();

        var ok = await git.ExecutarAsync(Git.Em(repo.Caminho).Comando("rev-parse").Opcao("--abbrev-ref").Argumento("HEAD"));
        var fora = await git.ExecutarAsync(Git.Em(repo.Raiz).Comando("rev-parse").Argumento("HEAD"));

        ok.Valor.Texto.Trim().Should().Be("main");
        fora.EhOk.Should().BeFalse();
        fora.Erro.Causa.Should().Be("Pasta não é um repositório Git");
    }

    [Fact]
    public async Task Git_nao_instalado_vira_erro_com_acao()
    {
        var git = new GitExecutor(new ProcessoRunner(), new SshFixo([]), "git-que-nao-existe-xyz");

        var r = await git.ExecutarAsync(Git.Em(Path.GetTempPath()).Comando("status"));

        r.Erro.Causa.Should().Be("Git indisponível");
    }
}
