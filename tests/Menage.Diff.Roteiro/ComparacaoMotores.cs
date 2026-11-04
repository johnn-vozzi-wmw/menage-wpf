using System.Diagnostics;
using System.IO;
using System.Text;
using Menage.Core.Diff;
using Menage.Core.Processos;

namespace Menage.Diff.Roteiro;

/// <summary>
/// DiffPlex (como o Core usa) contra o modelo do app antigo (<c>git diff -U100000000</c>, lido bloco a bloco) em
/// arquivos reais do lavenderepda. Só leitura: <c>git log</c> e <c>git show</c>; o diff do git roda em %TEMP%.
/// </summary>
public sealed class ComparacaoMotores
{
    const string Repo = @"C:\wmw\wmwvendas\projetos\lavenderepda";
    const int Pares = 150;

    readonly ProcessoRunner _runner = new();
    readonly ConstrutorDiff _construtor = new();

    public async Task<object> RodarAsync()
    {
        var pasta = Directory.CreateTempSubdirectory("mg-motores-").FullName;
        var r = new Resultados();
        try
        {
            foreach (var (commit, arquivo) in await ParesAsync())
            {
                var antes = await ShowAsync($"{commit}~1:{arquivo}");
                var depois = await ShowAsync($"{commit}:{arquivo}");
                if (antes is null || depois is null || Codificacoes.Binario(antes) || Codificacoes.Binario(depois)) continue;
                await CompararAsync(pasta, arquivo, antes, depois, r);
            }
            await PiorCasoAsync(pasta, r);
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
        return r.Resumo();
    }

    async Task CompararAsync(string pasta, string arquivo, byte[] antes, byte[] depois, Resultados r)
    {
        var relogio = Stopwatch.StartNew();
        var modelo = _construtor.Construir(ArquivoTexto.De(antes), ArquivoTexto.De(depois));
        r.Plex.Add(relogio.Elapsed.TotalMilliseconds);
        relogio.Restart();
        var (remGit, addGit, blocosGit) = await GitDiffAsync(pasta, antes, depois);
        r.Git.Add(relogio.Elapsed.TotalMilliseconds);
        var (remPlex, addPlex, blocosPlex) = Contar(modelo);
        r.Comparados++;
        if (!Reconstroi(modelo)) r.NaoReconstroi.Add(arquivo);
        if (remPlex == remGit && addPlex == addGit) r.MesmasLinhas++;
        else if (r.Diferentes.Count < 12) r.Diferentes.Add($"{Path.GetFileName(arquivo)}: git -{remGit}+{addGit} ({blocosGit} blocos) x plex -{remPlex}+{addPlex} ({blocosPlex} blocos, {modelo.SoEspaco} só espaço, {modelo.SoFimDeLinha} só fim)");
        if (blocosPlex == blocosGit) r.MesmosBlocos++;
    }

    /// <summary>Linhas que mudam de verdade: as só invisíveis contam como mudança no git, então entram na conta.</summary>
    static (int Rem, int Add, int Blocos) Contar(ModeloDiff m) =>
        (m.Segmentos.Where(s => !s.Igual).Sum(s => s.ELen), m.Segmentos.Where(s => !s.Igual).Sum(s => s.DLen), m.Segmentos.Count(s => !s.Igual));

    /// <summary>Os segmentos têm que cobrir os dois arquivos, em ordem, sem buraco.</summary>
    static bool Reconstroi(ModeloDiff m)
    {
        var (e, d) = (0, 0);
        foreach (var s in m.Segmentos)
        {
            if (s.EIni != e || s.DIni != d) return false;
            if (s.Igual && !m.Esq.Skip(s.EIni).Take(s.ELen).SequenceEqual(m.Dir.Skip(s.DIni).Take(s.DLen))) return false;
            (e, d) = (e + s.ELen, d + s.DLen);
        }
        return e == m.Esq.Count && d == m.Dir.Count;
    }

    /// <summary>Pior caso: o arquivo grande inteiro virando de CRLF para LF, e dois arquivos sem nada em comum.</summary>
    async Task PiorCasoAsync(string pasta, Resultados r)
    {
        var linhas = Enumerable.Range(1, 8000).Select(i => $"Messages.CAMPO_{i} = \"texto {i}\";").ToList();
        var crlf = Encoding.UTF8.GetBytes(string.Join("\r\n", linhas) + "\r\n");
        var lf = Encoding.UTF8.GetBytes(string.Join("\n", linhas) + "\n");
        var diferente = Encoding.UTF8.GetBytes(string.Join("\n", linhas.Select(l => l.Replace("CAMPO", "OUTRO", StringComparison.Ordinal))) + "\n");
        foreach (var (nome, a, b) in new[] { ("fim_de_linha_8000", crlf, lf), ("nada_em_comum_8000", lf, diferente) })
        {
            var relogio = Stopwatch.StartNew();
            var m = _construtor.Construir(ArquivoTexto.De(a), ArquivoTexto.De(b));
            var plex = relogio.Elapsed.TotalMilliseconds;
            relogio.Restart();
            var (rem, add, blocos) = await GitDiffAsync(pasta, a, b);
            r.Piores[nome] = new { plexMs = Math.Round(plex), gitMs = Math.Round(relogio.Elapsed.TotalMilliseconds), plexBlocos = m.Segmentos.Count(s => !s.Igual && !s.SoInvisivel), plexSoFim = m.SoFimDeLinha, gitBlocos = blocos, gitLinhas = rem + add };
        }
    }

    async Task<List<(string Commit, string Arquivo)>> ParesAsync()
    {
        var saida = await GitAsync(Repo, "log", "--no-merges", "-400", "--name-only", "--diff-filter=M", "--format=#%H", "--", "*.java");
        var pares = new List<(string, string)>();
        var commit = "";
        foreach (var linha in Encoding.UTF8.GetString(saida.Saida).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (linha[0] == '#') commit = linha[1..];
            else if (pares.Count < Pares) pares.Add((commit, linha));
        }
        return pares;
    }

    async Task<byte[]?> ShowAsync(string revisao)
    {
        var saida = await GitAsync(Repo, "show", revisao);
        return saida.Sucesso ? saida.Saida : null;
    }

    /// <summary>O caminho do app antigo: git diff com contexto infinito, lido linha a linha.</summary>
    async Task<(int Rem, int Add, int Blocos)> GitDiffAsync(string pasta, byte[] antes, byte[] depois)
    {
        await File.WriteAllBytesAsync(Path.Combine(pasta, "a"), antes);
        await File.WriteAllBytesAsync(Path.Combine(pasta, "b"), depois);
        var saida = await GitAsync(pasta, "diff", "--no-index", "--no-color", "--no-ext-diff", "-U100000000", "a", "b");
        var (rem, add, blocos, dentro) = (0, 0, 0, false);
        var corpo = false;
        foreach (var linha in Encoding.Latin1.GetString(saida.Saida).Split('\n'))
        {
            if (!corpo)
            {
                corpo = linha.StartsWith("@@", StringComparison.Ordinal);
                continue;
            }
            var mudou = linha.StartsWith('-') || linha.StartsWith('+');
            if (mudou && !dentro) blocos++;
            dentro = mudou;
            if (linha.StartsWith('-')) rem++;
            else if (linha.StartsWith('+')) add++;
        }
        return (rem, add, blocos);
    }

    Task<SaidaProcesso> GitAsync(string pasta, params string[] args) =>
        _runner.ExecutarAsync(new ComandoProcesso("git", args, pasta, null, TimeSpan.FromMinutes(2)));

    sealed class Resultados
    {
        public List<double> Plex { get; } = [];

        public List<double> Git { get; } = [];

        public List<string> Diferentes { get; } = [];

        public List<string> NaoReconstroi { get; } = [];

        public Dictionary<string, object> Piores { get; } = [];

        public int Comparados { get; set; }

        public int MesmasLinhas { get; set; }

        public int MesmosBlocos { get; set; }

        public object Resumo() => new { Comparados, MesmasLinhas, MesmosBlocos, plexMs = Estatistica.De(Plex), gitMs = Estatistica.De(Git), NaoReconstroi, Diferentes, Piores };
    }
}
