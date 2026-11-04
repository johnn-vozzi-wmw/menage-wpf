using Menage.App.Controles;
using Menage.Core.Comandos;
using Menage.Core.Perfis;
using Menage.TestKit;

namespace Menage.App.Tests;

public class PaletaDeComandosViewModelTests
{
    readonly CatalogoDeComandos _catalogo = new();
    readonly DialogosFalsos _dialogos = new();
    readonly List<string> _executados = [];

    PaletaDeComandosViewModel Criar()
    {
        _catalogo.Adicionar(new Comando("a", "Alfa", _ => Registrar("a")));
        _catalogo.Adicionar(new Comando("b", "Beta", _ => Registrar("b")));
        _catalogo.Adicionar(new Comando("d", "Defeituoso", _ => throw new InvalidOperationException("quebrou")));
        _catalogo.Adicionar(new Comando("c", "Bloqueado", _ => Registrar("c"), Perfis: ConjuntosDePerfis.Somente(PerfilDeUso.Tester)));
        var modelo = new PaletaDeComandosViewModel(_catalogo, new PerfilFixo(), _dialogos);
        modelo.Reiniciar();
        return modelo;
    }

    Task Registrar(string id)
    {
        _executados.Add(id);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Enter_executa_o_selecionado_e_pede_para_fechar()
    {
        var modelo = Criar();
        var fechou = false;
        modelo.Concluido += () => fechou = true;
        modelo.Mover(1);

        await modelo.ExecutarCommand.ExecuteAsync(null);

        _executados.Should().Equal("b");
        fechou.Should().BeTrue();
    }

    [Fact]
    public async Task Item_bloqueado_nao_executa_nem_fecha()
    {
        var modelo = Criar();
        var fechou = false;
        modelo.Concluido += () => fechou = true;
        modelo.Busca = "bloqueado";

        await modelo.ExecutarCommand.ExecuteAsync(null);

        _executados.Should().BeEmpty();
        fechou.Should().BeFalse();
    }

    [Fact]
    public void Setas_dao_a_volta_na_lista()
    {
        var modelo = Criar();
        modelo.Mover(-1);
        modelo.Selecionado!.Comando.Id.Should().Be("c");
        modelo.Mover(1);
        modelo.Selecionado!.Comando.Id.Should().Be("a");
    }

    [Fact]
    public async Task Excecao_do_comando_vira_mensagem_de_erro()
    {
        var modelo = Criar();
        modelo.Busca = "defeituoso";

        await modelo.ExecutarCommand.ExecuteAsync(null);

        _dialogos.Erros.Should().ContainSingle().Which.Explicacao.Should().Be("quebrou");
    }

    [Fact]
    public void Reabrir_limpa_a_busca()
    {
        var modelo = Criar();
        modelo.Busca = "alfa";
        modelo.Reiniciar();
        modelo.Busca.Should().BeEmpty();
        modelo.Itens.Should().HaveCount(4);
    }
}
