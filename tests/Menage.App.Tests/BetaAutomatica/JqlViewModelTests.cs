using Menage.App.Controles;
using Menage.App.Modulos.BetaAutomatica;
using Menage.Core.BetaAutomatica;

namespace Menage.App.Tests.BetaAutomatica;

public sealed class JqlViewModelTests : IDisposable
{
    readonly CenarioDasAbas _c = new();

    public void Dispose() => _c.Dispose();

    [Fact]
    public void Lista_as_consultas_padrao_do_perfil_com_a_primeira_selecionada()
    {
        var vm = _c.NovoJql();

        vm.Linhas.Select(l => l.Tipo).Should().OnlyContain(t => t == "padrão");
        vm.Linhas.Select(l => l.Jql.Id).Should().Equal(GerenciadorDeJql.Padrao(false).Select(p => p.Id));
        vm.Selecionada.Should().BeSameAs(vm.Linhas[0]);
        vm.Texto.Should().Be(vm.Linhas[0].Jql.Jql);
        vm.NomeSomenteLeitura.Should().BeTrue();
        vm.PodeExcluir.Should().BeFalse();
        vm.Alterado.Should().BeFalse();
    }

    [Fact]
    public void Nova_cria_uma_personalizada_selecionada_com_nome_editavel_e_marca_alteracao()
    {
        var vm = _c.NovoJql();
        var antes = vm.Linhas.Count;

        vm.NovaCommand.Execute(null);

        vm.Linhas.Should().HaveCount(antes + 1);
        vm.Selecionada!.Tipo.Should().Be("sua");
        vm.Selecionada.PadraoDoPrograma.Should().BeFalse();
        vm.NomeSomenteLeitura.Should().BeFalse();
        vm.PodeExcluir.Should().BeTrue();
        vm.Alterado.Should().BeTrue();
    }

    [Fact]
    public void Duplicar_copia_o_texto_depois_da_atual_como_personalizada()
    {
        var vm = _c.NovoJql();

        vm.DuplicarCommand.Execute(null);

        vm.Linhas.Should().HaveCount(3);
        vm.Selecionada.Should().BeSameAs(vm.Linhas[1]);
        vm.Selecionada!.Nome.Should().EndWith("(cópia)");
        vm.Texto.Should().Be(vm.Linhas[0].Jql.Jql);
        vm.Selecionada.Tipo.Should().Be("sua");
    }

    [Fact]
    public void Excluir_so_vale_para_personalizada()
    {
        var vm = _c.NovoJql();
        vm.ExcluirCommand.CanExecute(null).Should().BeFalse();

        vm.NovaCommand.Execute(null);
        vm.ExcluirCommand.CanExecute(null).Should().BeTrue();
        vm.ExcluirCommand.Execute(null);

        vm.Linhas.Should().HaveCount(2);
        vm.Linhas.Should().OnlyContain(l => l.PadraoDoPrograma);
    }

    [Fact]
    public void Subir_e_Descer_reordenam_e_respeitam_as_pontas()
    {
        var vm = _c.NovoJql();
        var ids = vm.Linhas.Select(l => l.Jql.Id).ToList();
        vm.SubirCommand.CanExecute(null).Should().BeFalse();
        vm.DescerCommand.CanExecute(null).Should().BeTrue();

        vm.DescerCommand.Execute(null);

        vm.Linhas.Select(l => l.Jql.Id).Should().Equal(ids[1], ids[0]);
        vm.Selecionada!.Jql.Id.Should().Be(ids[0]);
        vm.DescerCommand.CanExecute(null).Should().BeFalse();
        vm.SubirCommand.CanExecute(null).Should().BeTrue();
        vm.Alterado.Should().BeTrue();
    }

    [Fact]
    public void Editar_o_texto_de_uma_padrao_vira_editada_e_Restaurar_volta_ao_padrao()
    {
        var vm = _c.NovoJql();
        var padrao = vm.Texto;

        vm.Texto = padrao + " ORDER BY created DESC";

        vm.Selecionada!.Tipo.Should().Be("padrão (editada)");
        vm.PodeRestaurar.Should().BeTrue();
        vm.Alterado.Should().BeTrue();

        vm.RestaurarCommand.Execute(null);

        vm.Texto.Should().Be(padrao);
        vm.Selecionada!.Tipo.Should().Be("padrão");
        vm.PodeRestaurar.Should().BeFalse();
    }

