using Menage.App.Modulos.BetaAutomatica;
using Menage.Core.Perfis;

namespace Menage.App.Tests.BetaAutomatica;

public sealed class ConfigBetaViewModelTests : IDisposable
{
    readonly CenarioDasAbas _c = new();

    public void Dispose() => _c.Dispose();

    [Fact]
    public void Carrega_todos_os_campos_da_configuracao_gravada()
    {
        _c.Servico.Alterar(c =>
        {
            c.ChavesJira = "VENDAS,PDA";
            c.StatusFinal = "Concluído,Homologação";
            c.TiposCoding = "Coding,Código";
            c.IntervaloMin = 5;
        });

        var vm = _c.NovaConfig();

        vm.JiraUrl.Should().Be(_c.Servico.Config.JiraUrl);
        vm.VariavelDoToken.Should().Be("JIRA_TOKEN");
        vm.Chaves.Should().Equal("VENDAS", "PDA");
        vm.StatusFinais.Should().Equal("Concluído", "Homologação");
        vm.TiposCoding.Should().Equal("Coding", "Código");
        vm.Intervalo.Should().Be("5");
        vm.StatusAlvo.Should().Be("Waiting for Testing");
        vm.StatusDev.Should().Be("In Development");
        vm.StatusRevisao.Should().Be("Waiting for Code Review");
        vm.StatusOrigem.Should().Be("In Code Review");
        vm.PastaTrabalho.Should().Be(_c.Servico.Config.PastaTrabalho);
        vm.Alterado.Should().BeFalse();
    }

    [Fact]
    public void Salvar_e_Cancelar_ficam_desabilitados_ate_haver_alteracao()
    {
        var vm = _c.NovaConfig();
        vm.SalvarCommand.CanExecute(null).Should().BeFalse();
        vm.CancelarCommand.CanExecute(null).Should().BeFalse();

        vm.StatusAlvo = "Ready for Beta";

        vm.Alterado.Should().BeTrue();
        vm.SalvarCommand.CanExecute(null).Should().BeTrue();
        vm.CancelarCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Voltar_ao_valor_original_tira_a_alteracao_pendente()
    {
        var vm = _c.NovaConfig();
        var original = vm.JiraUrl;

        vm.JiraUrl = "https://outro";
        vm.Alterado.Should().BeTrue();

        vm.JiraUrl = original;
        vm.Alterado.Should().BeFalse();
    }

    [Fact]
    public void Chips_adicionados_ou_removidos_contam_como_alteracao()
    {
        var vm = _c.NovaConfig();

        vm.Chaves.Add("PDA");
        vm.Alterado.Should().BeTrue();

        vm.Chaves.Remove("PDA");
        vm.Alterado.Should().BeFalse();

        vm.StatusFinais.Add("Concluído");
        vm.Alterado.Should().BeTrue();
    }

    [Fact]
    public void Cancelar_descarta_o_que_foi_digitado_e_volta_ao_gravado()
    {
        var vm = _c.NovaConfig();
        vm.Intervalo = "10";
        vm.Chaves.Add("PDA");
        vm.EscopoQualquer = true;

        vm.CancelarCommand.Execute(null);

        vm.Intervalo.Should().Be("3");
        vm.Chaves.Should().Equal("VENDAS");
        vm.EscopoQualquer.Should().BeFalse();
        vm.Alterado.Should().BeFalse();
        _c.Servico.Config.IntervaloMin.Should().Be(3);
    }

    [Fact]
    public void Salvar_grava_na_configuracao_do_servico_avisa_e_zera_a_alteracao()
    {
        var vm = _c.NovaConfig();
        var salvou = 0;
        vm.Salvo += () => salvou++;
        vm.Chaves.Add("PDA");
        vm.StatusFinais.Add("Concluído");
        vm.TiposCoding.Add("Código");
        vm.Intervalo = "7";
        vm.StatusAlvo = " Ready for Beta ";
        vm.EscopoQualquer = true;
        vm.PastaTrabalho = @"C:\clones";

        vm.SalvarCommand.Execute(null);

        var c = _c.Servico.Config;
        c.ChavesJira.Should().Be("VENDAS,PDA");
        c.StatusFinal.Should().Be("Waiting for Product Increment,Concluído");
        c.StatusFinais.Should().Equal("Waiting for Product Increment", "Concluído");
        c.TiposCoding.Should().Be("Coding,Code,Codigo,Código");
        c.IntervaloMin.Should().Be(7);
        c.StatusAlvo.Should().Be("Ready for Beta");
        c.TesterEscopo.Should().Be("todas");
        c.PastaTrabalho.Should().Be(@"C:\clones");
        vm.Alterado.Should().BeFalse();
        vm.Mensagem.Should().Be("Configuração salva.");
        salvou.Should().Be(1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-2")]
    public void Intervalo_invalido_nao_salva_e_explica(string intervalo)
    {
        var vm = _c.NovaConfig();
        vm.Intervalo = intervalo;

        vm.SalvarCommand.Execute(null);

        vm.Aviso.Should().Contain("intervalo");
        vm.Alterado.Should().BeTrue();
        _c.Servico.Config.IntervaloMin.Should().Be(3);
    }

    [Fact]
    public void Jira_status_e_pasta_sao_obrigatorios()
    {
        var vm = _c.NovaConfig();
        vm.StatusAlvo = "  ";

        vm.SalvarCommand.Execute(null);

        vm.Aviso.Should().Contain("obrigatórios");
        _c.Servico.Config.StatusAlvo.Should().Be("Waiting for Testing");
    }

    [Fact]
    public void Escopo_atribuidas_e_qualquer_sao_exclusivos()
    {
        var vm = _c.NovaConfig();
        vm.EscopoAtribuidas.Should().BeTrue();

        vm.EscopoQualquer = true;
        vm.EscopoAtribuidas.Should().BeFalse();

        vm.EscopoAtribuidas = true;
        vm.EscopoQualquer.Should().BeFalse();
        vm.Alterado.Should().BeFalse();
    }

    [Fact]
    public void Painel_do_perfil_muda_com_o_papel()
    {
        _c.NovaConfig().ModoDesenvolvedor.Should().BeTrue();
        _c.NovaConfig().ModoTester.Should().BeFalse();

        using var tester = new CenarioDasAbas(PerfilDeUso.Tester);
        tester.NovaConfig().ModoTester.Should().BeTrue();
        tester.NovaConfig().ModoDesenvolvedor.Should().BeFalse();
    }

    [Fact]
    public async Task Ordem_dos_projetos_abre_o_dialogo_e_grava_a_ordem_devolvida()
    {
        var vm = _c.NovaConfig();
        _c.Janelas.RespostaDoDialogo = new List<string> { "lavendereweb", "lavenderecore" };

        await vm.OrdemDosProjetosCommand.ExecuteAsync(null);

        _c.Janelas.Abertas.Should().ContainSingle();
        _c.Servico.Config.OrdemProjetos.Should().Equal("lavendereweb", "lavenderecore");
    }
}
