using System.Windows.Media;
using Menage.App.Tema;

namespace Menage.App.Tests;

/// <summary>Contraste (WCAG) dos pares texto/fundo mais usados, nos 12 temas (claros e escuros).</summary>
public class ContrasteDosTemasTests
{
    readonly CatalogoDeTemas _catalogo = new();

    public static TheoryData<string> Ids()
    {
        var dados = new TheoryData<string>();
        foreach (var tema in new CatalogoDeTemas().Todos) dados.Add(tema.Id);
        return dados;
    }

    [Fact]
    public void Razao_conhecida_preto_no_branco_e_21()
    {
        Contraste.Razao(Colors.Black, Colors.White).Should().BeApproximately(21, 0.01);
        Contraste.Razao(Colors.Gray, Colors.Gray).Should().BeApproximately(1, 0.001);
    }

    public static IEnumerable<object[]> TextosNormais() =>
        from id in new CatalogoDeTemas().Todos.Select(t => t.Id)
        from par in new[]
        {
            (PapelDeCor.Texto, PapelDeCor.Fundo), (PapelDeCor.Texto, PapelDeCor.Painel), (PapelDeCor.TextoForte, PapelDeCor.Painel),
            (PapelDeCor.TextoFraco, PapelDeCor.Painel), (PapelDeCor.Texto, PapelDeCor.Lateral), (PapelDeCor.Texto, PapelDeCor.Cartao2),
            (PapelDeCor.Texto, PapelDeCor.GradeSel), (PapelDeCor.ConsoleTexto, PapelDeCor.Console),
        }
        select new object[] { id, par.Item1, par.Item2 };

    [Theory]
    [MemberData(nameof(TextosNormais))]
    public void Texto_normal_tem_pelo_menos_4_5(string id, PapelDeCor texto, PapelDeCor fundo)
    {
        var tema = _catalogo.Buscar(id)!;
        Contraste.Razao(tema[texto], tema[fundo]).Should().BeGreaterThanOrEqualTo(Contraste.TextoNormal, $"{id}: {texto} sobre {fundo}");
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Destaque_e_cores_de_estado_se_leem_sobre_o_painel(string id)
    {
        var tema = _catalogo.Buscar(id)!;
        foreach (var papel in new[] { PapelDeCor.Destaque, PapelDeCor.Verde, PapelDeCor.Amarelo, PapelDeCor.Laranja, PapelDeCor.Vermelho, PapelDeCor.Azul })
            Contraste.Razao(tema[papel], tema[PapelDeCor.Painel]).Should().BeGreaterThanOrEqualTo(Contraste.TextoGrandeOuControle, $"{id}: {papel} sobre o painel");
    }
}
