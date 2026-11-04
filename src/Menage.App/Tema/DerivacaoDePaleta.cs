using System.Windows.Media;
using static Menage.App.Tema.PapelDeCor;

namespace Menage.App.Tema;

/// <summary>Monta paletas: tinge uma base com um matiz, garante cores únicas e o console preto.</summary>
public static class DerivacaoDePaleta
{
    static readonly PapelDeCor[] _neutros =
        [Fundo, Painel, PainelClaro, Lateral, Borda, Cartao2, BotaoSutil, BotaoHover, HoverLateral, Contador, GradeLinha, Desab, InterrOff];

    public static Color Mistura(Color a, Color b, double k) => Color.FromRgb(
        (byte)Math.Round(a.R + ((b.R - a.R) * k)),
        (byte)Math.Round(a.G + ((b.G - a.G) * k)),
        (byte)Math.Round(a.B + ((b.B - a.B) * k)));

    /// <summary>Tema derivado: tinge os neutros com o matiz e troca o destaque (seleções e hovers saem dele).</summary>
    public static Color[] Derivar(IReadOnlyList<Color> baseDaPaleta, bool escuro, Color matiz, Color destaque, Color destaqueHover, Color azul)
    {
        var p = baseDaPaleta.ToArray();
        void Tingir(PapelDeCor papel, Color cor, double k) => p[(int)papel] = Mistura(p[(int)papel], cor, k);

        foreach (var papel in _neutros) Tingir(papel, matiz, escuro ? 0.22 : 0.45);

        var tomDoTexto = escuro ? Mistura(matiz, Colors.White, 0.85) : Mistura(matiz, Colors.Black, 0.8);
        foreach (var papel in new[] { Texto, TextoFraco, TextoDesab }) Tingir(papel, tomDoTexto, 0.25);

        var tomDoConsole = escuro ? matiz : Mistura(destaque, Colors.Black, 0.75);
        foreach (var papel in new[] { ConsoleBotao, ConsoleBotaoHover }) Tingir(papel, tomDoConsole, 0.25);

        p[(int)Destaque] = destaque;
        p[(int)DestaqueHover] = destaqueHover;
        p[(int)Azul] = azul;
        p[(int)GrupoWeb] = azul;
        p[(int)SelLateral] = Mistura(p[(int)Painel], destaque, escuro ? 0.32 : 0.2);
        p[(int)GradeSel] = Mistura(p[(int)Painel], destaque, escuro ? 0.4 : 0.24);
        p[(int)GradeHover] = Mistura(p[(int)Painel], destaque, escuro ? 0.08 : 0.06);
        return p;
    }

    /// <summary>
    /// Paleta final: a área de Saída é sempre preta, as cores não se repetem e o tema claro nunca usa branco puro
    /// (branco fica reservado ao texto sobre botões coloridos). Repetidas ganham um ajuste de 1 no azul.
    /// </summary>
    public static Color[] Finalizar(IReadOnlyList<Color> paleta, bool escuro)
    {
        var p = paleta.ToArray();
        p[(int)PapelDeCor.Console] = Colors.Black;
        var vistas = new HashSet<Color>();
        if (!escuro) vistas.Add(Colors.White);
        for (var i = 0; i < p.Length; i++)
        {
            var cor = Color.FromRgb(p[i].R, p[i].G, p[i].B);
            while (vistas.Contains(cor)) cor = Color.FromRgb(cor.R, cor.G, (byte)(cor.B > 0 ? cor.B - 1 : cor.B + 1));
            vistas.Add(cor);
            p[i] = cor;
        }

        return p;
    }
}
