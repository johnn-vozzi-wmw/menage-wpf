using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Menage.Core.Armazenamento;
using Menage.Core.Http;

namespace Menage.Core.Nexus;

public sealed record PedidoDeDownload(IReadOnlyList<ComponenteNexus> Componentes, IReadOnlySet<string> Extensoes, string Destino, bool ManterEstrutura, bool Subpasta);

public sealed record ResumoDoDownload(string Pasta, int Baixados, IReadOnlyList<string> Falhas, long Bytes, bool Cancelado)
{
    public bool Ok => Falhas.Count == 0 && !Cancelado;
}

public sealed record AndamentoDoDownload(long Feito, long Total, int ArquivoAtual, int Arquivos, string Nome);

public static class BaixadorNexus
{
    public static IEnumerable<ArquivoNexus> Arquivos(IEnumerable<ComponenteNexus> componentes, IReadOnlySet<string> extensoes) =>
        componentes.SelectMany(c => c.Arquivos).Where(a => extensoes.Contains(a.Extensao));

    /// <summary>beta_Nexus_9030_9060-5: versões sem pontos, da menor para a maior; muitas viram "primeira_a_ultima_Nversoes".</summary>
    public static string NomeDaPasta(IEnumerable<string> versoes, DateTime? agora = null)
    {
        var v = versoes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().OrderBy(x => x, VersaoNatural.Comparador).ToList();
        static string Limpa(string s) => Path.GetInvalidFileNameChars().Aggregate(s.Replace(".", ""), (t, c) => t.Replace(c, '-'));
        var meio = v.Count == 0 ? (agora ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
            : v.Count <= 5 ? string.Join('_', v.Select(Limpa))
            : $"{Limpa(v[0])}_a_{Limpa(v[^1])}_{v.Count}versoes";
        return "beta_Nexus_" + meio;
    }

    /// <summary>Baixa um a um. Falha de um arquivo não para os outros; cancelar apaga só o parcial.</summary>
    public static async Task<Resultado<ResumoDoDownload>> BaixarAsync(ClienteNexus cliente, PedidoDeDownload pedido, IProgress<AndamentoDoDownload>? andamento, CancellationToken cancelar = default)
    {
        var arquivos = Arquivos(pedido.Componentes, pedido.Extensoes).ToList();
        if (pedido.Extensoes.Count == 0) return Resultado.Falha<ResumoDoDownload>("Nenhum tipo marcado", null, "Marque jar, war ou pom em Baixar.");
        if (arquivos.Count == 0) return Resultado.Falha<ResumoDoDownload>("Nada para baixar", "Os componentes marcados não têm arquivos dos tipos escolhidos.", "Marque outros tipos ou outros componentes.");
        if (string.IsNullOrWhiteSpace(pedido.Destino)) return Resultado.Falha<ResumoDoDownload>("Informe a pasta em Salvar em");

        var pasta = pedido.Subpasta ? Path.Combine(pedido.Destino, NomeDaPasta(pedido.Componentes.Select(c => c.Versao))) : pedido.Destino;
        try { Directory.CreateDirectory(pasta); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Resultado.Falha<ResumoDoDownload>("Pasta de destino inválida", ex.Message, "Escolha outra pasta.");
        }

        var total = arquivos.Sum(a => a.Tamanho);
        long feito = 0;
        var baixados = 0;
        var falhas = new List<string>();
        for (var i = 0; i < arquivos.Count; i++)
        {
            var arquivo = arquivos[i];
            var indice = i;
            var r = await cliente.BaixarAsync(arquivo, pasta, pedido.ManterEstrutura,
                n => andamento?.Report(new AndamentoDoDownload(Interlocked.Add(ref feito, n), total, indice + 1, arquivos.Count, Path.GetFileName(arquivo.Caminho))), cancelar);
            if (r.EhOk) baixados++;
            else if (r.Erro == Erro.Cancelado) return Resultado.Ok(new ResumoDoDownload(pasta, baixados, falhas, feito, Cancelado: true));
            else falhas.Add($"{Path.GetFileName(arquivo.Caminho)}: {r.Erro.Causa}");
        }

        return Resultado.Ok(new ResumoDoDownload(pasta, baixados, falhas, feito, false));
    }
}

public static class NexusExtensions
{
    /// <summary>Motor do Nexus. O HTTP e o protetor de segredo são trocáveis (testes registram antes; <c>TryAdd</c>).</summary>
    public static IServiceCollection AddNexus(this IServiceCollection servicos)
    {
        servicos.TryAddSingleton<IClienteHttp, ClienteHttp>();
        servicos.TryAddSingleton<IProtetorDeSegredo>(_ => OperatingSystem.IsWindows() ? new ProtetorDpapiTolerante() : throw new PlatformNotSupportedException());
        servicos.AddSingleton<IArmazenamento<ConfiguracaoDoNexus>>(sp =>
            new Armazenamento<ConfiguracaoDoNexus>(sp.GetRequiredService<PastaDeDados>(), ConfiguracaoDoNexus.NomeDoArquivo, padrao: ConfiguracaoDoNexus.Padrao));
        return servicos.AddSingleton<RepositorioDoNexus>();
    }
}
