using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Menage.Tests.Operacoes;

public sealed class DecoradoresTests : IDisposable
{
    sealed class OperacaoSimples(
        IProjetos projetos, bool destrutiva = false, params PerfilDeUso[] perfis)
        : OperacaoEmLote<int, int>(projetos, new DialogosFalsos(), new ProgressoNulo())
    {
        public int Execucoes { get; private set; }

        public override string Id => "teste.simples";

        public override string Nome => "Simples";

        public override bool Destrutiva => destrutiva;

        public override IReadOnlyCollection<PerfilDeUso> PerfisQueLiberam => perfis.Length == 0 ? base.PerfisQueLiberam : perfis;

        protected override Task<ResultadoDoProjeto<int>> ExecutarEmProjeto(Projeto projeto, int opcoes, CancellationToken cancelar)
        {
            Execucoes++;
            return Task.FromResult(ResultadoDoProjeto<int>.Ok(opcoes));
        }
    }

    sealed class LogEspiao : ILogger<ExecutorComLog>
    {
        public List<string> Mensagens { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Mensagens.Add($"{logLevel}: {formatter(state, exception)}");
    }

    readonly RepositorioDescartavel _repo = RepositorioDescartavel.Criar();
    readonly ProjetosEmMemoria _projetos = new();

    public DecoradoresTests()
    {
        _projetos.Adicionar(_repo.ComoProjeto());
        _projetos.Marcar(_repo.Caminho, true);
    }

    public void Dispose() => _repo.Dispose();

    static GitExecutor CriarGit() => GitDeTeste.Criar();

    [Fact]
    public async Task Perfil_nao_liberado_recusa_sem_executar_a_operacao()
    {
        var op = new OperacaoSimples(_projetos, false, PerfilDeUso.Desenvolvedor);
        var executor = new ExecutorComPerfil(new ExecutorDeOperacoes(), new PerfilFixo(PerfilDeUso.Tester));

        var r = await executor.ExecutarAsync(op, 1);

        r.Erro.Causa.Should().Be("'Simples' não está liberada no perfil Tester");
        op.Execucoes.Should().Be(0);
    }

    [Fact]
    public async Task Perfil_liberado_executa()
    {
        var op = new OperacaoSimples(_projetos, false, PerfilDeUso.Tester);
        var executor = new ExecutorComPerfil(new ExecutorDeOperacoes(), new PerfilFixo(PerfilDeUso.Tester));

        (await executor.ExecutarAsync(op, 1)).EhOk.Should().BeTrue();
        op.Execucoes.Should().Be(1);
    }

    [Fact]
    public async Task Operacao_destrutiva_em_branch_protegida_e_bloqueada()
    {
        var op = new OperacaoSimples(_projetos, destrutiva: true);
        var executor = new ExecutorComProtecao(new ExecutorDeOperacoes(), CriarGit(), RegrasDeProtecao.Padrao);

        var r = await executor.ExecutarAsync(op, 1); // o repositório está na main

        r.Erro.Causa.Should().Be("'Simples' bloqueada pela proteção de branches");
        r.Erro.Explicacao.Should().Contain("projeto-teste (main)");
        op.Execucoes.Should().Be(0);
    }

    [Fact]
    public async Task Operacao_destrutiva_em_branch_livre_executa()
    {
        _repo.Rodar("checkout", "feature/exemplo");
        var op = new OperacaoSimples(_projetos, destrutiva: true);
        var executor = new ExecutorComProtecao(new ExecutorDeOperacoes(), CriarGit(), RegrasDeProtecao.Padrao);

        (await executor.ExecutarAsync(op, 1)).EhOk.Should().BeTrue();
        op.Execucoes.Should().Be(1);
    }

    [Fact]
    public async Task Operacao_que_nao_e_destrutiva_ignora_a_protecao()
    {
        var op = new OperacaoSimples(_projetos, destrutiva: false);
        var executor = new ExecutorComProtecao(new ExecutorDeOperacoes(), CriarGit(), RegrasDeProtecao.Padrao);

        (await executor.ExecutarAsync(op, 1)).EhOk.Should().BeTrue();
    }

    [Fact]
    public async Task Log_registra_inicio_e_fim_com_contagens_e_o_motivo_da_recusa()
    {
        var log = new LogEspiao();
        var op = new OperacaoSimples(_projetos);
        var executor = new ExecutorComLog(new ExecutorDeOperacoes(), log);

        await executor.ExecutarAsync(op, 1);
        await new ExecutorComLog(new ExecutorComPerfil(new ExecutorDeOperacoes(), new PerfilFixo(PerfilDeUso.Tester)),
            log).ExecutarAsync(new OperacaoSimples(_projetos, false, PerfilDeUso.Geral), 1);

        log.Mensagens.Should().Contain(m => m.Contains("teste.simples iniciada"))
            .And.Contain(m => m.Contains("1 ok, 0 aviso(s), 0 erro(s)"))
            .And.Contain(m => m.StartsWith("Warning") && m.Contains("não executada"));
    }

    [Fact]
    public async Task Cadeia_montada_por_DI_aplica_perfil_e_protecao_na_ordem()
    {
        var servicos = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton<IProjetos>(_projetos)
            .AddSingleton<IPerfilAtual>(new PerfilFixo(PerfilDeUso.Tester))
            .AddMenageCore(new PastaDeDados(Path.Combine(_repo.Raiz, "dados")));
        using var sp = servicos.BuildServiceProvider();
        var executor = sp.GetRequiredService<IExecutorDeOperacoes>();

        var liberada = new OperacaoSimples(_projetos, destrutiva: true, PerfilDeUso.Desenvolvedor);
        var destrutiva = new OperacaoSimples(_projetos, destrutiva: true, PerfilDeUso.Tester);

        (await executor.ExecutarAsync(liberada, 1)).Erro.Causa.Should().Contain("perfil Tester");
        (await executor.ExecutarAsync(destrutiva, 1)).Erro.Causa.Should().Contain("proteção de branches");
    }
}
