using System.Windows.Media;
using Menage.App.Tema;

namespace Menage.App.Tests;

public class CatalogoDeTemasTests
{
    readonly CatalogoDeTemas _catalogo = new();

    public static TheoryData<string> Ids()
    {
        var dados = new TheoryData<string>();
        foreach (var tema in new CatalogoDeTemas().Todos) dados.Add(tema.Id);
        return dados;
    }

    [Fact]
    public void Tem_12_temas_em_6_pares()
    {
        _catalogo.Todos.Should().HaveCount(12);
        _catalogo.Todos.Count(t => t.Escuro).Should().Be(6);
        foreach (var tema in _catalogo.Todos)
        {
            var par = _catalogo.ParDe(tema);
            par.Escuro.Should().Be(!tema.Escuro);
            par.Par.Should().Be(tema.Id);
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Cada_tema_tem_todas_as_cores_sem_repetir(string id)
    {
        var tema = _catalogo.Buscar(id)!;
        tema.Cores.Should().HaveCount(Enum.GetValues<PapelDeCor>().Length);
        tema.Cores.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Saida_e_preta_em_qualquer_tema(string id) =>
        _catalogo.Buscar(id)![PapelDeCor.Console].Should().Be(Colors.Black);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Tema_claro_nao_usa_branco_puro(string id)
    {
        var tema = _catalogo.Buscar(id)!;
        if (!tema.Escuro) tema.Cores.Should().NotContain(Colors.White);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Texto_tem_contraste_com_o_fundo(string id)
    {
        var tema = _catalogo.Buscar(id)!;
        Contraste(tema[PapelDeCor.Texto], tema[PapelDeCor.Fundo]).Should().BeGreaterThan(7);
        Contraste(tema[PapelDeCor.Texto], tema[PapelDeCor.Painel]).Should().BeGreaterThan(7);
        Contraste(tema[PapelDeCor.TextoFraco], tema[PapelDeCor.Painel]).Should().BeGreaterThan(3);
    }

    [Fact]
    public void Tema_inexistente_cai_no_escuro() =>
        _catalogo.BuscarOuPadrao("privado-sem-acesso").Id.Should().Be(CatalogoDeTemas.IdEscuro);

    [Fact]
    public void Privado_valido_entra_no_catalogo_derivado_da_base()
    {
        var rejeitados = _catalogo.DefinirPrivados([Privado("empresa-escuro", "empresa-claro"), Privado("empresa-claro", "empresa-escuro", escuro: false)]);

        rejeitados.Should().BeEmpty();
        var tema = _catalogo.Buscar("empresa-escuro")!;
        tema.Privado.Should().BeTrue();
        tema[PapelDeCor.Console].Should().Be(Colors.Black);
        tema[PapelDeCor.Destaque].Should().Be(Color.FromRgb(0x11, 0x22, 0x33));
        _catalogo.ParDe(tema).Id.Should().Be("empresa-claro");
    }

    [Fact]
    public void Privado_invalido_ou_com_id_do_programa_fica_de_fora()
    {
        var rejeitados = _catalogo.DefinirPrivados([Privado("escuro", "claro"), Privado("ruim", "claro") with { Destaque = "azul" }]);

        rejeitados.Should().BeEquivalentTo("escuro", "ruim");
        _catalogo.Todos.Should().HaveCount(12);
    }

    [Fact]
    public void Privado_sem_par_valido_alterna_para_o_tema_base()
    {
        _catalogo.DefinirPrivados([Privado("solto", "nao-existe")]);
        _catalogo.ParDe(_catalogo.Buscar("solto")!).Id.Should().Be(CatalogoDeTemas.IdClaro);
    }

    static TemaPrivado Privado(string id, string par, bool escuro = true) =>
        new(id, "Privado " + id, par, escuro, "#101820", "#112233", "#223344", "#334455");

    static double Contraste(Color a, Color b)
    {
        var (claro, escuro) = (Math.Max(Luz(a), Luz(b)), Math.Min(Luz(a), Luz(b)));
        return (claro + 0.05) / (escuro + 0.05);
    }

    static double Luz(Color c) => (0.2126 * Canal(c.R)) + (0.7152 * Canal(c.G)) + (0.0722 * Canal(c.B));

    static double Canal(byte v)
    {
        var s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
