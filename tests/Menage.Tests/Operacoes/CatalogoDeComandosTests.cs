namespace Menage.Tests.Operacoes;

public class CatalogoDeComandosTests
{
    sealed class ModuloA : IModulo
    {
        public string Nome => "A";

        public void Registrar(ICatalogoDeComandos catalogo)
        {
            catalogo.Adicionar(new Comando("a.um", "Buscar commit", _ => Task.CompletedTask, "Git", Atalho: "Ctrl+B"));
            catalogo.Adicionar(new Comando("a.dois", "Gerar beta", _ => Task.CompletedTask, "Beta",
                Perfis: ConjuntosDePerfis.Somente(PerfilDeUso.Desenvolvedor)));
        }
    }

    sealed class ModuloB : IModulo
    {
        public string Nome => "B";

        public void Registrar(ICatalogoDeComandos catalogo) =>
            catalogo.Adicionar(new Comando("b.um", "Push", _ => Task.CompletedTask, "Git", Habilitado: () => false));
    }

    [Fact]
    public void Montar_pede_a_cada_modulo_que_se_registre_na_ordem()
    {
        var catalogo = CatalogoDeComandos.Montar([new ModuloA(), new ModuloB()]);

        catalogo.Todos.Select(c => c.Id).Should().Equal("a.um", "a.dois", "b.um");
        catalogo.Obter("A.UM")!.Atalho.Should().Be("Ctrl+B");
        catalogo.Obter("nao.existe").Should().BeNull();
    }

    [Fact]
    public void Id_duplicado_e_erro_de_programacao()
    {
        var catalogo = CatalogoDeComandos.Montar([new ModuloA()]);

        catalogo.Invoking(c => new ModuloA().Registrar(c)).Should().Throw<InvalidOperationException>().WithMessage("*a.um*");
    }

    [Fact]
    public void Disponiveis_respeita_o_perfil_que_libera()
    {
        var catalogo = CatalogoDeComandos.Montar([new ModuloA(), new ModuloB()]);

        catalogo.Disponiveis(PerfilDeUso.Desenvolvedor).Should().HaveCount(3);
        catalogo.Disponiveis(PerfilDeUso.Tester).Select(c => c.Id).Should().Equal("a.um", "b.um");
    }

    [Fact]
    public void Busca_da_paleta_casa_texto_ou_grupo_com_todas_as_palavras_e_respeita_perfil()
    {
        var catalogo = CatalogoDeComandos.Montar([new ModuloA(), new ModuloB()]);

        catalogo.Buscar("git", PerfilDeUso.Geral).Select(c => c.Id).Should().Equal("a.um", "b.um");
        catalogo.Buscar("buscar GIT", PerfilDeUso.Geral).Select(c => c.Id).Should().Equal("a.um");
        catalogo.Buscar("beta", PerfilDeUso.Tester).Should().BeEmpty();
        catalogo.Buscar("  ", PerfilDeUso.Geral).Should().HaveCount(2, "sem texto lista tudo que o perfil libera");
    }

    [Fact]
    public async Task PodeExecutar_e_ExecutarAsync_usam_os_delegates()
    {
        var executou = false;
        var comando = new Comando("x", "X", _ => { executou = true; return Task.CompletedTask; });

        comando.PodeExecutar().Should().BeTrue();
        await comando.ExecutarAsync();

        executou.Should().BeTrue();
        new Comando("y", "Y", _ => Task.CompletedTask, Habilitado: () => false).PodeExecutar().Should().BeFalse();
        comando.PerfisQueLiberam.Should().BeEquivalentTo(ConjuntosDePerfis.Todos);
    }

    [Fact]
    public void Modulos_registrados_por_DI_alimentam_o_catalogo()
    {
        var servicos = new ServiceCollection()
            .AddMenageCore(new PastaDeDados(Path.GetTempPath()))
            .AddModulo<ModuloA>();
        using var sp = servicos.BuildServiceProvider();

        sp.GetRequiredService<ICatalogoDeComandos>().Todos.Should().HaveCount(2);
    }
}
