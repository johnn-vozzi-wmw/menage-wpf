using System.IO;
using Menage.App.Modulos.BetaAutomatica;
using Menage.Core.BetaAutomatica;

namespace Menage.App.Tests.BetaAutomatica;

public sealed class ConsoleEDiagnosticoViewModelTests : IDisposable
{
    readonly CenarioDasAbas _c = new();

    public void Dispose() => _c.Dispose();

    static void Linhas(ConsoleDaBeta console)
    {
        console.Registrar(NivelDoConsole.Info, FiltroDoConsole.OrigemMonitor, "", "", "consulta concluída");
        console.Registrar(NivelDoConsole.Comando, FiltroDoConsole.OrigemBeta, "VENDAS-1", "lavenderecore", "git fetch origin");
        console.Registrar(NivelDoConsole.Saida, FiltroDoConsole.OrigemBeta, "VENDAS-1", "lavenderecore", "saída do maven");
        console.Registrar(NivelDoConsole.Aviso, FiltroDoConsole.OrigemBeta, "VENDAS-2", "lavendereweb", "fora do lote");
        console.Registrar(NivelDoConsole.Erro, FiltroDoConsole.OrigemBeta, "VENDAS-2", "lavendereweb", "falhou");
    }

    [Fact]
    public void Padrao_esconde_a_saida_do_maven_e_o_contador_de_erros_acompanha()
    {
        var vm = _c.NovoConsole();

        Linhas(_c.Console);

        vm.Visiveis.Select(l => l.Registro.Texto).Should().Equal("consulta concluída", "git fetch origin", "fora do lote", "falhou");
        vm.Erros.Should().Be(1);
        vm.TemErros.Should().BeTrue();
        vm.TextoDoErro.Should().Be("1");
        vm.Rodape.Should().Contain("4 linha(s) na tela de 5");
    }

    [Fact]
    public void Filtros_de_nivel_origem_atividade_e_busca_combinam()
    {
        var vm = _c.NovoConsole();
        Linhas(_c.Console);

        vm.Nivel = NivelDeFiltro.Tudo;
        vm.Visiveis.Should().HaveCount(5);

        vm.Nivel = NivelDeFiltro.Problemas;
        vm.Visiveis.Select(l => l.Registro.Texto).Should().Equal("fora do lote", "falhou");

        vm.Nivel = NivelDeFiltro.Comandos;
        vm.Visiveis.Should().ContainSingle().Which.Registro.Texto.Should().Be("git fetch origin");

        vm.Nivel = NivelDeFiltro.Tudo;
        vm.Origem = FiltroDoConsole.OrigemMonitor;
        vm.Visiveis.Should().ContainSingle();

        vm.Origem = "";
        vm.Atividade = "vendas-2";
        vm.Visiveis.Should().HaveCount(2);

        vm.Busca = "falh";
        vm.Visiveis.Should().ContainSingle().Which.Registro.Texto.Should().Be("falhou");
    }

    [Fact]
    public void Pausar_segura_as_linhas_novas_e_Retomar_despeja_tudo()
    {
        var vm = _c.NovoConsole();
        vm.Pausado = true;

        Linhas(_c.Console);

        vm.Visiveis.Should().BeEmpty();
        vm.Rodape.Should().Contain("pausado: 5 nova(s) esperando");
        vm.TextoDaPausa.Should().Be("Retomar");

        vm.Pausado = false;

        vm.Visiveis.Should().HaveCount(4);
        vm.Erros.Should().Be(1);
        vm.TextoDaPausa.Should().Be("Pausar");
    }

    [Fact]
    public void Limpar_tela_zera_as_linhas_e_os_erros_mas_o_arquivo_fica()
    {
        var vm = _c.NovoConsole();
        Linhas(_c.Console);

        vm.LimparTelaCommand.Execute(null);

        vm.Visiveis.Should().BeEmpty();
        vm.Erros.Should().Be(0);
        vm.TemErros.Should().BeFalse();
        File.Exists(_c.Console.Arquivo).Should().BeTrue();
    }

