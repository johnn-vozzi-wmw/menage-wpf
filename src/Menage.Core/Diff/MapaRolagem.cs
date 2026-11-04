namespace Menage.Core.Diff;

/// <summary>
/// Liga N painéis de alturas diferentes (sem linhas de preenchimento) a uma única posição de rolagem.
/// Coordenada unificada: entre duas âncoras ela anda o tanto do painel mais alto, então nenhum painel "dispara".
/// O meio da tela é a linha de sincronia: <c>Y = limite(linha(U + meio) - meio)</c>. Com o meio fixo, em trecho igual a
/// diferença entre dois painéis é sempre um número inteiro de linhas, ou seja, as linhas iguais ficam na mesma altura
/// da tela, pixel a pixel. Como cada painel anda no máximo o tanto da coordenada unificada, o limite faz todos
/// começarem e terminarem juntos, e nenhum anda para trás.
/// </summary>
public sealed class MapaRolagem
{
    readonly double[] _unificado;
    readonly double[][] _linhaPx;
    readonly double[] _limite;
    readonly double _meio;

    /// <param name="ancoras">Linhas que se correspondem, uma posição por painel, crescentes.</param>
    /// <param name="totais">Quantas linhas cada painel tem.</param>
    public MapaRolagem(IEnumerable<int[]> ancoras, IReadOnlyList<int> totais, double alturaLinha, double viewport)
    {
        var pontos = Pontos(ancoras, totais);
        var paineis = totais.Count;
        _linhaPx = new double[paineis][];
        for (var p = 0; p < paineis; p++) _linhaPx[p] = pontos.Select(a => a[p] * alturaLinha).ToArray();
        _unificado = Unificar(_linhaPx);
        _limite = totais.Select(t => Math.Max(0, (t * alturaLinha) - viewport)).ToArray();
        _meio = viewport / 2;
        Viewport = viewport;
        Altura = _unificado[^1];
        Maximo = Math.Max(0, Altura - viewport);
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
        var linha = Interpolar(_unificado, _linhaPx[painel], Math.Clamp(u, 0, Maximo) + _meio);
        return Math.Clamp(linha - _meio, 0, _limite[painel]);
    }

    /// <summary>Posição de rolagem em que o painel fica no deslocamento <paramref name="y"/>.</summary>
    public double UDeY(int painel, double y) =>
        Maximo <= 0 ? 0 : Math.Clamp(Interpolar(_linhaPx[painel], _unificado, y + _meio) - _meio, 0, Maximo);

    /// <summary>Posição de rolagem que deixa uma linha do painel na linha de sincronia: os dois lados da mudança se encontram.</summary>
    public double UDaLinha(int painel, double linhaPx) => Math.Clamp(Unificado(painel, linhaPx) - _meio, 0, Maximo);

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
