namespace Menage.Core.Atualizacao;

/// <summary>Compara versões "1.2.3" e "1.2.3-beta.4" (sem a dependência do NuGet): pré-lançamento é menor que a versão final.</summary>
public sealed class VersaoSemantica : IComparer<string>
{
    public static VersaoSemantica Comparador { get; } = new();

    public int Compare(string? x, string? y) => (x, y) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        _ => Comparar(x, y),
    };

    static int Comparar(string a, string b)
    {
        var (na, pa) = Separar(a);
        var (nb, pb) = Separar(b);
        for (var i = 0; i < Math.Max(na.Length, nb.Length); i++)
        {
            var c = (i < na.Length ? na[i] : 0).CompareTo(i < nb.Length ? nb[i] : 0);
            if (c != 0) return c;
        }

        if (pa.Length == 0 || pb.Length == 0) return pa.Length == pb.Length ? 0 : pa.Length == 0 ? 1 : -1;
        return CompararPre(pa, pb);
    }

    static int CompararPre(string[] a, string[] b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var numeros = int.TryParse(a[i], out var x) & int.TryParse(b[i], out var y);
            var c = numeros ? x.CompareTo(y) : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }

        return a.Length.CompareTo(b.Length);
    }

    static (int[] Numeros, string[] Pre) Separar(string versao)
    {
        var limpa = versao.Trim().TrimStart('v', 'V').Split('+')[0];
        var corte = limpa.IndexOf('-');
        var nucleo = corte < 0 ? limpa : limpa[..corte];
        var pre = corte < 0 ? [] : limpa[(corte + 1)..].Split('.');
        var numeros = nucleo.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        return (numeros, pre);
    }
}
