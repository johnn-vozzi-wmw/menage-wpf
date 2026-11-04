using Menage.Core.ModoBeta;
using Menage.Core.Operacoes.Checkout;
using Menage.Core.Operacoes.Pull;
using Menage.Core.Operacoes.Stash;
using Menage.Core.Operacoes.Sync;
using Menage.Core.Saida;

namespace Menage.Tests.JanelaPrincipal;

/// <summary>A operação do começo ao fim, como a janela principal a roda: coordenador, Saída e resumo, em repositório real em %TEMP%.</summary>
public sealed class CoordenadorDeOperacoesTests : IDisposable
{
    readonly RepositorioDescartavel _repo = RepositorioDescartavel.Criar();
    readonly GitExecutor _git = GitDeTeste.Criar();
    readonly ProjetosEmMemoria _projetos = new();
    readonly SaidaDeOperacoes _saida = new();
    readonly DialogosFalsos _interno = new();
    readonly DialogosComResumoNaSaida _dialogos;
    readonly CoordenadorDeOperacoes _coordenador;
    readonly ConsultasGit _consultas;

    public CoordenadorDeOperacoesTests()
    {
        _consultas = new ConsultasGit(_git);
        _dialogos = new DialogosComResumoNaSaida(_interno, _saida);
        _coordenador = new CoordenadorDeOperacoes(new ExecutorDeOperacoes(), _saida);
        _projetos.Adicionar(_repo.ComoProjeto("p1"));
        _projetos.Marcar(_repo.Caminho, true);
    }

    public void Dispose()
    {
        _coordenador.Dispose();
        _repo.Dispose();
    }

    [Fact]
    public async Task Fetch_escreve_comando_progresso_e_resumo_na_Saida_sem_abrir_dialogo()
    {
        _repo.AvancarOrigin();
        ResumoDoLote<ResultadoDeFetch>? recebido = null;
        _dialogos.ResumoPronto += r => recebido = (ResumoDoLote<ResultadoDeFetch>)r;
        var fetch = new FetchEmLote(_projetos, _dialogos, _coordenador, _git);

        var r = await _coordenador.ExecutarAsync(fetch, new OpcoesDeFetch());

        r!.EhOk.Should().BeTrue();
        recebido.Should().NotBeNull();
        _interno.Resumos.Should().BeEmpty("o resumo vai para a Saída e para o aviso do canto, não para um diálogo");
        var texto = _saida.Texto();
        texto.Should().Contain("> Fetch").And.Contain("[1/1] p1...").And.Contain("== Fetch: 1 ok").And.Contain("✓ p1: Atualizado");
        _saida.Linhas.Should().Contain(l => l.Tipo == TipoDeLinha.Comando).And.Contain(l => l.Tipo == TipoDeLinha.Ok);
        _coordenador.EmAndamento.Should().BeFalse();
    }

    [Fact]
    public async Task Pull_traz_o_commit_do_origin()
    {
        _repo.AvancarOrigin();
        var pull = new PullEmLote(_projetos, _dialogos, _coordenador, _git, _consultas);

        var r = await _coordenador.ExecutarAsync(pull, new OpcoesDePull());

        r!.Valor.Oks.Should().Be(1);
        _repo.Rodar("log", "-1", "--format=%s").Should().Be("Commit remoto");
    }

    [Fact]
    public async Task Sync_preserva_alteracoes_locais()
    {
        _repo.AvancarOrigin();
        File.WriteAllText(Path.Combine(_repo.Caminho, "README.md"), "alterado\n");
        var guarda = new GuardaDePendencias(_git, _consultas);
        var sync = new SyncEmLote(_projetos, _dialogos, _coordenador, _git, _consultas, guarda);

        var r = await _coordenador.ExecutarAsync(sync, new OpcoesDeSync());

        r!.Valor.Oks.Should().Be(1);
        _repo.Rodar("log", "-1", "--format=%s").Should().Be("Commit remoto");
        File.ReadAllText(Path.Combine(_repo.Caminho, "README.md")).Should().Be("alterado\n");
    }

