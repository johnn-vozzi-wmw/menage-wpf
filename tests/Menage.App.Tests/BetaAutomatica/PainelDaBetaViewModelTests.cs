using Menage.App.Controles;
using Menage.App.Modulos.BetaAutomatica;
using Menage.Core.BetaAutomatica;
using Menage.Core.Perfis;
using static Menage.App.Tests.BetaAutomatica.CenarioDasAbas;

namespace Menage.App.Tests.BetaAutomatica;

public sealed class PainelDaBetaViewModelTests : IDisposable
{
    readonly CenarioDasAbas _c = new();

    public void Dispose() => _c.Dispose();

    [Fact]
    public void Abre_na_aba_Betas_com_as_quatro_abas_e_o_console_ancorado_expandido()
    {
        var vm = _c.NovoPainel();

        vm.AbaAtual.Should().Be(AbaDaBeta.Betas);
        vm.Posicao.Should().Be(0);
        vm.Abas.Select(a => a.Titulo).Should().Equal("Betas", "Configurar", "JQL", "Diagnóstico");
        vm.Console.Expandido.Should().BeTrue();
    }

    [Fact]
    public void Selecionar_aba_troca_a_aba_atual_e_a_posicao_da_transicao()
    {
        var vm = _c.NovoPainel();

        vm.SelecionarAba(AbaDaBeta.Jql);
        vm.AbaAtual.Should().Be(AbaDaBeta.Jql);
        vm.Posicao.Should().Be(2);

        vm.AbaSelecionada = vm.Abas[3];
        vm.AbaAtual.Should().Be(AbaDaBeta.Diagnostico);
    }

    [Fact]
    public void Pedido_de_comando_leva_a_aba_certa_e_o_console_expande_sem_trocar_de_aba()
    {
        var vm = _c.NovoPainel();
        vm.Abrir(new PedidoDeAba(AbaDaBeta.Configurar));
        vm.Console.Expandido = false;

        vm.Abrir(new PedidoDeAba(null, null, Console: true));

        vm.AbaAtual.Should().Be(AbaDaBeta.Configurar);
        vm.Console.Expandido.Should().BeTrue();
    }

    [Fact]
    public void Pedido_com_chave_abre_a_aba_Betas_com_a_atividade_selecionada()
    {
        _c.Itens(Item("VENDAS-1", EstadoDoItem.Pronta), Item("VENDAS-2", EstadoDoItem.Aguardando));
        var vm = _c.NovoPainel();
        vm.SelecionarAba(AbaDaBeta.Jql);

        vm.Abrir(new PedidoDeAba(AbaDaBeta.Betas, "vendas-2"));

        vm.AbaAtual.Should().Be(AbaDaBeta.Betas);
        vm.Selecionado!.Chave.Should().Be("VENDAS-2");
    }

    [Fact]
    public void Contador_da_aba_Betas_conta_a_lista_e_fica_em_alerta_so_com_beta_com_problema()
    {
        _c.Itens(Item("VENDAS-1", EstadoDoItem.Pronta), Item("VENDAS-2", EstadoDoItem.Aguardando), Item("VENDAS-3", EstadoDoItem.Ignorada));
        var vm = _c.NovoPainel();

        vm.Abas[0].Contador.Should().Be(2);
        vm.Abas[0].TemContador.Should().BeTrue();
        vm.Abas[0].Alerta.Should().BeFalse();
        vm.Abas[1].TemContador.Should().BeFalse();

        _c.Itens(Item("VENDAS-1", EstadoDoItem.Pronta), Item("VENDAS-4", EstadoDoItem.Falhou));
        vm.Atualizar();

        vm.Abas[0].Contador.Should().Be(2);
        vm.Abas[0].Alerta.Should().BeTrue();
    }

    [Fact]
    public void Gavetas_mostram_a_quantidade_de_ignoradas_e_finalizadas()
    {
        _c.Itens(Item("VENDAS-1", EstadoDoItem.Pronta), Item("VENDAS-2", EstadoDoItem.Ignorada), Item("VENDAS-3", EstadoDoItem.Finalizada), Item("VENDAS-4", EstadoDoItem.Finalizada));
        var vm = _c.NovoPainel();

        vm.TituloIgnoradas.Should().Be("Ignoradas (1)");
        vm.TituloFinalizadas.Should().Be("Finalizadas (2)");
    }

    [Fact]
    public void A_selecao_sobrevive_a_uma_atualizacao_e_cai_na_primeira_se_a_atividade_sumiu()
    {
        _c.Itens(Item("VENDAS-1", EstadoDoItem.Pronta), Item("VENDAS-2", EstadoDoItem.Pronta));
        var vm = _c.NovoPainel();
        vm.SelecionarBeta("VENDAS-2");

        vm.Atualizar();
        vm.Selecionado!.Chave.Should().Be("VENDAS-2");

        _c.Itens(Item("VENDAS-1", EstadoDoItem.Pronta));
        vm.Atualizar();
        vm.Selecionado!.Chave.Should().Be("VENDAS-1");
    }

