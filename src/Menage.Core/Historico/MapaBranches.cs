namespace Menage.Core.Historico;

/// <summary>Consulta instantânea de "em quais branches está este commit", sem um <c>git branch --contains</c> por commit.</summary>
public sealed class MapaBranches
{
    readonly string[] _nomes;
    readonly Dictionary<string, int> _posicoes;
    readonly ulong[] _mascaras;
    readonly int _palavras;

    MapaBranches(string[] nomes, Dictionary<string, int> posicoes, ulong[] mascaras, int palavras) =>
        (_nomes, _posicoes, _mascaras, _palavras) = (nomes, posicoes, mascaras, palavras);

    public int Branches => _nomes.Length;

    public int Commits => _posicoes.Count;

    public IReadOnlyList<string> De(string hash)
    {
        if (!_posicoes.TryGetValue(hash, out var posicao)) return [];
        var nomes = new List<string>();
        for (var b = 0; b < _nomes.Length; b++)
            if ((_mascaras[(posicao * _palavras) + (b >> 6)] & (1UL << (b & 63))) != 0) nomes.Add(_nomes[b]);
        return nomes;
    }

    /// <summary>
    /// Propaga, dos filhos para os pais, um bitset com as branches que alcançam cada commit: O(commits x branches/64).
    /// </summary>
    /// <param name="pontas">Nome da branch e hash da ponta.</param>
    /// <param name="linhas">Saída de <c>git rev-list --topo-order --parents</c>: filhos sempre antes dos pais.</param>
    public static MapaBranches Montar(IReadOnlyList<(string Nome, string Hash)> pontas, IReadOnlyList<string> linhas)
    {
        var posicoes = new Dictionary<string, int>(linhas.Count);
        foreach (var linha in linhas) posicoes[linha.Split(' ')[0]] = posicoes.Count;

        var palavras = Math.Max(1, (pontas.Count + 63) / 64);
        var mascaras = new ulong[Math.Max(1, posicoes.Count * palavras)];
        for (var b = 0; b < pontas.Count; b++)
            if (posicoes.TryGetValue(pontas[b].Hash, out var ponta)) mascaras[(ponta * palavras) + (b >> 6)] |= 1UL << (b & 63);

        for (var i = 0; i < linhas.Count; i++)
            foreach (var pai in linhas[i].Split(' ').Skip(1))
                if (posicoes.TryGetValue(pai, out var p))
                    for (var w = 0; w < palavras; w++) mascaras[(p * palavras) + w] |= mascaras[(i * palavras) + w];

        return new([.. pontas.Select(x => x.Nome)], posicoes, mascaras, palavras);
    }
}
