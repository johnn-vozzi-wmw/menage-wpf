namespace Menage.Core.Diff;

/// <summary>Por que uma linha aparece como alterada sem mudança real de texto.</summary>
public enum Trivial { Nao, FimDeLinha, Espaco, Bom }

/// <summary>Trecho de caracteres [Inicio, Inicio + Tamanho) dentro de uma linha.</summary>
public readonly record struct Trecho(int Inicio, int Tamanho);

/// <summary>Linha removida emparelhada com a adicionada na mesma posição do bloco.</summary>
public sealed record Par(int Esq, int Dir, Trivial Tipo, IReadOnlyList<Trecho> PalavrasEsq, IReadOnlyList<Trecho> PalavrasDir);

/// <summary>Trecho do arquivo: igual nos dois lados ou uma mudança. Índices base 0 nas listas de linhas de cada lado.</summary>
public sealed record Segmento(bool Igual, int EIni, int ELen, int DIni, int DLen, IReadOnlyList<Par> Pares)
{
    public bool SoInvisivel => !Igual && ELen == DLen && Pares.Count == ELen && Pares.All(p => p.Tipo != Trivial.Nao);
}

/// <summary>Por que o diff não pode ser mostrado como texto.</summary>
public enum SemTexto { Nao, Binario, MuitoGrande, Vazio }

/// <summary>Resultado da comparação de um arquivo inteiro (sem linhas de preenchimento).</summary>
public sealed record ModeloDiff(ArquivoTexto? Esquerda, ArquivoTexto? Direita, IReadOnlyList<Segmento> Segmentos)
{
    static readonly IReadOnlyList<string> _semLinhas = [];

    public SemTexto Motivo { get; init; }

    public bool Novo { get; init; }

    public bool Excluido { get; init; }

    public int SoFimDeLinha { get; init; }

    public int SoEspaco { get; init; }

    public IReadOnlyList<string> Esq => Esquerda?.Linhas ?? _semLinhas;

    public IReadOnlyList<string> Dir => Direita?.Linhas ?? _semLinhas;

    public bool GanhouBom => Esquerda is { TemBom: false } && Direita is { TemBom: true };

    public bool PerdeuBom => Esquerda is { TemBom: true } && Direita is { TemBom: false };

    public bool MudouCodificacao =>
        Esquerda is { Linhas.Count: > 0 } e && Direita is { Linhas.Count: > 0 } d && e.Codificacao.CodePage != d.Codificacao.CodePage;

    public static ModeloDiff SemConteudo(SemTexto motivo, bool novo = false, bool excluido = false) =>
        new(null, null, []) { Motivo = motivo, Novo = novo, Excluido = excluido };
}
