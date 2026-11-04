using System.Text.RegularExpressions;

namespace Menage.Core.Diff;

/// <summary>O que fica no lugar do conflito.</summary>
public enum EscolhaConflito { Entrando = 1, Atual = 2, AtualDepoisEntrando = 3, EntrandoDepoisAtual = 4 }

/// <summary>Um trecho que o git não juntou sozinho, com o que cada lado tem nele.</summary>
public sealed record ConflitoTexto(int Numero, IReadOnlyList<string> Atual, IReadOnlyList<string> Base, IReadOnlyList<string> Entrando)
{
    public IReadOnlyList<string> Linhas(EscolhaConflito escolha) => escolha switch
    {
        EscolhaConflito.Entrando => Entrando,
        EscolhaConflito.Atual => Atual,
        EscolhaConflito.AtualDepoisEntrando => [.. Atual, .. Entrando],
        _ => [.. Entrando, .. Atual],
    };
}

/// <summary>
/// Resultado do <c>git merge-file</c>: o que juntou sozinho já vem pronto e cada conflito vira uma linha de aviso.
/// A linha de aviso é a fonte da verdade do que está pendente: desfazer, refazer e editar o resultado continuam
/// certos sem nenhum estado à parte.
/// </summary>
public sealed partial class JuncaoConflito
{
    /// <summary>Marcador maior que o padrão: um arquivo com "&lt;&lt;&lt;&lt;&lt;&lt;&lt;" no texto não é confundido.</summary>
    public const int TamanhoDoMarcador = 32;

    JuncaoConflito(IReadOnlyList<string> resultado, IReadOnlyList<ConflitoTexto> conflitos) => (Resultado, Conflitos) = (resultado, conflitos);

    public IReadOnlyList<string> Resultado { get; }

    public IReadOnlyList<ConflitoTexto> Conflitos { get; }

    public static string TextoDaMarca(int numero, int total) => $"    ⚠  Conflito {numero} de {total}   —   escolha um lado ou edite aqui";

    /// <summary>Linhas que ainda são aviso de conflito: número do conflito → linha (base 0).</summary>
    public static Dictionary<int, int> Pendentes(IReadOnlyList<string> linhas)
    {
        var pendentes = new Dictionary<int, int>();
        for (var i = 0; i < linhas.Count; i++)
        {
            if (!linhas[i].Contains('⚠')) continue;
            var marca = Marca().Match(linhas[i]);
            if (marca.Success) pendentes[int.Parse(marca.Groups[1].ValueSpan, System.Globalization.CultureInfo.InvariantCulture)] = i;
        }
        return pendentes;
    }

    public static JuncaoConflito Ler(IReadOnlyList<string> saidaDoMerge)
    {
        var abre = new string('<', TamanhoDoMarcador) + " ";
        var meio = new string('|', TamanhoDoMarcador) + " ";
        var separa = new string('=', TamanhoDoMarcador);
        var fecha = new string('>', TamanhoDoMarcador) + " ";

        var resultado = new List<string>();
        var conflitos = new List<(int Linha, List<string> Atual, List<string> Base, List<string> Entrando)>();
        List<string>? parte = null;
        foreach (var linha in saidaDoMerge)
        {
            if (parte is null && linha.StartsWith(abre, StringComparison.Ordinal))
            {
                conflitos.Add((resultado.Count, [], [], []));
                parte = conflitos[^1].Atual;
            }
            else if (parte is not null && linha.StartsWith(meio, StringComparison.Ordinal)) parte = conflitos[^1].Base;
            else if (parte is not null && linha == separa) parte = conflitos[^1].Entrando;
            else if (parte is not null && linha.StartsWith(fecha, StringComparison.Ordinal))
            {
                resultado.Add("");
                parte = null;
            }
            else (parte ?? resultado).Add(linha);
        }
        var lista = conflitos.Select((c, i) => new ConflitoTexto(i + 1, c.Atual, c.Base, c.Entrando)).ToList();
        for (var i = 0; i < conflitos.Count; i++) resultado[conflitos[i].Linha] = TextoDaMarca(i + 1, conflitos.Count);
        return new(resultado, lista);
    }

    [GeneratedRegex(@"^\s*⚠  Conflito (\d+) de \d+")]
    private static partial Regex Marca();
}
