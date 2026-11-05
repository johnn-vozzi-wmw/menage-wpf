namespace Menage.Core.Diff;

public enum TipoLinha { Igual, Removida, Adicionada, Invisivel, Dobra, Conflito, Veio, AlteradaAtual, AlteradaEntrando }

/// <summary>Uma linha mostrada num painel. <c>Numero</c> é a linha real no arquivo (0 quando não há).</summary>
public sealed record LinhaVista(string Texto, int Numero, TipoLinha Tipo)
{
    public IReadOnlyList<Trecho> Palavras { get; init; } = [];

    /// <summary>Por que a linha mudou sem mudar o texto.</summary>
    public Trivial Trivial { get; init; }

    /// <summary>Texto da etiqueta ("fim de linha LF → CRLF").</summary>
    public string? Nota { get; init; }

    /// <summary>A nota não vira etiqueta no fim da linha: só dica na marca da margem (linha ignorada).</summary>
    public bool SoDica { get; init; }

    /// <summary>Primeira linha (índice no painel direito) do trecho recolhido; identifica o trecho ao abrir.</summary>
    public int Dobra { get; init; } = -1;

    /// <summary>Quantas linhas o trecho recolhido esconde.</summary>
    public int Escondidas { get; init; }

    /// <summary>Índice no arquivo da primeira linha escondida, neste lado.</summary>
    public int PrimeiraEscondida { get; init; }
}

/// <summary>Uma mudança na tela: faixa de linhas de cada painel. <c>Motivo</c> preenchido quando só mudou o invisível.</summary>
public sealed record BlocoVista(int ERow, int ERows, int DRow, int DRows, string? Motivo = null);

/// <summary>O que os painéis mostram, já com os trechos iguais recolhidos e as âncoras para a rolagem.</summary>
public sealed record VistaDiff(
    IReadOnlyList<LinhaVista> Esq,
    IReadOnlyList<LinhaVista> Dir,
    IReadOnlyList<BlocoVista> Blocos,
    IReadOnlyList<int[]> Ancoras,
    int Adicionadas,
    int Removidas)
{
    /// <summary>Linha de cada painel que deve ficar na tela ao abrir ou reconstruir: índice no arquivo → índice no painel.</summary>
    public int LinhaDoPainelDireito(int linhaArquivo)
    {
        for (var i = 0; i < Dir.Count; i++)
        {
            var linha = Dir[i];
            if (linha.Numero - 1 >= linhaArquivo) return i;
            if (linha.Tipo == TipoLinha.Dobra && linha.PrimeiraEscondida + linha.Escondidas > linhaArquivo) return i;
        }
        return Math.Max(0, Dir.Count - 1);
    }

    /// <summary>Linha real do arquivo mostrada numa linha do painel direito.</summary>
    public int LinhaDoArquivo(int linhaPainel)
    {
        if (linhaPainel < 0 || linhaPainel >= Dir.Count) return 0;
        var linha = Dir[linhaPainel];
        return linha.Tipo == TipoLinha.Dobra ? linha.PrimeiraEscondida : Math.Max(0, linha.Numero - 1);
    }
}
