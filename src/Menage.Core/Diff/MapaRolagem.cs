namespace Menage.Core.Diff;

/// <summary>
/// Liga N painéis de alturas diferentes (sem linhas de preenchimento) a uma única posição de rolagem.
/// Coordenada unificada: entre duas âncoras ela anda o tanto do painel mais alto, então nenhum painel "dispara".
/// A linha de sincronia acompanha a rolagem (no topo está no alto da tela, no fim, embaixo): todos os painéis
/// chegam ao começo e ao fim juntos. O resultado é pré-calculado por âncora, sempre crescente em cada painel
/// (nenhum painel anda para trás), e cada consulta é uma busca binária mais uma interpolação.
/// </summary>
public sealed class MapaRolagem
{
    readonly double[] _u;
    readonly double[][] _y;
    readonly double[] _unificado;
    readonly double[][] _linhaPx;

    /// <param name="ancoras">Linhas que se correspondem, uma posição por painel, crescentes.</param>
    /// <param name="totais">Quantas linhas cada painel tem.</param>
    public MapaRolagem(IEnumerable<int[]> ancoras, IReadOnlyList<int> totais, double alturaLinha, double viewport)
    {
        var pontos = Pontos(ancoras, totais);
        var paineis = totais.Count;
        _linhaPx = new double[paineis][];
        for (var p = 0; p < paineis; p++) _linhaPx[p] = pontos.Select(a => a[p] * alturaLinha).ToArray();
        _unificado = Unificar(_linhaPx);
        Viewport = viewport;
        Altura = _unificado[^1];
        Maximo = Math.Max(0, Altura - viewport);
        var escala = Altura > 0 ? Maximo / Altura : 0;
        _u = _unificado.Select(u => u * escala).ToArray();
        _y = new double[paineis][];
        for (var p = 0; p < paineis; p++) _y[p] = PosicoesDoPainel(_linhaPx[p], totais[p] * alturaLinha);
    }

    /// <summary>Altura unificada (todas as linhas, o maior painel em cada trecho).</summary>
    public double Altura { get; }

    /// <summary>Maior posição de rolagem.</summary>
    public double Maximo { get; }

    public double Viewport { get; }

    /// <summary>Deslocamento vertical do painel na posição de rolagem <paramref name="u"/>.</summary>
    public double Y(int painel, double u)
    {
        if (Maximo <= 0) return 0;
        return Interpolar(_u, _y[painel], Math.Clamp(u, 0, Maximo));
    }

    /// <summary>Posição de rolagem em que o painel fica no deslocamento <paramref name="y"/>.</summary>
    public double UDeY(int painel, double y) => Maximo <= 0 ? 0 : Interpolar(_y[painel], _u, y);

    /// <summary>Posição na coordenada unificada (0 a <see cref="Altura"/>) de uma linha do painel; a régua usa esta escala.</summary>
    public double Unificado(int painel, double linhaPx) => Interpolar(_linhaPx[painel], _unificado, linhaPx);

    static List<int[]> Pontos(IEnumerable<int[]> ancoras, IReadOnlyList<int> totais)
    {
        var pontos = new List<int[]> { new int[totais.Count] };
        foreach (var a in ancoras)
        {
            if (a.Length != totais.Count) throw new ArgumentException("Âncora com número de painéis errado.", nameof(ancoras));
            if (!a.SequenceEqual(pontos[^1])) pontos.Add(a);
        }
        var fim = totais.ToArray();
        if (!fim.SequenceEqual(pontos[^1])) pontos.Add(fim);
        return pontos;
    }

    static double[] Unificar(double[][] linhas)
    {
        var n = linhas[0].Length;
        var unificado = new double[n];
        for (var k = 1; k < n; k++)
        {
            var passo = 0.0;
            foreach (var painel in linhas) passo = Math.Max(passo, painel[k] - painel[k - 1]);
            unificado[k] = unificado[k - 1] + passo;
        }
        return unificado;
    }

    double[] PosicoesDoPainel(double[] linhas, double alturaPainel)
    {
        var limite = Math.Max(0, alturaPainel - Viewport);
        var y = new double[linhas.Length];
        var anterior = 0.0;
        for (var k = 0; k < y.Length; k++)
        {
            var fracao = Altura > 0 ? _unificado[k] / Altura : 0;
            var bruto = linhas[k] - (fracao * Viewport);
            anterior = Math.Max(anterior, Math.Clamp(bruto, 0, limite));
            y[k] = anterior;
        }
        return y;
    }

    /// <summary>Interpola <paramref name="saida"/> em função de <paramref name="entrada"/> (crescente, pode ter trechos parados).</summary>
    static double Interpolar(double[] entrada, double[] saida, double valor)
    {
        if (valor <= entrada[0]) return saida[0];
        if (valor >= entrada[^1]) return saida[^1];
        var (baixo, alto) = (0, entrada.Length - 1);
        while (alto - baixo > 1)
        {
            var meio = (baixo + alto) / 2;
            if (entrada[meio] <= valor) baixo = meio;
            else alto = meio;
        }
        var trecho = entrada[alto] - entrada[baixo];
        return trecho <= 0 ? saida[baixo] : saida[baixo] + ((valor - entrada[baixo]) / trecho * (saida[alto] - saida[baixo]));
    }
}