    [Fact]
    public void Restaurar_todas_volta_as_consultas_padrao_e_descarta_as_suas()
    {
        var vm = _c.NovoJql();
        vm.NovaCommand.Execute(null);

        vm.RestaurarTodasCommand.Execute(null);

        vm.Linhas.Select(l => l.Jql.Id).Should().Equal(GerenciadorDeJql.Padrao(false).Select(p => p.Id));
    }

    [Fact]
    public void Marcadores_do_texto_sao_destacados_e_a_previa_troca_os_valores_da_configuracao()
    {
        var vm = _c.NovoJql();

        vm.Trechos.Where(t => t.Marcador).Select(t => t.Texto).Should().Equal("{projeto}", "{status}");
        vm.Expandida.Should().Contain("project in (VENDAS)").And.Contain("\"Waiting for Testing\"").And.NotContain("{");
    }

    [Fact]
    public void Jql_invalida_mostra_erro_em_vermelho_e_desabilita_o_teste()
    {
        var vm = _c.NovoJql();

        vm.Texto = "{projeto} AND status = \"{status}\" AND ((assignee = currentUser()";

        vm.TomDoAviso.Should().Be(TomDeCor.Vermelho);
        vm.Aviso.Should().Contain("Parênteses");
        vm.PodeTestar.Should().BeFalse();
        vm.TestarCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Testar_consulta_o_jira_de_mentira_e_lista_o_resultado()
    {
        _c.Jira.Issues.Add(new IssueJira { Chave = "VENDAS-9", Titulo = "Algo", Status = "Waiting for Testing", Responsavel = "eu" });
        var vm = _c.NovoJql();

        await vm.TestarCommand.ExecuteAsync(null);

        _c.Jira.Consultas.Should().ContainSingle().Which.Should().StartWith("project in (VENDAS)");
        vm.ResultadoDoTeste.Should().Contain("1 resultado(s)").And.Contain("VENDAS-9");
        vm.Testando.Should().BeFalse();
        vm.TextoDoTeste.Should().Be("Testar esta JQL");
    }

    [Fact]
    public async Task Testar_sem_token_mostra_o_erro()
    {
        _c.Jira.TemToken = false;
        var vm = _c.NovoJql();

        await vm.TestarCommand.ExecuteAsync(null);

        vm.ResultadoDoTeste.Should().StartWith("ERRO:").And.Contain("JIRA_TOKEN");
    }

    [Fact]
    public void Salvar_grava_so_a_diferenca_do_padrao_zera_a_alteracao_e_pede_nova_verificacao()
    {
        var vm = _c.NovoJql();
        vm.NovaCommand.Execute(null);
        vm.Nome = "Minha consulta";

        vm.SalvarCommand.Execute(null);

        _c.Servico.Config.Jqls.Should().ContainSingle(j => j.Nome == "Minha consulta");
        vm.Alterado.Should().BeFalse();
        vm.Mensagem.Should().Be("Consultas salvas.");
    }

    [Fact]
    public void Salvar_recusa_personalizada_sem_nome_ou_com_jql_invalida()
    {
        var vm = _c.NovoJql();
        vm.NovaCommand.Execute(null);
        vm.Nome = "   ";

        vm.SalvarCommand.Execute(null);
        vm.Erro.Should().Contain("nome");

        vm.Nome = "Com erro";
        vm.Texto = "status = \"{status}\" AND (";
        vm.SalvarCommand.Execute(null);

        vm.Erro.Should().Contain("Com erro");
        _c.Servico.Config.Jqls.Should().BeEmpty();
    }

    [Fact]
    public void Cancelar_descarta_as_alteracoes_e_volta_ao_gravado()
    {
        var vm = _c.NovoJql();
        vm.NovaCommand.Execute(null);

        vm.CancelarCommand.Execute(null);

        vm.Linhas.Should().HaveCount(2);
        vm.Alterado.Should().BeFalse();
    }

    [Fact]
    public void Inserir_trecho_coloca_o_texto_no_cursor_e_devolve_a_nova_posicao()
    {
        var vm = _c.NovoJql();
        vm.Texto = "abc def";

        var cursor = vm.Inserir(" AND ", 3, 0);

        vm.Texto.Should().Be("abc AND  def");
        cursor.Should().Be(8);
    }

    [Fact]
    public void Desligar_uma_consulta_conta_como_alteracao()
    {
        var vm = _c.NovoJql();

        vm.Linhas[0].Ativa = false;

        vm.Alterado.Should().BeTrue();
        vm.Aviso.Should().Contain("desligada");
    }
}
