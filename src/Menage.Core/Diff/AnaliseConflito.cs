namespace Menage.Core.Diff;

/// <summary>Onde um conflito pendente está nos painéis de cima (índice e tamanho; -1 quando o lado não o tem).</summary>
public readonly record struct PosicaoConflito(int Numero, int LinhaResultado, int AtualIni, int AtualLen, int EntrandoIni, int EntrandoLen);

/// <summary>Estado de um resultado: o que está pendente, o que colorir em cada painel e as âncoras da rolagem.</summary>
public sealed record AnaliseConflito(
    IReadOnlyList<PosicaoConflito> Pendentes,
    IReadOnlyList<int> LinhasQueVieram,
    IReadOnlyList<int[]> Ancoras)
{
    /// <summary>Painéis nas âncoras: entrando, atual, resultado.</summary>
    public const int PainelEntrando = 0;
    public const int PainelAtual = 1;
    public const int PainelResultado = 2;

    /// <summary>Compara o resultado com cada lado. Sem tela e sem git, para rodar fora da thread de UI.</summary>
    public static AnaliseConflito Analisar(IReadOnlyList<string> atual, IReadOnlyList<string> entrando, IReadOnlyList<string> resultado)
    {
        var blocosAtual = BlocosDiff.Entre(atual, resultado);
        var blocosEntrando = BlocosDiff.Entre(entrando, resultado);
        var pendentes = JuncaoConflito.Pendentes(resultado);
        var posicoes = pendentes.OrderBy(p => p.Value).Select(p => Posicao(p.Key, p.Value, blocosAtual, blocosEntrando)).ToList();
        return new(posicoes, Vieram(blocosAtual, pendentes.Values, resultado.Count),
            Ancorar(blocosEntrando, blocosAtual, entrando.Count, atual.Count, resultado.Count));
    }

    static PosicaoConflito Posicao(int numero, int linha, IReadOnlyList<Bloco> atual, IReadOnlyList<Bloco> entrando)
    {
        var (a, e) = (BlocoDaLinha(atual, linha), BlocoDaLinha(entrando, linha));
        return new(numero, linha, a?.AIni ?? -1, a?.ALen ?? 0, e?.AIni ?? -1, e?.ALen ?? 0);
    }

    static Bloco? BlocoDaLinha(IReadOnlyList<Bloco> blocos, int linha)
    {
        foreach (var b in blocos)
        {
            if (linha >= b.BIni && linha < b.BIni + Math.Max(1, b.BLen)) return b;
        }
        return null;
    }

    /// <summary>Linhas do resultado que não estão no lado atual: vieram do outro lado (ou foram editadas).</summary>
    static List<int> Vieram(IReadOnlyList<Bloco> atual, IEnumerable<int> avisos, int total)
    {
        var marcas = avisos.ToHashSet();
        var vieram = new List<int>();
        foreach (var b in atual)
        {
            for (var l = b.BIni; l < Math.Min(b.BIni + b.BLen, total); l++)
            {
                if (!marcas.Contains(l)) vieram.Add(l);
            }
        }
        return vieram;
    }

    /// <summary>Junta as correspondências dos dois lados com o resultado numa lista só, em ordem de linha do resultado.</summary>
    static List<int[]> Ancorar(IReadOnlyList<Bloco> entrando, IReadOnlyList<Bloco> atual, int nEntrando, int nAtual, int nResultado)
    {
        var (ladoE, ladoA) = (new LadoDoResultado(entrando, nEntrando), new LadoDoResultado(atual, nAtual));
        var linhas = new SortedSet<int>(ladoE.LinhasDoResultado.Concat(ladoA.LinhasDoResultado)) { 0, nResultado };
        var ancoras = new List<int[]>();
        foreach (var r in linhas)
        {
            var (e, a) = (ladoE.Faixa(r), ladoA.Faixa(r));
            ancoras.Add([e.Inicio, a.Inicio, r]);
            if (e.Fim != e.Inicio || a.Fim != a.Inicio) ancoras.Add([e.Fim, a.Fim, r]);
        }
        return ancoras;
    }

    /// <summary>Para uma linha do resultado, a faixa correspondente de linhas de um lado (trechos iguais andam juntos).</summary>
    sealed class LadoDoResultado(IReadOnlyList<Bloco> blocos, int totalDoLado)
    {
        public IEnumerable<int> LinhasDoResultado => blocos.SelectMany(b => new[] { b.BIni, b.BIni + b.BLen });

        public (int Inicio, int Fim) Faixa(int resultado)
        {
            var deslocamento = 0;
            foreach (var b in blocos)
            {
                if (resultado < b.BIni) break;
                if (resultado == b.BIni) return (b.AIni, b.BLen == 0 ? b.AIni + b.ALen : b.AIni);
                if (resultado < b.BIni + b.BLen) return (b.AIni + b.ALen, b.AIni + b.ALen);
                deslocamento = b.AIni + b.ALen - (b.BIni + b.BLen);
            }
            var linha = Math.Min(totalDoLado, resultado + deslocamento);
            return (linha, linha);
        }
    }
}
