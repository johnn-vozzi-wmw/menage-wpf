using System.Windows.Media;
using Menage.App.Tema;
using Menage.Core.Personalizacao;
using static Menage.App.Tema.PapelDeCor;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>
/// Troca a cor de destaque de qualquer tema pela escolhida pelo usuário. Refaz só o que nasce do destaque
/// (hover, seleção da lateral e da grade, hover da grade); o resto da paleta fica como o tema definiu.
/// </summary>
public sealed class AjusteDeDestaque : IAjusteDeTema
{
    public Color? Cor { get; set; }

    public DefinicaoDeTema Ajustar(DefinicaoDeTema tema)
    {
        if (Cor is not { } cor) return tema;
        var (r, g, b) = MedidasDeAparencia.Hover((cor.R, cor.G, cor.B));
        var p = tema.Cores.ToArray();
        p[(int)Destaque] = cor;
        p[(int)DestaqueHover] = Color.FromRgb(r, g, b);
        p[(int)SelLateral] = DerivacaoDePaleta.Mistura(p[(int)Painel], cor, tema.Escuro ? 0.32 : 0.2);
        p[(int)GradeSel] = DerivacaoDePaleta.Mistura(p[(int)Painel], cor, tema.Escuro ? 0.4 : 0.24);
        p[(int)GradeHover] = DerivacaoDePaleta.Mistura(p[(int)Painel], cor, tema.Escuro ? 0.08 : 0.06);
        return tema with { Cores = DerivacaoDePaleta.Finalizar(p, tema.Escuro) };
    }

    public static Color? Ler(string? hex)
    {
        if (MedidasDeAparencia.NormalizarCor(hex) is not { } n) return null;
        var rgb = Convert.ToInt32(n[1..], 16);
        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
