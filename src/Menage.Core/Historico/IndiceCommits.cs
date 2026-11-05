namespace Menage.Core.Historico;

/// <summary>Filtro da busca; datas são inclusivas, por dia.</summary>
public sealed record FiltroHistorico(string Texto = "", string Autor = "", DateTime? De = null, DateTime? Ate = null);

/// <summary>
/// Índice de busca sobre uma lista de commits (ordem do git log: do mais novo para o mais antigo).
/// Índice invertido com as palavras num vetor ordenado: prefixo ("vend" acha "vendas", "9f3a" acha o hash) é busca binária mais
/// varredura do intervalo. Vários termos são combinados por interseção. Termos com pontuação ("VENDAS-22222") são confirmados
/// por comparação de texto. Se o índice não acha um termo, cai para busca de trecho só entre os candidatos.
/// </summary>
public sealed class IndiceCommits
{
    readonly IReadOnlyList<CommitLinha> _commits;
    readonly string[] _chaves;
    readonly int[][] _listas;

    public IndiceCommits(IReadOnlyList<CommitLinha> commits)
    {
        _commits = commits;
        var mapa = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < commits.Count; i++)
            foreach (var palavra in Palavras(commits[i].Texto))
            {
                if (!mapa.TryGetValue(palavra, out var lista)) mapa[palavra] = lista = new(2);
                if (lista.Count == 0 || lista[^1] != i) lista.Add(i);
            }

        _chaves = [.. mapa.Keys];
        Array.Sort(_chaves, StringComparer.Ordinal);
        _listas = Array.ConvertAll(_chaves, k => mapa[k].ToArray());
    }

    public int QuantidadePalavras => _chaves.Length;

    public static List<string> Palavras(string texto)
    {
        var palavras = new List<string>();
        var inicio = -1;
        for (var i = 0; i <= texto.Length; i++)
        {
            if (i < texto.Length && char.IsLetterOrDigit(texto[i]))
            {
                if (inicio < 0) inicio = i;
            }
            else if (inicio >= 0)
            {
                palavras.Add(texto[inicio..i]);
                inicio = -1;
            }
        }

        return palavras;
    }

    /// <summary>Índices em ordem crescente (do mais novo para o mais antigo) dos commits que atendem ao filtro.</summary>
    public int[] Buscar(FiltroHistorico filtro)
    {
        var candidatos = PorTexto(filtro.Texto);
        if (candidatos is { Length: 0 }) return candidatos;

        var autor = filtro.Autor.Trim().ToLowerInvariant();
        if (autor.Length == 0 && filtro.De is null && filtro.Ate is null) return candidatos ?? Todos();

        var de = filtro.De?.Date;
        var ate = filtro.Ate?.Date.AddDays(1);
        return Array.FindAll(candidatos ?? Todos(), i =>
        {
            var commit = _commits[i];
            return (autor.Length == 0 || commit.AutorTexto.Contains(autor, StringComparison.Ordinal))
                && (de is null || commit.Data >= de)
                && (ate is null || commit.Data < ate);
        });
    }

    // Nulo significa "sem filtro de texto": todos os commits são candidatos.
    int[]? PorTexto(string texto)
    {
        int[]? candidatos = null;
        foreach (var termo in texto.Trim().ToLowerInvariant().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var achados = PorTermo(termo, candidatos);
            candidatos = candidatos is null ? achados : Intersecao(candidatos, achados);
            if (candidatos.Length == 0) break;
        }

        return candidatos;
    }

    int[] PorTermo(string termo, int[]? candidatos)
    {
        var partes = Palavras(termo);
        int[]? achados = null;
        foreach (var parte in partes)
        {
            var comPrefixo = Prefixo(parte);
            achados = achados is null ? comPrefixo : Intersecao(achados, comPrefixo);
            if (achados.Length == 0) break;
        }

        if (achados is not { Length: > 0 }) return ComTrecho(candidatos ?? Todos(), termo);
        var temPontuacao = termo.Length != partes.Sum(p => p.Length);
        return temPontuacao ? ComTrecho(achados, termo) : achados;
    }

    int LimiteInferior(string prefixo)
    {
        int inicio = 0, fim = _chaves.Length;
        while (inicio < fim)
        {
            var meio = (inicio + fim) >> 1;
            if (string.CompareOrdinal(_chaves[meio], prefixo) < 0) inicio = meio + 1;
            else fim = meio;
        }

        return inicio;
    }

    // Commits que têm alguma palavra começando com o prefixo, em ordem crescente.
    int[] Prefixo(string prefixo)
    {
        var primeira = LimiteInferior(prefixo);
        var fim = primeira;
        while (fim < _chaves.Length && _chaves[fim].StartsWith(prefixo, StringComparison.Ordinal)) fim++;
        if (fim == primeira) return [];
        if (fim - primeira == 1) return _listas[primeira];

        var marca = new bool[_commits.Count];
        var total = 0;
        for (var k = primeira; k < fim; k++)
            foreach (var commit in _listas[k])
                if (!marca[commit])
                {
                    marca[commit] = true;
                    total++;
                }

        var resultado = new int[total];
        var posicao = 0;
        for (var commit = 0; commit < marca.Length && posicao < total; commit++)
            if (marca[commit]) resultado[posicao++] = commit;
        return resultado;
    }

    static int[] Intersecao(int[] a, int[] b)
    {
        if (a.Length > b.Length) (a, b) = (b, a);

        // a é bem menor: busca binária de cada elemento em b
        if ((long)a.Length * 16 < b.Length) return Array.FindAll(a, x => Array.BinarySearch(b, x) >= 0);

        var resultado = new List<int>(a.Length);
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j])
            {
                resultado.Add(a[i]);
                i++;
                j++;
            }
            else if (a[i] < b[j])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return [.. resultado];
    }

    int[] Todos()
    {
        var todos = new int[_commits.Count];
        for (var i = 0; i < todos.Length; i++) todos[i] = i;
        return todos;
    }

    int[] ComTrecho(int[] candidatos, string trecho) =>
        Array.FindAll(candidatos, c => _commits[c].Texto.Contains(trecho, StringComparison.Ordinal));
}
