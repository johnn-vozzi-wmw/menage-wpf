using System.IO;
using Menage.App.Modulos.Perfis;
using Menage.Core.Armazenamento;
using Menage.Core.Perfis;
using Menage.TestKit;

namespace Menage.App.Tests;

public sealed class PerfisViewModelTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "pv" + Guid.NewGuid().ToString("N")[..6]);
    readonly DialogosFalsos _dialogos = new();
    readonly ServicoDePerfis _perfis;

    public PerfisViewModelTests() => _perfis = new ServicoDePerfis(new PastaDeDados(_pasta));

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    PerfisViewModel Criar() => new(_perfis, _dialogos);

    [Fact]
    public void Abre_no_perfil_ativo_com_as_caixas_marcadas_pelo_que_ele_permite()
    {
        _perfis.Ativar("tester");

        var vm = Criar();

        vm.Selecionado!.Id.Should().Be("tester");
        vm.Opcoes.First(o => o.Id == "fetch").Marcada.Should().BeTrue();
        vm.Opcoes.First(o => o.Id == "push").Marcada.Should().BeFalse();
        vm.Alterado.Should().BeFalse();
    }

    [Fact]
    public void Perfil_novo_com_tudo_marcado_grava_asterisco_e_funcionalidade_de_dev_fica_bloqueada_na_base_Geral()
    {
        var vm = Criar();
        vm.NovoCommand.Execute(null);

        vm.Nome = "Meu perfil";
        vm.Opcoes.First(o => o.Id == "melhorias").Habilitada.Should().BeFalse();
        vm.SalvarCommand.Execute(null);

        _perfis.Perfis.Single(p => p.Nome == "Meu perfil").Permitidas.Should().Equal("*");
        vm.Selecionado!.Nome.Should().Be("Meu perfil");
        vm.Alterado.Should().BeFalse();
    }

    [Fact]
    public void Desmarcar_uma_funcionalidade_grava_a_lista_explicita_e_marca_como_alterado()
    {
        var vm = Criar();
        vm.NovoCommand.Execute(null);
        vm.Nome = "Sem push";

        vm.Opcoes.First(o => o.Id == "push").Marcada = false;
        vm.SalvarCommand.Execute(null);

        var salvo = _perfis.Perfis.Single(p => p.Nome == "Sem push");
        salvo.Permitidas.Should().NotContain("push").And.Contain("fetch");
        salvo.Permite("push").Should().BeFalse();
    }

    [Fact]
    public void Mudar_a_base_para_Desenvolvedor_libera_Melhorias_e_para_Geral_desmarca()
    {
        var vm = Criar();
        vm.NovoCommand.Execute(null);

        vm.Base = PerfilDeUso.Desenvolvedor;
        vm.Opcoes.First(o => o.Id == "melhorias").Habilitada.Should().BeTrue();
        vm.Opcoes.First(o => o.Id == "melhorias").Marcada = true;
        vm.Base = PerfilDeUso.Geral;

        vm.Opcoes.First(o => o.Id == "melhorias").Marcada.Should().BeFalse();
    }

    [Fact]
    public async Task Excluir_pede_confirmacao_e_o_perfil_ativo_excluido_volta_para_Geral()
    {
        var meu = _perfis.Duplicar("dev").Valor;
        _perfis.Ativar(meu.Id);
        var vm = Criar();

        _dialogos.RespostaDaConfirmacao = false;
        await vm.ExcluirCommand.ExecuteAsync(null);
        _perfis.Obter(meu.Id).Should().NotBeNull();

        _dialogos.RespostaDaConfirmacao = true;
        await vm.ExcluirCommand.ExecuteAsync(null);

        _perfis.Obter(meu.Id).Should().BeNull();
        _perfis.Ativo.Id.Should().Be("completo");
        _dialogos.Previas.Last().Detalhes.Should().Contain(d => d.Contains("perfil Geral"));
    }

    [Fact]
    public async Task Trocar_de_perfil_com_edicao_pendente_pergunta_e_pode_ficar()
    {
        var vm = Criar();
        vm.NovoCommand.Execute(null);
        vm.Nome = "Rascunho";
        _dialogos.RespostaDaConfirmacao = false;

        var trocou = await vm.TrocarAsync(vm.Itens.First(i => i.Id == "tester"));

        trocou.Should().BeFalse();
        vm.Nome.Should().Be("Rascunho");
    }
}
