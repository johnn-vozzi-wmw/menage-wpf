using Menage.App.Controles;
using Menage.Core.Comandos;
using Menage.Core.Perfis;

namespace Menage.App.Tests;

public class BuscaDeComandosTests
{
    static Comando Novo(string id, string texto, string grupo = "Git", IReadOnlyCollection<PerfilDeUso>? perfis = null, Func<bool>? habilitado = null) =>
        new(id, texto, _ => Task.CompletedTask, Grupo: grupo, Perfis: perfis, Habilitado: habilitado);

    static readonly IComando[] _comandos =
    [
        Novo("config", "Configurações", "Geral"),
        Novo("renomear", "Renomear branch"),
        Novo("nova", "Branch nova"),
        Novo("diario", "Diário de trabalho", "Dev", ConjuntosDePerfis.Somente(PerfilDeUso.Desenvolvedor)),
        Novo("desfazer", "Desfazer commit", habilitado: () => false),
    ];

    static IReadOnlyList<ItemDaPaleta> Buscar(string? texto, PerfilDeUso perfil = PerfilDeUso.Geral) =>
        BuscaDeComandos.Filtrar(_comandos, texto, perfil);

    [Theory]
    [InlineData("configuracoes")]
    [InlineData("CONFIGURAÇÕES")]
    [InlineData("confíguracões")]
    public void Ignora_acento_e_caixa(string busca) =>
        Buscar(busca).Should().ContainSingle().Which.Comando.Id.Should().Be("config");

    [Fact]
    public void Todas_as_palavras_precisam_casar_em_qualquer_ordem()
    {
        Buscar("nova branch").Should().ContainSingle().Which.Comando.Id.Should().Be("nova");
        Buscar("branch inexistente").Should().BeEmpty();
    }

    [Fact]
    public void Busca_tambem_no_grupo() => Buscar("git nova").Should().ContainSingle();

    [Fact]
    public void Quem_comeca_com_a_busca_vem_primeiro() => Buscar("branch")[0].Comando.Id.Should().Be("nova");

    [Fact]
    public void Busca_vazia_lista_tudo_com_os_bloqueados_no_fim()
    {
        var itens = Buscar("  ");

        itens.Should().HaveCount(5);
        itens.TakeLast(2).Should().OnlyContain(i => i.Bloqueado);
    }

    [Fact]
    public void Bloqueado_por_perfil_diz_qual_perfil_libera()
    {
        var item = Buscar("diario")[0];

        item.Bloqueado.Should().BeTrue();
        item.Motivo.Should().Contain("Desenvolvedor");
    }

    [Fact]
    public void Perfil_certo_libera() => Buscar("diario", PerfilDeUso.Desenvolvedor)[0].Bloqueado.Should().BeFalse();

    [Fact]
    public void Comando_que_nao_pode_executar_agora_tambem_fica_bloqueado() =>
        Buscar("desfazer")[0].Motivo.Should().Be("Indisponível no momento");
}