    [Fact]
    public void Acompanhar_pede_para_rolar_ao_fim_a_cada_linha_nova_e_desligado_nao_pede()
    {
        var vm = _c.NovoConsole();
        var rolagens = 0;
        vm.RolarParaOFim += () => rolagens++;

        _c.Console.Registrar(NivelDoConsole.Info, FiltroDoConsole.OrigemMonitor, "", "", "a");
        rolagens.Should().Be(1);

        vm.Acompanhar = false;
        _c.Console.Registrar(NivelDoConsole.Info, FiltroDoConsole.OrigemMonitor, "", "", "b");
        rolagens.Should().Be(1);
    }

    [Fact]
    public void Recolher_expandir_e_altura_do_dock_respeitam_os_limites()
    {
        var vm = _c.NovoConsole();
        vm.Expandido.Should().BeTrue();
        vm.TextoDoCabecalho.Should().Be("recolher ▾");

        vm.AlternarDockCommand.Execute(null);
        vm.Expandido.Should().BeFalse();
        vm.TextoDoCabecalho.Should().Be("expandir ▴");

        vm.ExpandirDock();
        vm.Expandido.Should().BeTrue();

        vm.Redimensionar(-10_000);
        vm.Altura.Should().Be(ConsoleDoDockViewModel.AlturaMaxima);
        vm.Redimensionar(10_000);
        vm.Altura.Should().Be(ConsoleDoDockViewModel.AlturaMinima);
    }

    [Fact]
    public void Texto_da_tela_so_traz_o_que_passa_pelo_filtro()
    {
        var vm = _c.NovoConsole();
        Linhas(_c.Console);
        vm.Nivel = NivelDeFiltro.Problemas;

        var linhas = vm.TextoNaTela().Split(Environment.NewLine);

        linhas.Should().HaveCount(2);
        linhas[1].Should().Contain("ERRO").And.Contain("VENDAS-2");
    }

    [Fact]
    public void Console_novo_ja_nasce_com_o_historico_da_memoria()
    {
        Linhas(_c.Console);

        var vm = _c.NovoConsole();

        vm.Visiveis.Should().HaveCount(4);
        vm.Erros.Should().Be(1);
    }

    [Fact]
    public async Task Simular_sem_token_mostra_o_motivo_e_libera_o_botao_de_novo()
    {
        _c.Jira.TemToken = false;
        var vm = _c.NovoDiagnostico();
        vm.MostrarOrientacao.Should().BeTrue();
        vm.CopiarCommand.CanExecute(null).Should().BeFalse();

        await vm.SimularCommand.ExecuteAsync(null);

        vm.Simulando.Should().BeFalse();
        vm.TemRelatorio.Should().BeTrue();
        vm.Texto.Should().Contain("NÃO definido");
        vm.MostrarOrientacao.Should().BeFalse();
        vm.SimularCommand.CanExecute(null).Should().BeTrue();
        vm.CopiarCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Simular_nao_altera_a_lista_nem_chama_nada_alem_da_busca()
    {
        _c.Jira.Issues.Add(new IssueJira { Chave = "VENDAS-5", Titulo = "Algo", Tipo = "Story", Status = "Waiting for Testing", Responsavel = "eu" });
        var vm = _c.NovoDiagnostico();

        await vm.SimularCommand.ExecuteAsync(null);

        _c.Servico.Itens().Should().BeEmpty();
        vm.Decisoes.Should().ContainSingle(d => d.Chave == "VENDAS-5");
        vm.Contagem.Should().Contain("entram").And.Contain("ficam de fora");
    }

    [Fact]
    public void Receber_monta_as_decisoes_com_a_contagem_e_o_copiar_devolve_o_texto_inteiro()
    {
        var vm = _c.NovoDiagnostico();
        string? copiado = null;
        vm.PedirCopia += t => copiado = t;

        vm.Receber(
        [
            "=== DIAGNÓSTICO ===", "", "--- VENDAS-1  \"Um\"", "    DECISÃO: NOVA - x", "    escopo: ENTRA - comentário seu",
            "", "--- VENDAS-2  \"Dois\"", "    DECISÃO: FORADOSTATUS - y", "", "=== RESUMO: 2 ===",
        ]);
        vm.CopiarCommand.Execute(null);

        vm.Decisoes.Select(d => (d.Chave, d.Entra)).Should().Equal(("VENDAS-1", true), ("VENDAS-2", false));
        vm.Contagem.Should().Be("1 entram · 1 ficam de fora");
        copiado.Should().Contain("VENDAS-2").And.Contain("RESUMO");
    }
}
