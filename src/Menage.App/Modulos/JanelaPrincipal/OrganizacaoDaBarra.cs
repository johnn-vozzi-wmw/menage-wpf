namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>
/// Ordem e visibilidade das pílulas da barra (personalizar a barra, como no Git Manager). Só lógica: a tela lê e grava
/// pelas preferências. A pílula "Mais" nunca sai da barra, porque é por ela que se chega ao que foi escondido.
/// </summary>
public sealed record OrganizacaoDaBarra(IReadOnlyList<string> Ordem, IReadOnlyList<string> Ocultas)
{
    public const string Obrigatoria = "Mais";

    public static OrganizacaoDaBarra Padrao(IEnumerable<string> nomes) => new([.. nomes], []);

    /// <summary>Completa com as pílulas que a preferência salva não conhece e descarta as que não existem mais.</summary>
    public OrganizacaoDaBarra Normalizar(IReadOnlyList<string> todas)
    {
        var ordem = Ordem.Where(todas.Contains).Distinct().ToList();
        ordem.AddRange(todas.Where(n => !ordem.Contains(n)));
        return new(ordem, [.. Ocultas.Where(n => todas.Contains(n) && n != Obrigatoria).Distinct()]);
    }

    public IReadOnlyList<string> Visiveis => [.. Ordem.Where(n => !Ocultas.Contains(n))];

    public OrganizacaoDaBarra Tirar(string nome) =>
        nome == Obrigatoria || Ocultas.Contains(nome) ? this : this with { Ocultas = [.. Ocultas, nome] };

    public OrganizacaoDaBarra Mostrar(string nome) => this with { Ocultas = [.. Ocultas.Where(n => n != nome)] };

    /// <summary>Anda uma posição entre as pílulas visíveis (as ocultas não contam).</summary>
    public OrganizacaoDaBarra Mover(string nome, int passo)
    {
        var visiveis = Visiveis.ToList();
        var de = visiveis.IndexOf(nome);
        var para = de + Math.Sign(passo);
        if (de < 0 || para < 0 || para >= visiveis.Count) return this;

        var ordem = Ordem.ToList();
        var a = ordem.IndexOf(nome);
        var b = ordem.IndexOf(visiveis[para]);
        (ordem[a], ordem[b]) = (ordem[b], ordem[a]);
        return this with { Ordem = ordem };
    }
}
