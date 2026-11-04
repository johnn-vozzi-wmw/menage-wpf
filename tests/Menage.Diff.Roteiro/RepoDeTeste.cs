using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Menage.TestKit;

namespace Menage.Diff.Roteiro;

/// <summary>Repositório descartável com um caso de cada: arquivo grande real, só à direita, excluído, renomeado, binário, invisível.</summary>
public sealed partial class RepoDeTeste : IDisposable
{
    const string Origem = @"C:\wmw\wmwvendas\projetos\lavenderepda\src\main\java\br\com\wmw\lavenderepda\Messages_en_US.java";

    public RepositorioDescartavel Repo { get; } = RepositorioDescartavel.Criar();

    public string Caminho => Repo.Caminho;

    public void Dispose() => Repo.Dispose();

    public void MontarAlteracoes()
    {
        var (antigo, novo) = Grande();
        var alinhamento = string.Concat(Enumerable.Range(1, 6000).Select(i => $"linha de alinhamento numero {i:D5} com texto para comparar\n"));
        var invisivel = string.Concat(Enumerable.Range(1, 60).Select(i => $"linha {i}\r\n"));
        Gravar("grande.java", Bytes(antigo));
        Gravar("alinhamento.txt", Bytes(alinhamento));
        var longas = string.Concat(Enumerable.Range(1, 300).Select(i => $"{i}: " + string.Concat(Enumerable.Repeat($"campo{i} valor ", 450)) + "\n"));
        Gravar("longas.txt", Bytes(longas));
        Gravar("invisivel.txt", Bytes(invisivel));
        Gravar("excluido.txt", Bytes("sai 1\nsai 2\nsai 3\n"));
        Gravar("velho.txt", Bytes("um\ndois\ntres\nquatro\ncinco\n"));
        Gravar("logo.bin", [1, 2, 0, 3]);
        Gravar("acentos1252.txt", Codificacao1252("ação\ncoração\n"));
        Repo.Rodar("add", ".");
        Repo.Rodar("commit", "-m", "base");

        Gravar("grande.java", Bytes(novo));
        Gravar("alinhamento.txt", Bytes(Alterar(alinhamento)));
        Gravar("longas.txt", Bytes(longas.Replace("150: campo150", "150: CAMPO150")));
        Gravar("so-direita.java", Bytes(string.Concat(Enumerable.Range(1, 3000).Select(i => $"    // só existe à direita, linha {i}\n"))));
        var bom = "\uFEFF" + invisivel.Replace("linha 10\r\n", "linha 10\n").Replace("linha 20\r\n", "linha VINTE\r\n").Replace("linha 30\r\n", "linha  30\r\n");
        Gravar("invisivel.txt", Bytes(bom));
        File.Delete(Path.Combine(Caminho, "excluido.txt"));
        Repo.Rodar("mv", "velho.txt", "renomeado.txt");
        Gravar("renomeado.txt", Bytes("um\ndois\nTRES\nquatro\ncinco\n"));
        Repo.Rodar("add", "--", "renomeado.txt");
        Gravar("logo.bin", [1, 2, 0, 4]);
        Gravar("acentos1252.txt", Bytes("ação\ncoração\n"));
    }