    [Fact]
    public async Task Checkout_com_auto_stash_troca_de_branch_e_guarda_as_pendencias()
    {
        File.WriteAllText(Path.Combine(_repo.Caminho, "README.md"), "pendente\n");
        var checkout = new CheckoutEmLote(_projetos, _dialogos, _coordenador, _git, _consultas,
            new AutoStash(_git, _consultas), new AjustadorDeFrameworks(_git, _consultas, _projetos, new ModoBetaFixo()));

        var r = await _coordenador.ExecutarAsync(checkout, new OpcoesDeCheckout("develop", AutoStash: true));

        r!.Valor.Oks.Should().Be(1);
        _repo.BranchAtual.Should().Be("develop");
        _repo.Rodar("stash", "list").Should().Contain("GM-AUTO|main|", "as pendências ficaram guardadas para quando voltar para a main");
    }

    [Fact]
    public async Task Stash_com_nome_por_projeto()
    {
        File.WriteAllText(Path.Combine(_repo.Caminho, "README.md"), "mexi\n");
        var stash = new StashEmLote(_projetos, _dialogos, _coordenador, _git, _consultas);

        var r = await _coordenador.ExecutarAsync(stash, new OpcoesDeStash([new(_projetos.Todos[0], "minha-tarefa")]));

        r!.Valor.Oks.Should().Be(1);
        _repo.Rodar("stash", "list").Should().Contain("minha-tarefa");
        _saida.Texto().Should().Contain("stash \"minha-tarefa\" criado");
    }

    [Fact]
    public async Task Sem_projeto_marcado_o_erro_vai_para_a_Saida()
    {
        _projetos.MarcarGrupo(GruposEspeciais.Todos, false);
        var fetch = new FetchEmLote(_projetos, _dialogos, _coordenador, _git);

        var r = await _coordenador.ExecutarAsync(fetch, new OpcoesDeFetch());

        r!.EhOk.Should().BeFalse();
        _saida.Linhas.Should().Contain(l => l.Tipo == TipoDeLinha.Erro && l.Texto.Contains("Nenhum projeto marcado"));
    }

    [Fact]
    public async Task Parar_apos_o_atual_deixa_o_primeiro_terminar_e_nao_comeca_o_segundo()
    {
        var segundo = RepositorioDescartavel.Criar();
        try
        {
            _projetos.Adicionar(segundo.ComoProjeto("p2"));
            _projetos.Marcar(segundo.Caminho, true);
            var operacao = new OperacaoLenta(_projetos, _dialogos, _coordenador);

            var execucao = _coordenador.ExecutarAsync(operacao, 0);
            await operacao.PrimeiroIniciou.Task;
            _coordenador.PararApos();
            operacao.Liberar.SetResult();
            var r = await execucao;

            r!.Valor.Interrompido.Should().BeTrue();
            r.Valor.Itens.Should().ContainSingle();
            r.Valor.NaoExecutados.Should().Equal("p2");
            _saida.Texto().Should().Contain("Parando após o projeto atual").And.Contain("Não executados: p2");
        }
        finally
        {
            segundo.Dispose();
        }
    }

    [Fact]
    public async Task Segunda_operacao_enquanto_uma_roda_e_recusada()
    {
        var operacao = new OperacaoLenta(_projetos, _dialogos, _coordenador);
        var primeira = _coordenador.ExecutarAsync(operacao, 0);
        await operacao.PrimeiroIniciou.Task;

        (await _coordenador.ExecutarAsync(new OperacaoLenta(_projetos, _dialogos, _coordenador), 0)).Should().BeNull();

        operacao.Liberar.SetResult();
        await primeira;
    }

    sealed class OperacaoLenta(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso)
        : OperacaoEmLote<int, int>(projetos, dialogos, progresso)
    {
        public TaskCompletionSource PrimeiroIniciou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string Id => "teste.lenta";

        public override string Nome => "Lenta";

        protected override async Task<ResultadoDoProjeto<int>> ExecutarEmProjeto(Projeto projeto, int opcoes, CancellationToken cancelar)
        {
            PrimeiroIniciou.TrySetResult();
            await Liberar.Task;
            return ResultadoDoProjeto<int>.Ok(1, "feito");
        }
    }
}