    [Fact]
    public void Monitor_alterna_entre_Pausar_e_Retomar_e_o_status_diz_qual_esta()
    {
        var vm = _c.NovoPainel();
        vm.MonitorAtivo.Should().BeFalse();
        vm.TextoDoMonitor.Should().Be("Retomar monitor");
        vm.Status.Should().StartWith("Monitor pausado");

        _c.Servico.Alterar(c => c.Ativo = true);
        vm.Atualizar();

        vm.TextoDoMonitor.Should().Be("Pausar monitor");
        vm.StatusCurto.Should().Be("Monitor ativo · a cada 3 min");
    }

    [Fact]
    public void Perfil_Tester_aparece_no_painel()
    {
        using var tester = new CenarioDasAbas(PerfilDeUso.Tester);

        tester.NovoPainel().PerfilTexto.Should().Be("Perfil Tester");
        _c.NovoPainel().PerfilTexto.Should().Be("Perfil Desenvolvedor");
    }

    [Fact]
    public void Cartao_mostra_progresso_cor_do_estado_e_checklist_dos_repositorios()
    {
        _c.Itens(
            Item("VENDAS-1", EstadoDoItem.Pronta, Projeto("lavenderecore", SituacaoDaBranch.Local), Projeto("lavendereweb", SituacaoDaBranch.Ausente)),
            Item("VENDAS-2", EstadoDoItem.Aguardando, Projeto("lavenderecore", SituacaoDaBranch.Ausente)),
            Item("VENDAS-3", EstadoDoItem.Gerando, Projeto("lavenderecore", SituacaoDaBranch.Local, ResultadoGerado.Gerando)),
            Item("VENDAS-4", EstadoDoItem.Falhou, Projeto("lavenderecore", SituacaoDaBranch.Local, ResultadoGerado.Falhou)));
        var cartoes = _c.NovoPainel().Cartoes.ToDictionary(x => x.Chave);

        cartoes["VENDAS-1"].Progresso.Should().Be("1/2");
        cartoes["VENDAS-1"].Tom.Should().Be(TomDeCor.Verde);
        cartoes["VENDAS-1"].Projetos.Select(p => p.Situacao).Should().Equal("pronto", "problema");
        cartoes["VENDAS-1"].Projetos.Select(p => p.Marca).Should().Equal("✓", "✕");
        cartoes["VENDAS-2"].Tom.Should().Be(TomDeCor.Amarelo);
        cartoes["VENDAS-2"].Projetos[0].Situacao.Should().Be("aguardando");
        cartoes["VENDAS-3"].Tom.Should().Be(TomDeCor.Azul);
        cartoes["VENDAS-3"].TextoGerar.Should().Be("Gerando...");
        cartoes["VENDAS-4"].Tom.Should().Be(TomDeCor.Vermelho);
        cartoes["VENDAS-4"].ComProblema.Should().BeTrue();
    }

    [Fact]
    public void Aviso_cita_os_repositorios_sem_a_branch_e_fica_vermelho_quando_ha_erro()
    {
        var comErro = Item("VENDAS-2", EstadoDoItem.Falhou, Projeto("lavendereweb", SituacaoDaBranch.Local, ResultadoGerado.Falhou));
        comErro.Erro = "Falhou em lavendereweb.";
        _c.Itens(Item("VENDAS-1", EstadoDoItem.Pronta, Projeto("lavenderecore", SituacaoDaBranch.Local), Projeto("lavendereweb", SituacaoDaBranch.Ausente)), comErro);
        var cartoes = _c.NovoPainel().Cartoes.ToDictionary(x => x.Chave);

        cartoes["VENDAS-1"].Aviso.Should().Contain("VENDAS-1").And.Contain("lavendereweb");
        cartoes["VENDAS-1"].AvisoTom.Should().Be(TomDeCor.Amarelo);
        cartoes["VENDAS-2"].Aviso.Should().Be("Falhou em lavendereweb.");
        cartoes["VENDAS-2"].AvisoTom.Should().Be(TomDeCor.Vermelho);
    }

    [Fact]
    public async Task Ignorar_move_a_beta_para_a_gaveta_e_remover_pede_confirmacao()
    {
        _c.Itens(Item("VENDAS-1", EstadoDoItem.Pronta), Item("VENDAS-2", EstadoDoItem.Pronta));
        var vm = _c.NovoPainel();

        await vm.ExecutarAsync("ignorar", "VENDAS-1");
        vm.Atualizar();
        vm.Cartoes.Select(x => x.Chave).Should().Equal("VENDAS-2");
        vm.Ignoradas.Select(x => x.Chave).Should().Equal("VENDAS-1");

        _c.Dialogos.RespostaDaConfirmacao = false;
        await vm.ExecutarAsync("remover", "VENDAS-2");
        _c.Dialogos.Previas.Should().ContainSingle();
        _c.Servico.Itens().Should().ContainSingle(i => i.Chave == "VENDAS-2");
    }

    [Fact]
    public void Entrar_na_aba_Configurar_descarta_o_que_nao_foi_salvo_so_quando_nao_ha_alteracao_pendente()
    {
        var vm = _c.NovoPainel();
        vm.Configurar.Intervalo = "9";

        vm.SelecionarAba(AbaDaBeta.Configurar);

        vm.Configurar.Intervalo.Should().Be("9");
        vm.Configurar.Alterado.Should().BeTrue();
    }
}
