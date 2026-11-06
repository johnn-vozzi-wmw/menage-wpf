using Menage.Core.BetaAutomatica;

namespace Menage.Tests.BetaAutomatica;

/// <summary>Regras puras que alimentam as abas da janela da Beta automática (filtro do console, listas de chips, marcadores da JQL, leitura do diagnóstico).</summary>
public sealed class AbasDaBetaCoreTests
{
    static RegistroDoConsole Registro(NivelDoConsole nivel, string origem = "Monitor", string atividade = "", string projeto = "", string texto = "t") =>
        new(new DateTime(2026, 11, 6, 9, 0, 0), nivel, origem, atividade, projeto, texto);

    [Theory]
    [InlineData(NivelDeFiltro.SemSaida, NivelDoConsole.Saida, false)]
    [InlineData(NivelDeFiltro.SemSaida, NivelDoConsole.Comando, true)]
    [InlineData(NivelDeFiltro.Tudo, NivelDoConsole.Saida, true)]
    [InlineData(NivelDeFiltro.Problemas, NivelDoConsole.Aviso, true)]
    [InlineData(NivelDeFiltro.Problemas, NivelDoConsole.Erro, true)]
    [InlineData(NivelDeFiltro.Problemas, NivelDoConsole.Info, false)]
    [InlineData(NivelDeFiltro.Comandos, NivelDoConsole.Comando, true)]
    [InlineData(NivelDeFiltro.Comandos, NivelDoConsole.Erro, false)]
    public void Filtro_do_console_por_nivel(NivelDeFiltro filtro, NivelDoConsole nivel, bool passa) =>
        new FiltroDoConsole(filtro).Passa(Registro(nivel)).Should().Be(passa);

    [Fact]
    public void Filtro_do_console_combina_origem_atividade_e_busca_sem_diferenciar_maiusculas()
    {
        var r = Registro(NivelDoConsole.Info, FiltroDoConsole.OrigemBeta, "VENDAS-12", "lavenderecore", "Git Fetch origin");

        new FiltroDoConsole(NivelDeFiltro.Tudo, FiltroDoConsole.OrigemBeta, "vendas-12", "fetch").Passa(r).Should().BeTrue();
        new FiltroDoConsole(NivelDeFiltro.Tudo, FiltroDoConsole.OrigemMonitor).Passa(r).Should().BeFalse();
        new FiltroDoConsole(NivelDeFiltro.Tudo, Atividade: "VENDAS-99").Passa(r).Should().BeFalse();
        new FiltroDoConsole(NivelDeFiltro.Tudo, Busca: "LAVENDERECORE").Passa(r).Should().BeTrue();
        new FiltroDoConsole(NivelDeFiltro.Tudo, Busca: "outra coisa").Passa(r).Should().BeFalse();
    }

    [Fact]
    public void Lista_separada_le_e_junta_sem_repetir_nem_deixar_vazios()
    {
        ListaSeparada.Ler(" VENDAS, PDA ;vendas,, ").Should().Equal("VENDAS", "PDA");
        ListaSeparada.Ler("Waiting for Testing, Testing").Should().Equal("Waiting for Testing", "Testing");
        ListaSeparada.Ler("VENDAS PDA", separarPorEspaco: true).Should().Equal("VENDAS", "PDA");
        ListaSeparada.Ler(null).Should().BeEmpty();
        ListaSeparada.Juntar(["A", " B ", "", "a"]).Should().Be("A,B");
    }

    [Fact]
    public void Config_aceita_varios_status_finais_e_um_so_continua_valendo()
    {
        new ConfigBetaAuto { StatusFinal = "Waiting for Product Increment" }.StatusFinais.Should().Equal("Waiting for Product Increment");
        new ConfigBetaAuto { StatusFinal = "Concluído, Homologação" }.StatusFinais.Should().Equal("Concluído", "Homologação");
    }

