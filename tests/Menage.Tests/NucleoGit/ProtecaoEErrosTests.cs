using Menage.Core.Protecao;

namespace Menage.Tests.NucleoGit;

public sealed class ProtecaoEErrosTests
{
    sealed class OperacaoDeCommit(IProjetos projetos) : OperacaoEmLote<OpcoesDeCommit, int>(projetos, new DialogosFalsos(), new ProgressoNulo())
    {
        public int Execucoes { get; private set; }

        public override string Id => "teste.commit";

        public override string Nome => "Commit";

        protected override Task<ResultadoDoProjeto<int>> ExecutarEmProjeto(Projeto projeto, OpcoesDeCommit opcoes, CancellationToken cancelar)
        {
            Execucoes++;
            return Task.FromResult(ResultadoDoProjeto<int>.Ok(1));
        }
    }

    sealed record OpcoesDeCommit(string Mensagem) : IComMensagemDeCommit
    {
        public string MensagemDeCommit => Mensagem;
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("MASTER", true)]
    [InlineData("Develop", true)]
    [InlineData("release-9.1", true)]
    [InlineData("release/2026", true)]
    [InlineData("producao", true)]
    [InlineData("VENDAS-1", false)]
    [InlineData("feat/x", false)]
    [InlineData("", false)]
    public void Branches_protegidas_por_nome_ou_curinga_sem_diferenciar_caixa(string branch, bool protegida) =>
        new ConfigDeProtecao().EhProtegida(branch).Should().Be(protegida);

    [Theory]
    [InlineData("feat: nova tela", true)]
    [InlineData("fix(vendas)!: corrige total", true)]
    [InlineData("FEAT: maiúsculas", true)]
    [InlineData("VENDAS-123: ajuste", true)]
    [InlineData("VENDAS-123 ajuste sem dois pontos", true)]
    [InlineData("ajustes diversos", false)]
    [InlineData("feat:sem espaço", false)]
    [InlineData("   ", false)]
    public void Convencao_de_commit_quando_ligada(string mensagem, bool aceita) =>
        (new ConfigDeProtecao { ValidarCommit = true }.ValidarMensagem(mensagem) is null).Should().Be(aceita);

    [Fact]
    public void Desligada_aceita_qualquer_mensagem_e_ticket_pode_ser_desligado()
    {
        new ConfigDeProtecao().ValidarMensagem("qualquer coisa").Should().BeNull();
        new ConfigDeProtecao { ValidarCommit = true, AceitarTicket = false }.ValidarMensagem("VENDAS-1: x").Should().NotBeNull();
    }

    [Fact]
    public void Configuracao_e_gravada_em_protecao_json_e_lida_de_novo()
    {
        var pasta = new PastaDeDados(Path.Combine(Path.GetTempPath(), "mp" + Guid.NewGuid().ToString("N")[..6]));
        try
        {
            var protecao = new ProtecaoDeBranches(pasta);
            protecao.Protegida("main").Should().BeTrue("o padrão vale sem arquivo");

            protecao.Salvar(new ConfigDeProtecao { Branches = ["alvo*"], BloquearPush = false, BloquearExclusao = false }).EhOk.Should().BeTrue();

            var lida = new ProtecaoDeBranches(pasta);
            lida.Atual.Branches.Should().Equal("alvo*");
            lida.Protegida("alvo-1").Should().BeFalse("sem bloquear push nem exclusão não há proteção");
        }
        finally
        {
            if (Directory.Exists(pasta.Caminho)) Directory.Delete(pasta.Caminho, recursive: true);
        }
    }

    [Fact]
    public async Task Decorador_recusa_commit_fora_da_convencao_sem_executar_e_deixa_passar_o_valido()
    {
        var projetos = new ProjetosEmMemoria();
        var operacao = new OperacaoDeCommit(projetos);
        var validador = new ProtecaoDeBranches(new InMemoria(new ConfigDeProtecao { ValidarCommit = true }));
        var executor = new ExecutorComConvencaoDeCommit(new ExecutorDeOperacoes(), validador);

        var recusado = await executor.ExecutarAsync(operacao, new OpcoesDeCommit("ajustes"));
        recusado.Erro.Causa.Should().Be("Mensagem de commit fora da convenção");
        operacao.Execucoes.Should().Be(0);

        projetos.Adicionar(new Projeto("p", Path.GetTempPath()));
        projetos.Marcar(Path.GetTempPath(), true);
        (await executor.ExecutarAsync(operacao, new OpcoesDeCommit("feat: ok"))).EhOk.Should().BeTrue();
    }

    sealed class InMemoria(ConfigDeProtecao config) : IArmazenamento<ConfigDeProtecao>
    {
        public string Arquivo => "memoria";

        public Resultado<ConfigDeProtecao> Carregar() => Resultado.Ok(config);

        public Resultado<Nada> Salvar(ConfigDeProtecao dados) => Resultado.Ok();
    }

