using System.Diagnostics;
using System.Text;
using Menage.Core.Historico;
using Xunit.Abstractions;

namespace Menage.Tests.NucleoGit;

public sealed class IndiceCommitsTests(ITestOutputHelper saida)
{
    static List<CommitLinha> Sintetico(int n, int semente)
    {
        var rnd = new Random(semente);
        string[] autores = ["Ana Souza", "Bruno Lima", "Carla Dias", "Diego Rocha", "Eva Mota"];
        string[] palavras = ["ajuste", "pedido", "cliente", "estoque", "relatorio", "tela", "login", "preco", "nota", "frete", "sincronizacao", "cadastro"];
        var fim = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var lista = new List<CommitLinha>(n);
        for (var i = 0; i < n; i++)
        {
            var hash = new StringBuilder();
            for (var k = 0; k < 40; k++) hash.Append("0123456789abcdef"[rnd.Next(16)]);
            var autor = autores[rnd.Next(autores.Length)];
            var assunto = $"VENDAS-{10000 + rnd.Next((n / 3) + 1)} {palavras[rnd.Next(palavras.Length)]} {palavras[rnd.Next(palavras.Length)]}{(rnd.Next(10) == 0 ? " hotfix" : "")}";
            var refs = rnd.Next(50) == 0 ? $"origin/release-{rnd.Next(20)}" : "";
            lista.Add(new CommitLinha(hash.ToString(), autor, autor.Split(' ')[0].ToLowerInvariant() + "@wmw.com.br", assunto, refs, fim.AddMinutes(-i * 37)));
        }

        return lista;
    }

    // Referência lenta, escrita sem o índice: mesma regra (prefixo de palavra; sem resultado = trecho).
    static int[] ForcaBruta(List<CommitLinha> commits, string consulta)
    {
        IEnumerable<int> candidatos = Enumerable.Range(0, commits.Count);
        foreach (var termo in consulta.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = IndiceCommits.Palavras(termo);
            var pontuacao = partes.Count == 0 || termo.Length != partes.Sum(p => p.Length);
            var achou = Enumerable.Range(0, commits.Count).Where(i =>
            {
                var palavras = IndiceCommits.Palavras(commits[i].Texto);
                return partes.Count > 0 && partes.All(p => palavras.Exists(w => w.StartsWith(p, StringComparison.Ordinal)));
            }).ToList();
            var achados = achou.Count > 0
                ? (pontuacao ? achou.Where(i => commits[i].Texto.Contains(termo, StringComparison.Ordinal)) : achou)
                : Enumerable.Range(0, commits.Count).Where(i => commits[i].Texto.Contains(termo, StringComparison.Ordinal));
            candidatos = candidatos.Intersect(achados).ToList();
        }

        return [.. candidatos.Order()];
    }

    [Fact]
    public void Indice_e_igual_a_busca_por_forca_bruta_em_varias_formas_de_consulta()
    {
        var commits = Sintetico(6000, 1);
        var relogio = Stopwatch.StartNew();
        var indice = new IndiceCommits(commits);
        saida.WriteLine($"índice de 6.000 commits: {relogio.ElapsedMilliseconds} ms, {indice.QuantidadePalavras} palavras");

        string[] consultas =
        [
            "VENDAS-10012", "vendas 10012", "vend", "10012 ajuste", "ajuste pedido", "hotfix", "release-3", "zzzzzz",
            "ndas-1001", "djuste", "wmw.com.br", "ana", "pedido tela login", "VENDAS-1",
        ];
        var divergencias = consultas.Where(q => !indice.Buscar(new FiltroHistorico(q)).SequenceEqual(ForcaBruta(commits, q))).ToList();

        var rnd = new Random(7);
        for (var k = 0; k < 40; k++)
        {
            var hash = commits[rnd.Next(commits.Count)].Hash;
            var q = hash[..(6 + rnd.Next(6))];
            var a = indice.Buscar(new FiltroHistorico(q));
            if (a.Length == 0 || !a.SequenceEqual(ForcaBruta(commits, q))) divergencias.Add("hash " + q);
        }

        divergencias.Should().BeEmpty();
    }

    [Fact]
    public void Numero_de_ticket_nao_casa_com_numeros_maiores_que_comecam_igual()
    {
        var commits = Sintetico(6000, 1);
        var achados = new IndiceCommits(commits).Buscar(new FiltroHistorico("VENDAS-10012"));

        achados.Should().NotBeEmpty();
        achados.Should().OnlyContain(i => commits[i].Assunto.StartsWith("VENDAS-10012 ", StringComparison.Ordinal));
    }

    [Fact]
    public void Sem_filtro_traz_todos_e_termo_inexistente_nenhum()
    {
        var indice = new IndiceCommits(Sintetico(500, 2));

        indice.Buscar(new FiltroHistorico()).Should().HaveCount(500);
        indice.Buscar(new FiltroHistorico("nao-existe-xyz")).Should().BeEmpty();
    }

    [Fact]
    public void Filtra_por_autor_por_intervalo_de_datas_inclusivo_e_combinados()
    {
        var commits = Sintetico(6000, 1);
        var indice = new IndiceCommits(commits);

        indice.Buscar(new FiltroHistorico(Autor: "bruno")).Should().HaveCount(commits.Count(c => c.Autor == "Bruno Lima"));

        DateTime de = new(2026, 9, 20), ate = new(2026, 9, 22);
        var porData = indice.Buscar(new FiltroHistorico(De: de, Ate: ate));
        porData.Should().HaveCount(commits.Count(c => c.Data >= de && c.Data < ate.AddDays(1))).And.NotBeEmpty();

        var combinado = indice.Buscar(new FiltroHistorico("ajuste", "carla", de, ate));
        combinado.Should().Equal(Enumerable.Range(0, commits.Count).Where(i =>
            commits[i].Data >= de && commits[i].Data < ate.AddDays(1) && commits[i].AutorTexto.Contains("carla", StringComparison.Ordinal)
            && IndiceCommits.Palavras(commits[i].Texto).Exists(w => w.StartsWith("ajuste", StringComparison.Ordinal))));
    }

    [Fact]
    public void Mede_150_mil_commits_e_a_consulta_tipica_fica_abaixo_de_20_ms()
    {
        var grande = Sintetico(150_000, 3);
        var relogio = Stopwatch.StartNew();
        var indice = new IndiceCommits(grande);
        var montar = relogio.ElapsedMilliseconds;

        relogio.Restart();
        var achados = 0;
        for (var k = 0; k < 50; k++) achados += indice.Buscar(new FiltroHistorico($"VENDAS-{10000 + (k * 7)} ajuste")).Length;
        var medio = relogio.Elapsed.TotalMilliseconds / 50;

        relogio.Restart();
        indice.Buscar(new FiltroHistorico("ped", "ana", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)));
        var amplo = relogio.Elapsed.TotalMilliseconds;

        saida.WriteLine($"150.000 commits: índice {montar} ms; número+palavra {medio:0.00} ms; busca ampla {amplo:0.0} ms ({achados} achados)");
        medio.Should().BeLessThan(20);
        amplo.Should().BeLessThan(400);
    }
}