    /// <summary>Dois arquivos em conflito de texto (BOM + CRLF no atual) e um binário, com merge em andamento.</summary>
    public void MontarConflito()
    {
        var linhas = Enumerable.Range(1, 400).Select(i => $"linha {i} do arquivo\r\n").ToList();
        Gravar("Conflito.java", [0xEF, 0xBB, 0xBF, .. Bytes(string.Concat(linhas))]);
        Gravar("Outro.java", Bytes("a\nb\nc\nd\n"));
        Gravar("imagem.bin", [1, 0, 1]);
        Repo.Rodar("add", ".");
        Repo.Rodar("commit", "-m", "base");
        Repo.Rodar("checkout", "-b", "outra");
        var outra = linhas.ToList();
        outra[50] = "entrando: linha 51 mudou\r\n";
        outra.Insert(51, "entrando: linha extra\r\n");
        outra[200] = "entrando: linha 200\r\n";
        outra[300] = "entrando: 300\r\n";
        Gravar("Conflito.java", [0xEF, 0xBB, 0xBF, .. Bytes(string.Concat(outra))]);
        Gravar("Outro.java", Bytes("a\nB OUTRA\nc\nd\n"));
        Gravar("imagem.bin", [2, 0, 2]);
        Repo.Rodar("commit", "-am", "muda em outra");
        Repo.Rodar("checkout", "main");
        var atual = linhas.ToList();
        atual[50] = "atual: linha 51 foi trocada\r\n";
        atual[200] = "atual: linha 200\r\n";
        atual[300] = "atual: 300\r\n";
        atual[120] = "atual: mudou sem conflito\r\n";
        Gravar("Conflito.java", [0xEF, 0xBB, 0xBF, .. Bytes(string.Concat(atual))]);
        Gravar("Outro.java", Bytes("a\nB MAIN\nc\nd\n"));
        Gravar("imagem.bin", [3, 0, 3]);
        Repo.Rodar("commit", "-am", "muda na main");
        try { Repo.Rodar("merge", "outra"); }
        catch (InvalidOperationException) { /* o conflito é o que se quer */ }
    }

    public void Gravar(string arquivo, byte[] bytes) => File.WriteAllBytes(Path.Combine(Caminho, arquivo), bytes);

    static byte[] Bytes(string texto) => Encoding.UTF8.GetBytes(texto);

    static byte[] Codificacao1252(string texto) => Core.Diff.Codificacoes.Windows1252.GetBytes(texto);

    /// <summary>Poucas mudanças bem separadas, para sobrar trecho igual em volta da linha de sincronia.</summary>
    static string Alterar(string texto)
    {
        var linhas = texto.Split('\n').ToList();
        for (var i = 5000; i > 0; i -= 1000)
        {
            linhas.InsertRange(i, Enumerable.Range(1, 40).Select(n => $"bloco novo {i}-{n}"));
            linhas.RemoveRange(i + 300, 15);
        }
        return string.Join('\n', linhas);
    }

    /// <summary>O .java real (só leitura) com "linhas muito longas" e a versão nova com linhas alteradas, inseridas e removidas.</summary>
    static (string Antigo, string Novo) Grande()
    {
        var rnd = new Random(7);
        var antigo = File.ReadAllLines(Origem, Encoding.UTF8).ToList();
        for (var i = 0; i < antigo.Count; i += 25) antigo[i] += " // " + Cauda(rnd, 3000 + (i / 25 % 5 * 1500));
        var novo = new List<string>(antigo.Count + 200);
        for (var i = 0; i < antigo.Count; i++)
        {
            if (i % 173 == 3) continue;
            if (i >= 3000 && i < 3012)
            {
                if (i == 3000) novo.AddRange(Enumerable.Range(1, 5).Select(n => $"// bloco reescrito {n}"));
                continue;
            }
            novo.Add(i % 37 == 5 ? AlterarLinha(antigo[i]) : antigo[i]);
            if (i % 211 == 10) novo.AddRange(Enumerable.Range(1, 3).Select(n => $"// linha inserida {i}-{n}"));
        }
        return (string.Join('\n', antigo) + "\n", string.Join('\n', novo) + "\n");
    }

    static string AlterarLinha(string linha)
    {
        var palavras = Palavra().Matches(linha);
        if (palavras.Count == 0) return linha + " // alterada";
        var alvo = palavras[^1];
        return linha.Insert(alvo.Index + alvo.Length, "_v2");
    }

    static string Cauda(Random rnd, int tamanho)
    {
        var texto = new StringBuilder(tamanho + 40);
        while (texto.Length < tamanho) texto.Append("campo").Append(rnd.Next(10000)).Append(rnd.Next(3) == 0 ? ", " : " valor").Append(rnd.Next(100)).Append(' ');
        return texto.ToString();
    }

    [GeneratedRegex(@"[A-Za-z]{3,}")]
    private static partial Regex Palavra();
}
