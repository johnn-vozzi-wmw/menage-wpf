using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Menage.Pocs.Diff;

public enum TipoLinha { Igual, Inserida, Removida, Modificada, Vazia }

/// <summary>Intervalo [Inicio, Fim) de caracteres alterados dentro da linha (realce por palavra).</summary>
public record Trecho(int Inicio, int Fim);

public record LinhaDiff(TipoLinha Tipo, string Texto, IReadOnlyList<Trecho> Palavras);

public record ModeloDiff(IReadOnlyList<LinhaDiff> Esquerda, IReadOnlyList<LinhaDiff> Direita);

/// <summary>DiffPlex lado a lado: devolve as duas colunas já alinhadas (linhas "vazias" no lado sem correspondente).</summary>
public sealed class ConstrutorDiff
{
    public ModeloDiff Construir(string antigo, string novo)
    {
        var m = new SideBySideDiffBuilder(new Differ()).BuildDiffModel(antigo, novo, false);
        return new(Converter(m.OldText), Converter(m.NewText));
    }

    private static List<LinhaDiff> Converter(DiffPaneModel painel) =>
        painel.Lines.Select(p => new LinhaDiff(Tipo(p.Type), p.Text ?? "", Palavras(p))).ToList();

    private static TipoLinha Tipo(ChangeType t) => t switch
    {
        ChangeType.Inserted => TipoLinha.Inserida,
        ChangeType.Deleted => TipoLinha.Removida,
        ChangeType.Modified => TipoLinha.Modificada,
        ChangeType.Imaginary => TipoLinha.Vazia,
        _ => TipoLinha.Igual
    };

    private static List<Trecho> Palavras(DiffPiece linha)
    {
        var lista = new List<Trecho>();
        if (linha.Type != ChangeType.Modified) return lista;
        var pos = 0;
        foreach (var sp in linha.SubPieces)
        {
            if (sp.Type == ChangeType.Imaginary) continue;
            var len = sp.Text?.Length ?? 0;
            if (sp.Type != ChangeType.Unchanged && len > 0)
            {
                if (lista.Count > 0 && lista[^1].Fim == pos) lista[^1] = lista[^1] with { Fim = pos + len };
                else lista.Add(new Trecho(pos, pos + len));
            }
            pos += len;
        }
        return lista;
    }
}
