using Menage.Core.Diff;

namespace Menage.App.Modulos.Diff.Texto;

/// <summary>O que cada linha do documento representa. Fora da lista (texto recém-editado) não há decoração.</summary>
public sealed class LinhasDoPainel
{
    public IReadOnlyList<LinhaVista> Itens { get; set; } = [];

    /// <summary>Numera pela posição no documento (painel editável) em vez do número guardado na linha.</summary>
    public bool NumerarPelaPosicao { get; init; }

    public int Quantas => Itens.Count;

    public LinhaVista? Em(int linhaDoDocumento) =>
        linhaDoDocumento >= 1 && linhaDoDocumento <= Itens.Count ? Itens[linhaDoDocumento - 1] : null;

    public int NumeroDe(int linhaDoDocumento) => NumerarPelaPosicao ? linhaDoDocumento : Em(linhaDoDocumento)?.Numero ?? 0;
}
