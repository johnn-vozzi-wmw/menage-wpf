using System.IO;
using Menage.App.Galeria;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.Core.Armazenamento;
using Menage.Core.Banco;
using Menage.Core.Comandos;
using Menage.Core.Estado;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Perfis;
using Menage.Core.Projetos;
using Menage.Core.Saida;
using Menage.TestKit;

namespace Menage.App.Tests.JanelaPrincipal;

public sealed class JanelaPrincipalViewModelsTests : IDisposable
{
    sealed class RelogioManual : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Agora;
    }

    sealed class LeitorContador(ILeitorDeSituacao interno) : ILeitorDeSituacao
    {
        public int Leituras { get; private set; }

        public Task<IReadOnlyList<SituacaoDoProjeto>> LerTodosAsync(IReadOnlyList<Projeto> projetos, CancellationToken cancelar = default)
        {
            Leituras++;
            return interno.LerTodosAsync(projetos, cancelar);
        }
    }

    sealed class OperacaoParada(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso)
        : OperacaoEmLote<int, int>(projetos, dialogos, progresso)
    {
        public TaskCompletionSource Iniciou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string Id => "t.parada";

        public override string Nome => "Parada";

        protected override async Task<ResultadoDoProjeto<int>> ExecutarEmProjeto(Projeto projeto, int opcoes, CancellationToken cancelar)
        {
            Iniciou.TrySetResult();
            await Liberar.Task;
            return ResultadoDoProjeto<int>.Ok(1);
        }
    }

    readonly string _pasta = Path.Combine(Path.GetTempPath(), "mg" + Guid.NewGuid().ToString("N")[..6]);
    readonly ConfiguracaoDeExemplo _configuracao = new();
    readonly ProjetosConfigurados _projetos;
    readonly SaidaDeOperacoes _saida = new();
    readonly CoordenadorDeOperacoes _coordenador;
    readonly PreferenciasDaJanelaServico _preferencias;
    readonly RelogioManual _relogio = new();
    readonly LeitorContador _leitor = new(new LeitorDeExemplo());

    public JanelaPrincipalViewModelsTests()
    {
        _projetos = new ProjetosConfigurados(_configuracao);
        _coordenador = new CoordenadorDeOperacoes(new ExecutorDeOperacoes(), _saida);
        _preferencias = new PreferenciasDaJanelaServico(new Armazenamento<PreferenciasDaJanela>(new PastaDeDados(_pasta), "janela-principal"));
    }

    public void Dispose()
    {
        _coordenador.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    GradeDeProjetosViewModel Grade() => new(_projetos, _configuracao, _leitor, _coordenador, _relogio);

    BarraLateralViewModel Barra() => new(_projetos, _configuracao, new ServicoDeBancoFalso(), _preferencias,
            new Menage.Core.Personalizacao.ServicoDePreferencias(new Armazenamento<Menage.Core.Personalizacao.PreferenciasDeAparencia>(new PastaDeDados(_pasta), "aparencia", padrao: () => new Menage.Core.Personalizacao.PreferenciasDeAparencia())),
            new Menage.Core.Perfis.ServicoDePerfis(new PastaDeDados(_pasta)));

    [Fact]
    public void Clique_no_grupo_deixa_so_ele_marcado_e_clicar_de_novo_limpa()
    {
        var barra = Barra();
        var web = barra.Grupos.First(g => g.Id == "WEB");
        var fw = barra.Grupos.First(g => g.Id == "FRAMEWORK");

        barra.AlternarGrupo(web, somar: false);
        web.Texto.Should().Be("3/3");
        barra.AlternarGrupo(fw, somar: false);
        web.Marcados.Should().Be(0, "o clique simples troca a selecao");
        fw.Completo.Should().BeTrue();

        barra.AlternarGrupo(fw, somar: false);
        _projetos.Marcados.Should().BeEmpty();
    }

    [Fact]
    public void Shift_soma_grupos_e_tira_o_grupo_quando_ja_esta_inteiro()
    {
        var barra = Barra();
        var web = barra.Grupos.First(g => g.Id == "WEB");
        var fw = barra.Grupos.First(g => g.Id == "FRAMEWORK");

        barra.AlternarGrupo(web, somar: true);
        barra.AlternarGrupo(fw, somar: true);
        _projetos.Marcados.Should().HaveCount(6);
        barra.Grupos.First(g => g.EhTodos).Texto.Should().Be("6/9");
        barra.Grupos.First(g => g.EhTodos).Fracao.Should().BeApproximately(6.0 / 9, 0.001);

        barra.AlternarGrupo(web, somar: true);
        _projetos.Marcados.Select(p => p.Nome).Should().OnlyContain(n => n.StartsWith("framework", StringComparison.Ordinal));
    }

    [Fact]
    public void Grupo_Todos_marca_tudo_e_Limpar_selecao_desmarca()
    {
        var barra = Barra();
        barra.AlternarGrupo(barra.Grupos.First(g => g.EhTodos), somar: false);
        _projetos.Marcados.Should().HaveCount(9);
        barra.LimparSelecaoCommand.Execute(null);
        _projetos.Marcados.Should().BeEmpty();
    }

    [Fact]
    public void Marcar_pela_linha_sincroniza_projetos_e_cabecalho()
    {
        var grade = Grade();
        grade.TodosMarcados.Should().BeFalse();

        grade.Visiveis[0].Marcado = true;
        _projetos.Marcados.Should().ContainSingle();
        grade.TodosMarcados.Should().BeNull("so alguns marcados");

        grade.AlternarTodosCommand.Execute(null);
        grade.TodosMarcados.Should().BeTrue();
        _projetos.Marcados.Should().HaveCount(9);
    }

    [Fact]
    public void Marcar_pelo_grupo_atualiza_as_linhas()
    {
        var grade = Grade();
        _projetos.MarcarGrupo("APP", true);
        grade.Visiveis.Where(i => i.Marcado).Select(i => i.Nome).Should().Equal("lavenderepda");
    }

    [Fact]
    public void Busca_filtra_e_estado_vazio_cita_o_texto()
    {
        var grade = Grade();
        grade.Busca = "FRAMEWORK";
        grade.Visiveis.Should().HaveCount(4);
        grade.SemLinhas.Should().BeFalse();

        grade.Busca = "nada-assim";
        grade.SemLinhas.Should().BeTrue();
        grade.TituloVazio.Should().Be("Nenhum projeto combina com \"nada-assim\"");

        grade.LimparBusca();
        grade.Visiveis.Should().HaveCount(9);
    }

    [Fact]
    public void Marcar_visiveis_marca_so_os_filtrados_e_a_marcacao_sobrevive_a_busca()
    {
        var grade = Grade();
        grade.Busca = "lavendere";
        grade.MarcarVisiveis(true);
        _projetos.Marcados.Should().HaveCount(4);
        grade.Busca = "";
        grade.QuantidadeMarcada.Should().Be(4);
    }

    [Fact]
    public async Task Situacao_lida_preenche_icones_etiquetas_e_branch()
    {
        var grade = Grade();
        await grade.LerAsync();

        var web = grade.Visiveis.First(i => i.Nome == "lavendereweb");
        web.TemAlteracoes.Should().BeTrue();
        web.Limpo.Should().BeFalse();
        web.TipoDaBranch.Should().Be(TipoDeBranch.Atividade);
        web.ArquivosTexto.Should().Be("4");
        web.Versao.Should().Be("9.4.1");
        web.EtiquetasDeOrigin.Select(e => e.Texto).Should().Equal("↑2", "↓1");
        grade.Visiveis.First(i => i.Nome == "lavenderecore").Limpo.Should().BeTrue();
        grade.Visiveis.First(i => i.Nome == "framework-core").TipoDaBranch.Should().Be(TipoDeBranch.Detached);
        grade.Visiveis.First(i => i.Nome == "framework-tc").Lido.Should().BeFalse("falha de leitura");
    }

    [Fact]
    public async Task Leitura_ao_voltar_o_foco_respeita_o_intervalo_de_5_segundos()
    {
        var grade = Grade();
        await grade.LerAsync();
        _leitor.Leituras.Should().Be(1);

        _relogio.Agora += TimeSpan.FromSeconds(4);
        await grade.AtualizarAoFocarAsync();
        _leitor.Leituras.Should().Be(1, "menos de 5 s desde a ultima leitura");

        _relogio.Agora += TimeSpan.FromSeconds(2);
        await grade.AtualizarAoFocarAsync();
        _leitor.Leituras.Should().Be(2);

        await grade.AtualizarAsync();
        _leitor.Leituras.Should().Be(3, "F5 ignora o intervalo");
    }

    [Fact]
    public async Task Nao_le_durante_operacao()
    {
        var grade = Grade();
        var operacao = new OperacaoParada(_projetos, new DialogosFalsos(), _coordenador);
        _projetos.MarcarGrupo(GruposEspeciais.Todos, true);
        var execucao = _coordenador.ExecutarAsync(operacao, 0);
        await operacao.Iniciou.Task;

        await grade.LerAsync();
        _leitor.Leituras.Should().Be(0);

        operacao.Liberar.SetResult();
        await execucao;
    }

    [Fact]
    public void Perfil_e_motivos_bloqueiam_pilulas_com_dica()
    {
        var catalogo = new CatalogoDeComandos();
        catalogo.Adicionar(new Comando("git.fetch", "Fetch", _ => Task.CompletedTask, Perfis: ConjuntosDePerfis.Somente(PerfilDeUso.Tester)));
        catalogo.Adicionar(new Comando("git.pull", "Pull", _ => Task.CompletedTask, Habilitado: () => _projetos.Marcados.Count > 0));
        catalogo.Adicionar(new Comando("git.cherry-pick", "Cherry-pick", _ => Task.CompletedTask, Habilitado: () => false));
        var barra = new BarraDeAcoesViewModel(catalogo, new PerfilFixo(PerfilDeUso.Geral), _coordenador, _projetos);

        barra.Acoes[0].Motivo.Should().Be("Disponível só no perfil Tester");
        barra.Acoes[1].Motivo.Should().Be("Marque ao menos um projeto na grade");
        barra.Acoes[5].Motivo.Should().Be("Marque ao menos um projeto na grade", "cherry-pick agora é real; só falta selecionar");
        barra.Acoes[3].Motivo.Should().Be("Em breve", "comando ausente do catalogo");

        _projetos.MarcarGrupo("WEB", true);
        barra.Acoes[1].Habilitada.Should().BeTrue("a selecao reavalia as pilulas");
    }

    [Fact]
    public void Saida_abre_sozinha_em_erro_e_Limpar_esvazia()
    {
        var saida = new SaidaViewModel(_saida, _preferencias);
        saida.Alternar();
        saida.Recolhida.Should().BeTrue();

        _saida.Escrever("tudo certo", TipoDeLinha.Ok);
        saida.Recolhida.Should().BeTrue();
        saida.UltimaLinha!.Texto.Should().Be("tudo certo");

        _saida.Escrever("falhou", TipoDeLinha.Erro);
        saida.Recolhida.Should().BeFalse();
        _preferencias.Valor.SaidaRecolhida.Should().BeFalse();

        _saida.Limpar();
        saida.Linhas.Should().BeEmpty();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(30)]
    public void Altura_a_grade_ocupa_so_as_linhas_e_a_Saida_fica_com_o_resto(int linhas)
    {
        var a = CalculoDeAlturas.Calcular(700, linhas, 190, recolhida: false);
        (a.Grade + a.Saida + CalculoDeAlturas.Divisoria).Should().BeApproximately(700, 0.01);
        if (linhas == 4) a.Saida.Should().BeGreaterThan(190, "poucas linhas: sobra espaco para a Saida");
        else a.Saida.Should().Be(190, "muitas linhas: a Saida fica no que o usuario arrastou e a grade rola");
    }

    [Fact]
    public void Altura_recolhida_deixa_a_grade_com_quase_tudo_e_arrastar_respeita_os_limites()
    {
        var recolhida = CalculoDeAlturas.Calcular(700, 4, 190, recolhida: true);
        recolhida.Saida.Should().Be(CalculoDeAlturas.SaidaRecolhida);
        recolhida.Grade.Should().BeGreaterThan(600);

        CalculoDeAlturas.AposArrastar(190, 1000, 700).Should().Be(CalculoDeAlturas.SaidaMinima);
        CalculoDeAlturas.AposArrastar(190, -1000, 700).Should().Be(700 - CalculoDeAlturas.Divisoria - CalculoDeAlturas.GradeMinima);
        CalculoDeAlturas.AposArrastar(190, -20, 700).Should().Be(210, "arrastar para cima aumenta a Saida");
    }

    [Fact]
    public void Estado_da_barra_lateral_e_salvo()
    {
        var barra = Barra();
        barra.Alternar();
        barra.Recolhida.Should().BeTrue();
        new PreferenciasDaJanelaServico(new Armazenamento<PreferenciasDaJanela>(new PastaDeDados(_pasta), "janela-principal"))
            .Valor.BarraRecolhida.Should().BeTrue();
    }
}
