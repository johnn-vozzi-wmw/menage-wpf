namespace Menage.Tests.Operacoes;

public class OperacaoEmLoteTests
{
    /// <summary>Operação de teste: executa o que o delegate mandar, para cada projeto.</summary>
    sealed class OperacaoFalsa(
        IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso,
        Func<Projeto, CancellationToken, Task<ResultadoDoProjeto<string>>> corpo,
        bool comPrevia = false) : OperacaoEmLote<string, string>(projetos, dialogos, progresso)
    {
        public override string Id => "teste.op";

        public override string Nome => "Operação de teste";

        protected override Task<Previa?> MontarPreviaAsync(string opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
            Task.FromResult(comPrevia ? new Previa("Confirmar?", $"{alvo.Count} projeto(s)", [.. alvo.Select(p => p.Nome)]) : null);

        protected override Task<ResultadoDoProjeto<string>> ExecutarEmProjeto(Projeto projeto, string opcoes, CancellationToken cancelar) =>
            corpo(projeto, cancelar);
    }

    readonly ProjetosEmMemoria _projetos = new([new("a", "C:\\a"), new("b", "C:\\b"), new("c", "C:\\c")]);
    readonly DialogosFalsos _dialogos = new();
    readonly ProgressoGravador _progresso = new();

    OperacaoFalsa Criar(Func<Projeto, CancellationToken, Task<ResultadoDoProjeto<string>>> corpo, bool previa = false)
    {
        foreach (var p in _projetos.Todos) _projetos.Marcar(p.Caminho, true);
        return new OperacaoFalsa(_projetos, _dialogos, _progresso, corpo, previa);
    }

    static Task<ResultadoDoProjeto<string>> Ok(Projeto p, CancellationToken _) =>
        Task.FromResult(ResultadoDoProjeto<string>.Ok(p.Nome.ToUpperInvariant(), "feito"));

    [Fact]
    public async Task Percorre_os_marcados_em_ordem_e_resume_ok_aviso_e_erro()
    {
        var op = Criar((p, c) => p.Nome switch
        {
            "a" => Ok(p, c),
            "b" => Task.FromResult(ResultadoDoProjeto<string>.Aviso("atenção")),
            _ => Task.FromResult(ResultadoDoProjeto<string>.Falha(new Erro("quebrou"))),
        });

        var r = await op.ExecutarAsync("x");

        var resumo = r.Valor;
        resumo.Itens.Select(i => i.Projeto.Nome).Should().Equal("a", "b", "c");
        (resumo.Oks, resumo.Avisos, resumo.Erros).Should().Be((1, 1, 1));
        resumo.Interrompido.Should().BeFalse();
        resumo.Itens[0].Resultado.Dados.Should().Be("A");
        _dialogos.Resumos.Should().ContainSingle().Which.Should().BeSameAs(resumo);
        resumo.Linhas.Select(l => l.Situacao).Should().Equal(Situacao.Ok, Situacao.Aviso, Situacao.Erro);
    }

    [Fact]
    public async Task Sem_projeto_marcado_falha_na_validacao_sem_executar()
    {
        var chamadas = 0;
        var op = new OperacaoFalsa(_projetos, _dialogos, _progresso, (p, c) => { chamadas++; return Ok(p, c); });

        var r = await op.ExecutarAsync("x");

        r.Erro.Causa.Should().Be("Nenhum projeto marcado");
        chamadas.Should().Be(0);
        _dialogos.Resumos.Should().BeEmpty();
    }

    [Fact]
    public async Task Previa_recusada_cancela_antes_de_executar_qualquer_projeto()
    {
        _dialogos.RespostaDaConfirmacao = false;
        var chamadas = 0;
        var op = Criar((p, c) => { chamadas++; return Ok(p, c); }, previa: true);

        var r = await op.ExecutarAsync("x");

        r.Erro.Should().Be(Erro.Cancelado);
        chamadas.Should().Be(0);
        _dialogos.Previas.Single().Detalhes.Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task Previa_aceita_executa_tudo()
    {
        var r = await Criar(Ok, previa: true).ExecutarAsync("x");

        r.Valor.Oks.Should().Be(3);
        _dialogos.Previas.Should().ContainSingle();
    }

    [Fact]
    public async Task Parar_apos_o_atual_termina_o_projeto_em_curso_e_nao_comeca_o_proximo()
    {
        using var parar = new CancellationTokenSource();
        var op = Criar((p, c) =>
        {
            if (p.Nome == "a") parar.Cancel(); // o usuário clica "Parar após o atual" durante o projeto a
            return Ok(p, c);
        });

        var r = await op.ExecutarAsync("x", new SinaisDoLote(CancellationToken.None, parar.Token));

        r.Valor.Itens.Select(i => i.Projeto.Nome).Should().Equal("a");
        r.Valor.Itens[0].Resultado.Situacao.Should().Be(Situacao.Ok, "o projeto em curso termina");
        r.Valor.Interrompido.Should().BeTrue();
    }

    [Fact]
    public async Task Abortar_cancela_o_projeto_em_curso_e_marca_interrompido()
    {
        using var abortar = new CancellationTokenSource();
        var op = Criar(async (p, c) =>
        {
            abortar.Cancel();
            await Task.Delay(Timeout.Infinite, c);
            return ResultadoDoProjeto<string>.Ok("nunca");
        });

        var r = await op.ExecutarAsync("x", new SinaisDoLote(abortar.Token, CancellationToken.None));

        r.Valor.Itens.Should().ContainSingle().Which.Resultado.Erro.Should().Be(Erro.Cancelado);
        r.Valor.Interrompido.Should().BeTrue();
    }

    [Fact]
    public async Task Excecao_em_um_projeto_vira_erro_dele_e_os_outros_continuam()
    {
        var op = Criar((p, c) => p.Nome == "b" ? throw new InvalidOperationException("boom") : Ok(p, c));

        var r = await op.ExecutarAsync("x");

        r.Valor.Itens.Select(i => i.Resultado.Situacao).Should().Equal(Situacao.Ok, Situacao.Erro, Situacao.Ok);
        r.Valor.Itens[1].Resultado.Erro!.Explicacao.Should().Be("boom");
        r.Valor.Interrompido.Should().BeFalse();
    }

    [Fact]
    public async Task Progresso_informa_inicio_e_fim_de_cada_projeto_com_indice_e_total()
    {
        await Criar(Ok).ExecutarAsync("x");

        var eventos = _progresso.Eventos;
        eventos.Should().HaveCount(6);
        eventos[0].Should().Be(new EventoDeProgresso(FaseDoProjeto.Iniciou, 1, 3, "a"));
        eventos[1].Should().Be(new EventoDeProgresso(FaseDoProjeto.Terminou, 1, 3, "a", Situacao.Ok, "feito"));
        eventos[^1].Indice.Should().Be(3);
    }
}
