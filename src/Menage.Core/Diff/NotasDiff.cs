namespace Menage.Core.Diff;

/// <summary>Textos que explicam ao usuário uma mudança invisível.</summary>
public static class NotasDiff
{
    public static string Nota(ModeloDiff modelo, Par par) => par.Tipo switch
    {
        Trivial.FimDeLinha => $"fim de linha {NomeFim(modelo.Esquerda!.Fins[par.Esq])} → {NomeFim(modelo.Direita!.Fins[par.Dir])}",
        Trivial.Espaco => "só espaços / tabulação",
        _ => modelo.GanhouBom ? "ganhou BOM" : "perdeu BOM",
    };

    /// <summary>O que mudou num bloco inteiro de linhas de texto igual (aparece na ficha ao navegar até ele).</summary>
    public static string Motivo(ModeloDiff modelo, Segmento s)
    {
        if (s.Pares.All(p => p.Tipo == Trivial.FimDeLinha))
            return $"só fim de linha ({NomeFim(modelo.Esquerda!.Fins[s.EIni])} → {NomeFim(modelo.Direita!.Fins[s.DIni])}), o texto é igual";
        if (s.Pares.All(p => p.Tipo == Trivial.Espaco)) return "só espaços / tabulação";
        if (s.Pares.All(p => p.Tipo == Trivial.Bom)) return "só o BOM (marca invisível no início do arquivo), o texto é igual";
        return "só fim de linha, espaços ou BOM, o texto é igual";
    }

    /// <summary>Aviso âmbar acima do diff; vazio quando não há nada invisível.</summary>
    public static string Aviso(ModeloDiff modelo)
    {
        var partes = new List<string>();
        if (modelo.SoFimDeLinha > 0)
            partes.Add($"{modelo.SoFimDeLinha} linha(s) mudaram só o fim de linha ({modelo.Esquerda!.FimDominante} → {modelo.Direita!.FimDominante})");
        if (modelo.SoEspaco > 0) partes.Add($"{modelo.SoEspaco} linha(s) mudaram só espaços/tabulação");
        if (modelo.GanhouBom) partes.Add("o arquivo ganhou BOM (marca invisível de UTF-8 no início)");
        if (modelo.PerdeuBom) partes.Add("o arquivo perdeu o BOM");
        if (modelo.MudouCodificacao) partes.Add($"a codificação mudou ({modelo.Esquerda!.NomeCodificacao} → {modelo.Direita!.NomeCodificacao})");
        return partes.Count == 0 ? "" : $"⚠  {string.Join("   •   ", partes)}.  Isso aparece como alteração no git, mesmo sem mudança visível.";
    }

    public const string ExplicacaoDoAviso =
        "Mudanças que não aparecem no texto, mas o git conta como alteração.\n" +
        "Passe o mouse nas etiquetas das linhas para ver cada caso.\n" +
        "Com \"Ignorar fim de linha e espaços\" ligado, elas não entram na contagem.";

    public static string Titulo(string nota)
    {
        if (nota.StartsWith("fim de linha", StringComparison.Ordinal)) return "Só mudou o fim de linha";
        return nota.StartsWith("só espaços", StringComparison.Ordinal) ? "Só mudaram espaços" : "BOM";
    }

    public static string Explicacao(string nota)
    {
        if (nota.StartsWith("fim de linha", StringComparison.Ordinal))
        {
            return $"O texto da linha é igual: mudou só a quebra de linha invisível no fim dela ({nota["fim de linha ".Length..]}).\n" +
                "LF é o padrão Linux/Mac; CRLF é o padrão Windows.\n\n" +
                "Costuma acontecer quando o editor ou o git (core.autocrlf)\nregrava o arquivo com outro padrão.\n" +
                "Para não subir isso, mantenha o fim de linha original do arquivo no editor.";
        }
        if (nota.StartsWith("só espaços", StringComparison.Ordinal))
        {
            return "O código é o mesmo: mudaram só espaços ou tabulações (indentação, espaço no fim da linha).\n" +
                "Não muda o funcionamento, mas o git conta a linha como alterada.\n\n" +
                "Costuma vir do formatador automático ou de trocar tabulação por espaços.";
        }
        return "BOM (Byte Order Mark) é uma marca invisível de 3 bytes (EF BB BF)\n" +
            "no início de arquivos UTF-8. Alguns editores do Windows colocam ou tiram sozinhos.\n\n" +
            "O texto é igual, mas o git acusa a primeira linha como alterada,\n" +
            "e algumas ferramentas (scripts, alguns compiladores) podem estranhar o arquivo.\n\n" +
            (nota.StartsWith("ganhou", StringComparison.Ordinal) ? "Aqui o arquivo ganhou o BOM: o original não tinha." : "Aqui o arquivo perdeu o BOM: o original tinha.");
    }

    static string NomeFim(FimDeLinha fim) => fim switch
    {
        FimDeLinha.CrLf => "CRLF",
        FimDeLinha.Lf => "LF",
        _ => "sem quebra",
    };
}
