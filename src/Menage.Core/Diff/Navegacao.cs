namespace Menage.Core.Diff;

public static class Navegacao
{
    /// <summary>
    /// Próximo item numa lista crescente de linhas. Se o item atual ainda está na tela, anda a partir dele; se o
    /// usuário rolou para outro lugar, parte do que está na tela (senão "pula" para longe). Dá a volta nas pontas.
    /// </summary>
    public static int Proxima(IReadOnlyList<int> linhas, int atual, int topo, int naTela, int passo)
    {
        var n = linhas.Count;
        if (n == 0) return -1;
        if (atual >= 0 && atual < n && linhas[atual] >= topo && linhas[atual] < topo + Math.Max(1, naTela))
            return (((atual + passo) % n) + n) % n;
        var indices = Enumerable.Range(0, n);
        var achado = passo > 0
            ? indices.Where(i => linhas[i] >= topo).DefaultIfEmpty(-1).First()
            : indices.Where(i => linhas[i] < topo).DefaultIfEmpty(-1).Last();
        return achado >= 0 ? achado : passo > 0 ? 0 : n - 1;
    }
}