    [Fact]
    public void Finalizacao_automatica_vale_para_qualquer_um_dos_status_finais()
    {
        using var cenario = new CenarioDaBeta();
        cenario.Servico.Alterar(c => c.StatusFinal = "Concluído,Homologação");

        cenario.Servico.MotivoDeFinalizacao(new IssueJira { Chave = "V-1", Status = "homologação" }).Should().Contain("Homologação");
        cenario.Servico.MotivoDeFinalizacao(new IssueJira { Chave = "V-2", Status = "Concluído" }).Should().Contain("Concluído");
        cenario.Servico.MotivoDeFinalizacao(new IssueJira { Chave = "V-3", Status = "Em andamento" }).Should().BeNull();
    }

    [Fact]
    public void Marcadores_da_jql_sao_separados_do_texto_comum()
    {
        var trechos = MarcadoresDeJql.Separar("{projeto} AND status = \"{status}\"{escopo}");

        trechos.Select(t => (t.Texto, t.Marcador)).Should().Equal(
            ("{projeto}", true), (" AND status = \"", false), ("{status}", true), ("\"", false), ("{escopo}", true));
        MarcadoresDeJql.Separar("sem marcador").Should().ContainSingle().Which.Marcador.Should().BeFalse();
        MarcadoresDeJql.Separar(null).Should().BeEmpty();
    }

    const string Simulacao =
        """
        === DIAGNÓSTICO DA BUSCA NO JIRA (simulação: nada é alterado) ===
        Jira: https://jira   token (JIRA_TOKEN): definido

        --- VENDAS-1  "Corrige o desconto"
            tipo: Story   status: Waiting for Testing
            DECISÃO: NOVA - comentário seu
            escopo: ENTRA - comentário seu com BRANCH
              [ok] lavenderecore (citado) - branch local
              [--] lavendereweb (citado) - não achei
              [tag] lavenderedados (complemento da beta web) - tag 9.060
            verificação (sem alterar nada): ficaria "Pronta para gerar"

        --- VENDAS-2  "Não é minha"
            DECISÃO: FORADOESCOPO - atribuída a outra pessoa
            escopo: FORA - atribuída a outra pessoa

        --- VENDAS-3  "Já na fila"
            DECISÃO: JATRATADA - já está na fila

        --- Atividades que estão na lista do programa mas NÃO vieram nesta busca:
            VENDAS-9 - "Pronta para gerar"

        === RESUMO: 3 atividade(s) devolvida(s) pelo Jira ===
        NOVA/REENTRADA = entram na fila
        """;

    [Fact]
    public void Diagnostico_e_agrupado_por_atividade_com_decisao_passos_e_detalhes()
    {
        var r = InterpretadorDoDiagnostico.Interpretar(Simulacao.Split('\n'));

        r.Cabecalho.Should().HaveCount(2);
        r.Decisoes.Select(d => (d.Chave, d.Entra, d.Decisao)).Should().Equal(("VENDAS-1", true, "NOVA"), ("VENDAS-2", false, "FORADOESCOPO"), ("VENDAS-3", true, "JATRATADA"));
        r.Entram.Should().Be(2);
        r.FicamDeFora.Should().Be(1);

        var primeira = r.Decisoes[0];
        primeira.Titulo.Should().Be("Corrige o desconto");
        primeira.Passos.Select(p => p.Ok).Should().Equal(true, true, false, true);
        primeira.Passos[0].Texto.Should().Be("comentário seu com BRANCH");
        primeira.Detalhes.Should().Contain(d => d.StartsWith("verificação", StringComparison.Ordinal));
        r.Decisoes[1].Passos.Should().ContainSingle().Which.Ok.Should().BeFalse();
    }

    [Fact]
    public void Diagnostico_separa_o_que_ficou_fora_da_busca_e_o_resumo()
    {
        var r = InterpretadorDoDiagnostico.Interpretar(Simulacao.Split('\n'));

        r.ForaDaBusca.Should().ContainSingle().Which.Should().StartWith("VENDAS-9");
        r.Resumo.Should().HaveCount(2).And.Contain(l => l.StartsWith("=== RESUMO", StringComparison.Ordinal));
    }

    [Fact]
    public void Diagnostico_sem_atividades_so_traz_o_cabecalho()
    {
        var r = InterpretadorDoDiagnostico.Interpretar(["=== DIAGNÓSTICO ===", "Sem token não há como consultar."]);

        r.Decisoes.Should().BeEmpty();
        r.Cabecalho.Should().HaveCount(2);
    }
}
