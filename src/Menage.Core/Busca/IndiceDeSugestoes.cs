using System.Globalization;
using System.Text;

namespace Menage.Core.Busca;

/// <summary>Um trecho do texto a destacar na lista (início e tamanho, no texto original).</summary>
public readonly record struct TrechoDestacado(int Inicio, int Tamanho);

/// <summary>Uma sugestão com seus trechos destacados.</summary>
public sealed record Sugestao(string Texto, IReadOnlyList<TrechoDestacado> Destaques);

/// <summary>
/// Índice para autocomplete instantâneo: o texto de cada item é normalizado (sem acento, sem caixa, mesmo tamanho
/// do original) uma única vez; cada letra digitada só faz <c>IndexOf</c> ordinal (vetorizado) sobre o índice.
/// Todas as palavras digitadas precisam casar. Ordem: começa com o texto, começa uma palavra (depois de / - _ . espaço),
/// aparece no meio; empate: mais curto primeiro e, depois, a ordem original.
/// </summary>
public sealed class IndiceDeSugestoes
{
    readonly string[] _originais;
    readonly string[] _normalizados;

    public IndiceDeSugestoes(IEnumerable<string> itens)
    {
        _originais = [.. itens.Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal)];
        _normalizados = [.. _originais.Select(Normalizar)];
    }

    public int Total => _originais.Length;

    public IReadOnlyList<Sugestao> Buscar(string? texto, int maximo = 50)
    {
        var palavras = Normalizar(texto ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (palavras.Length == 0)
            return [.. _originais.Take(maximo).Select(o => new Sugestao(o, []))];

        var candidatos = new List<(int Indice, int Nota)>();
        for (var i = 0; i < _normalizados.Length; i++)
        {
            var nota = Nota(_normalizados[i], palavras);
            if (nota >= 0) candidatos.Add((i, nota));
        }

        return
        [
            .. candidatos
                .OrderBy(c => c.Nota).ThenBy(c => _originais[c.Indice].Length).ThenBy(c => c.Indice)
                .Take(maximo)
                .Select(c => new Sugestao(_originais[c.Indice], Destaques(_normalizados[c.Indice], palavras))),
        ];
    }

    // 0 = começa com a primeira palavra; 1 = começa uma palavra; 2 = no meio; -1 = não casa.
    static int Nota(string alvo, string[] palavras)
    {
        var pior = 0;
        for (var p = 0; p < palavras.Length; p++)
        {
            var pos = alvo.IndexOf(palavras[p], StringComparison.Ordinal);
            if (pos < 0) return -1;
            var nota = pos == 0 ? 0 : InicioDePalavra(alvo, palavras[p]) ? 1 : 2;
            pior = Math.Max(pior, nota);
        }

        return pior;
    }

    static bool InicioDePalavra(string alvo, string palavra)
    {
        for (var pos = alvo.IndexOf(palavra, StringComparison.Ordinal); pos >= 0;
             pos = alvo.IndexOf(palavra, pos + 1, StringComparison.Ordinal))
        {
            if (pos == 0 || alvo[pos - 1] is '/' or '-' or '_' or '.' or ' ') return true;
        }

        return false;
    }

    static List<TrechoDestacado> Destaques(string alvo, string[] palavras)
    {
        var faixas = new List<TrechoDestacado>();
        foreach (var palavra in palavras)
        {
            var pos = alvo.IndexOf(palavra, StringComparison.Ordinal);
            if (pos >= 0) faixas.Add(new TrechoDestacado(pos, palavra.Length));
        }

        faixas.Sort((a, b) => a.Inicio.CompareTo(b.Inicio));
        var unidas = new List<TrechoDestacado>();
        foreach (var f in faixas)
        {
            if (unidas.Count > 0 && f.Inicio <= unidas[^1].Inicio + unidas[^1].Tamanho)
            {
                var u = unidas[^1];
                unidas[^1] = u with { Tamanho = Math.Max(u.Tamanho, f.Inicio + f.Tamanho - u.Inicio) };
            }
            else unidas.Add(f);
        }

        return unidas;
    }

    /// <summary>Sem acento e sem caixa, preservando o tamanho (cada letra vira a sua letra-base).</summary>
    public static string Normalizar(string texto)
    {
        var saida = new StringBuilder(texto.Length);
        foreach (var letra in texto)
        {
            if (letra < 128) { saida.Append(char.ToLowerInvariant(letra)); continue; }
            var baseLetra = letra.ToString().Normalize(NormalizationForm.FormD)[0];
            saida.Append(char.ToLowerInvariant(CharUnicodeInfo.GetUnicodeCategory(baseLetra) == UnicodeCategory.NonSpacingMark ? letra : baseLetra));
        }

        return saida.ToString();
    }
}