    [Fact]
    public void Escapes_octais_do_ssh_viram_acentos()
    {
        FalhasDoGit.DecodificarOctal("getaddrinfo: atch: Este host n\\343o \\351 conhecido.").Should().Be("getaddrinfo: atch: Este host não é conhecido.");
        FalhasDoGit.DecodificarOctal("n\\303\\243o").Should().Be("não");
        FalhasDoGit.DecodificarOctal("sem escapes C:\\Users").Should().Be("sem escapes C:\\Users");
    }

    [Fact]
    public void Erro_do_print_e_configuracao_de_ssh_e_nao_rede_mas_dns_de_verdade_continua_rede()
    {
        var print = "getaddrinfo: atch: Este host n\\343o \\351 conhecido.\nssh: connect to host github.com port 22: failure\nfatal: Could not read from remote repository.";
        var ssh = FalhasDoGit.Reconhecer(print);
        ssh.Tipo.Should().Be(TipoDeFalhaGit.ConfiguracaoSsh);
        ssh.Host.Should().Be("github.com");
        ssh.Mensagem.Should().NotContain("Sem conexão");

        FalhasDoGit.Reconhecer("ssh: Could not resolve hostname github.com: Este host n\\343o \\351 conhecido.").Tipo.Should().Be(TipoDeFalhaGit.Rede);
        FalhasDoGit.Reconhecer("getaddrinfo: github.com: Name or service not known").Tipo.Should().Be(TipoDeFalhaGit.Rede);
        FalhasDoGit.Reconhecer("getaddrinfo: atch: Este host n\\343o \\351 conhecido.").Tipo.Should().NotBe(TipoDeFalhaGit.ConfiguracaoSsh, "sem servidor conhecido não se afirma erro de ssh");
    }

    [Theory]
    [InlineData("git@github.com: Permission denied (publickey).", TipoDeFalhaGit.Acesso)]
    [InlineData("remote: Invalid username or password.\nfatal: Authentication failed for 'https://github.com/x.git/'", TipoDeFalhaGit.Acesso)]
    [InlineData("Server refused our key", TipoDeFalhaGit.Acesso)]
    [InlineData("Host key verification failed.", TipoDeFalhaGit.ChaveDoHost)]
    [InlineData("Cannot confirm a host key in batch mode", TipoDeFalhaGit.ChaveDoHost)]
    [InlineData("ERROR: Repository not found.", TipoDeFalhaGit.Repositorio)]
    [InlineData("fatal: unable to access 'https://github.com/x/y.git/': Could not resolve host: github.com", TipoDeFalhaGit.Rede)]
    [InlineData("ssh: connect to host 10.1.1.5 port 22: Connection timed out", TipoDeFalhaGit.Rede)]
    [InlineData("Unable to open connection:\nHost does not exist", TipoDeFalhaGit.Rede)]
    [InlineData("fatal: Could not read from remote repository.\nPlease make sure you have the correct access rights\nand the repository exists.", TipoDeFalhaGit.Desconhecida)]
    [InlineData("CONFLICT (content): Merge conflict in a.txt", TipoDeFalhaGit.Desconhecida)]
    [InlineData("", TipoDeFalhaGit.Desconhecida)]
    public void Classifica_o_tipo_da_falha_sem_adivinhar_o_que_nao_conhece(string texto, TipoDeFalhaGit tipo) =>
        FalhasDoGit.Reconhecer(texto).Tipo.Should().Be(tipo);

    [Fact]
    public void Host_vem_da_mensagem_ou_do_origin_quando_o_plink_nao_diz()
    {
        FalhasDoGit.Reconhecer("ssh: connect to host 10.1.1.5 port 22: Connection timed out").Host.Should().Be("10.1.1.5");
        FalhasDoGit.Reconhecer("Unable to open connection:\nHost does not exist", "github.com").Host.Should().Be("github.com");
        FalhasDoGit.HostDaUrl("ssh://git@github.com/org/x.git").Should().Be("github.com");
        FalhasDoGit.HostDaUrl("git@gitlab.wmw.com.br:grupo/x.git").Should().Be("gitlab.wmw.com.br");
        FalhasDoGit.HostDaUrl("https://usuario@host.exemplo.com:8443/x.git").Should().Be("host.exemplo.com");
    }

    [Fact]
    public async Task Falha_de_rede_no_executor_explica_com_o_host_do_origin()
    {
        var runner = new RunnerFalso(c => c.Argumentos.Contains("get-url")
            ? RunnerFalso.Saida(0, "git@gitlab.wmw.com.br:grupo/x.git\n")
            : RunnerFalso.Saida(128, erro: "Unable to open connection:\nHost does not exist"));
        var git = new GitExecutor(runner, new Ssh());

        var r = await git.ExecutarAsync(Git.Em("repo").Comando("pull"));

        r.Erro.Causa.Should().Be("Sem conexão com o remoto");
        r.Erro.Explicacao.Should().Contain("gitlab.wmw.com.br");
    }

    sealed class Ssh : IAmbienteSsh
    {
        public Task<IReadOnlyDictionary<string, string?>> ObterAsync(CancellationToken cancelar = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>());
    }
}
