using System.Text;
using DiffPlex;

namespace Menage.Core.Diff;

/// <summary>
/// Compara dois arquivos em duas etapas. Primeiro alinha pelo texto exato, sem o fim de linha: é o alinhamento do
/// git, e um arquivo que só mudou de CRLF para LF sai na hora e linha a linha. Depois, só dentro de cada bloco
/// alterado, compara sem os espaços, para achar as linhas que só mudaram a indentação. Ignorar espaços já na primeira
/// etapa casava o "}" de um bloco inserido com o "}" antigo de outra indentação (visto em 23 de 150 arquivos reais).
/// </summary>
public sealed class ConstrutorDiff
{
    readonly Differ _differ = new();
    readonly PalavrasDiff _palavras;

    public ConstrutorDiff() => _palavras = new(_differ);

    public ModeloDiff Construir(ArquivoTexto? esquerda, ArquivoTexto? direita) => (esquerda, direita) switch
    {
        (null, null) => ModeloDiff.SemConteudo(SemTexto.Vazio),
        (null, { } d) => new ModeloDiff(null, d, [new(false, 0, 0, 0, d.Linhas.Count, [])]) { Novo = true },
        ({ } e, null) => new ModeloDiff(e, null, [new(false, 0, e.Linhas.Count, 0, 0, [])]) { Excluido = true },
        ({ } e, { } d) => Comparar(e, d),
    };

    ModeloDiff Comparar(ArquivoTexto esq, ArquivoTexto dir)
    {
        var segmentos = new List<Segmento>();
        var (i, j) = (0, 0);
        foreach (var bloco in BlocosDiff.Entre(esq.Linhas, dir.Linhas, _differ))
        {
            Iguais(esq, dir, i, j, bloco.AIni - i, segmentos);
            Detalhar(esq, dir, bloco, segmentos);
            (i, j) = (bloco.AIni + bloco.ALen, bloco.BIni + bloco.BLen);
        }
        Iguais(esq, dir, i, j, esq.Linhas.Count - i, segmentos);
        if (segmentos.Count == 0) segmentos.Add(new(true, 0, 0, 0, 0, []));
        var pares = segmentos.SelectMany(s => s.Pares).ToList();
        return new ModeloDiff(esq, dir, segmentos)
        {
            SoFimDeLinha = pares.Count(p => p.Tipo == Trivial.FimDeLinha),
            SoEspaco = pares.Count(p => p.Tipo == Trivial.Espaco),
        };
    }

    /// <summary>Dentro de um bloco alterado, separa o que só mudou espaços do que mudou de verdade.</summary>
    void Detalhar(ArquivoTexto esq, ArquivoTexto dir, Bloco bloco, List<Segmento> saida)
    {
        var (i, j) = (0, 0);
        foreach (var b in BlocosDiff.Entre(Chaves(esq, bloco.AIni, bloco.ALen), Chaves(dir, bloco.BIni, bloco.BLen), _differ))
        {
            Iguais(esq, dir, bloco.AIni + i, bloco.BIni + j, b.AIni - i, saida);
            saida.Add(Mudanca(esq, dir, new(bloco.AIni + b.AIni, b.ALen, bloco.BIni + b.BIni, b.BLen)));
            (i, j) = (b.AIni + b.ALen, b.BIni + b.BLen);
        }
        Iguais(esq, dir, bloco.AIni + i, bloco.BIni + j, bloco.ALen - i, saida);
    }

    /// <summary>Linhas que se correspondem: as idênticas viram segmento igual; as que diferem no invisível, um à parte.</summary>
    static void Iguais(ArquivoTexto esq, ArquivoTexto dir, int i, int j, int quantos, List<Segmento> saida)
    {
        for (var k = 0; k < quantos;)
        {
            var inicio = k;
            var identicas = Classificar(esq, dir, i + k, j + k) == Trivial.Nao;
            var pares = new List<Par>();
            while (k < quantos)
            {
                var tipo = Classificar(esq, dir, i + k, j + k);
                if (tipo == Trivial.Nao != identicas) break;
                if (tipo != Trivial.Nao) pares.Add(new(i + k, j + k, tipo, [], []));
                k++;
            }
            saida.Add(new(identicas, i + inicio, k - inicio, j + inicio, k - inicio, pares));
        }
    }

    Segmento Mudanca(ArquivoTexto esq, ArquivoTexto dir, Bloco bloco)
    {
        var pares = new List<Par>(Math.Min(bloco.ALen, bloco.BLen));
        for (var k = 0; k < Math.Min(bloco.ALen, bloco.BLen); k++)
        {
            var (e, d) = (bloco.AIni + k, bloco.BIni + k);
            var tipo = Classificar(esq, dir, e, d);
            var (pe, pd) = tipo == Trivial.Nao ? _palavras.Comparar(esq.Linhas[e], dir.Linhas[d]) : ([], []);
            pares.Add(new(e, d, tipo, pe, pd));
        }
        return new(false, bloco.AIni, bloco.ALen, bloco.BIni, bloco.BLen, pares);
    }

    static Trivial Classificar(ArquivoTexto esq, ArquivoTexto dir, int i, int j)
    {
        var (a, b) = (esq.Linhas[i], dir.Linhas[j]);
        var mesmoTexto = string.Equals(a, b, StringComparison.Ordinal);
        if (mesmoTexto && i == 0 && j == 0 && esq.TemBom != dir.TemBom) return Trivial.Bom;
        if (mesmoTexto) return esq.Fins[i] == dir.Fins[j] ? Trivial.Nao : Trivial.FimDeLinha;
        return string.Equals(Chave(a), Chave(b), StringComparison.Ordinal) ? Trivial.Espaco : Trivial.Nao;
    }

    static string[] Chaves(ArquivoTexto arquivo, int inicio, int tamanho) => arquivo.Linhas.Skip(inicio).Take(tamanho).Select(Chave).ToArray();

    /// <summary>A linha sem nada invisível: espaços, tabulações e o caractere do BOM.</summary>
    static string Chave(string linha)
    {
        var primeiro = 0;
        while (primeiro < linha.Length && !Invisivel(linha[primeiro])) primeiro++;
        if (primeiro == linha.Length) return linha;
        var chave = new StringBuilder(linha.Length).Append(linha, 0, primeiro);
        for (var i = primeiro; i < linha.Length; i++)
        {
            if (!Invisivel(linha[i])) chave.Append(linha[i]);
        }
        return chave.ToString();
    }

    static bool Invisivel(char c) => char.IsWhiteSpace(c) || c == '﻿';
}
