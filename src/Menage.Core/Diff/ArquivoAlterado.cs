namespace Menage.Core.Diff;

public enum TipoAlteracao { Modificado, Adicionado, Excluido, Novo, Renomeado, EmConflito }

/// <summary>Arquivo da lista de alterações. <c>Caminho</c> é relativo ao repositório, com "/"; <c>Antigo</c>, o nome no HEAD quando renomeado.</summary>
public sealed record ArquivoAlterado(string Caminho, TipoAlteracao Tipo, string? Antigo = null, string CodigoStatus = "")
{
    public string Nome => Caminho[(Caminho.LastIndexOf('/') + 1)..];

    public string Pasta => Caminho.Contains('/') ? Caminho[..Caminho.LastIndexOf('/')] : "";

    public char Letra => Tipo switch
    {
        TipoAlteracao.Novo => 'N',
        TipoAlteracao.Adicionado => 'A',
        TipoAlteracao.Excluido => 'D',
        TipoAlteracao.Renomeado => 'R',
        TipoAlteracao.EmConflito => 'U',
        _ => 'M',
    };

    public string Descricao => Tipo switch
    {
        TipoAlteracao.Novo => "novo (fora do git)",
        TipoAlteracao.Adicionado => "adicionado",
        TipoAlteracao.Excluido => "excluído",
        TipoAlteracao.Renomeado => "renomeado de " + Antigo,
        TipoAlteracao.EmConflito => "em conflito",
        _ => "modificado",
    };

    /// <summary>
    /// Lê a saída de <c>git status --porcelain -z -uall</c>: entradas separadas por NUL, sem aspas nem escapes
    /// (acentos vêm como estão) e, nas renomeadas, o nome antigo como entrada seguinte.
    /// </summary>
    public static IReadOnlyList<ArquivoAlterado> LerStatus(string saida)
    {
        var lista = new List<ArquivoAlterado>();
        var partes = saida.Split('\0');
        for (var i = 0; i < partes.Length; i++)
        {
            var entrada = partes[i];
            if (entrada.Length < 4 || entrada.StartsWith("!!", StringComparison.Ordinal)) continue;
            var codigo = entrada[..2];
            var renomeado = codigo[0] is 'R' or 'C' || codigo[1] is 'R' or 'C';
            var antigo = renomeado && i + 1 < partes.Length ? partes[++i] : null;
            var tipo = Classificar(codigo);
            lista.Add(new(entrada[3..], tipo, tipo == TipoAlteracao.Renomeado ? antigo : null, codigo));
        }
        return lista;
    }

    static TipoAlteracao Classificar(string codigo)
    {
        if (codigo == "??") return TipoAlteracao.Novo;
        if (codigo.Contains('U') || codigo is "AA" or "DD") return TipoAlteracao.EmConflito;
        if (codigo.Contains('D')) return TipoAlteracao.Excluido;
        if (codigo[0] == 'R' || codigo[1] == 'R') return TipoAlteracao.Renomeado;
        return codigo[0] == 'A' ? TipoAlteracao.Adicionado : TipoAlteracao.Modificado;
    }
}
