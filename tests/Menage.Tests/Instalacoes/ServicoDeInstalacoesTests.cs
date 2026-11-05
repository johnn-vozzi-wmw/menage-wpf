using Menage.Core;
using Menage.Core.Atualizacao;

namespace Menage.Tests.Instalacoes;

/// <summary>Feed e gerenciador falsos: nada de Velopack, rede ou instalação real.</summary>
sealed class GerenteFalso(string? atual, bool instalado, params string[] versoes) : IGerenteDeInstalacao
{
    public bool Instalado { get; } = instalado;

    public string? VersaoAtual { get; } = atual;

    public List<string> Baixadas { get; } = [];

    public string? Aplicada { get; private set; }

    public Exception? Falha { get; set; }

    public Task<IReadOnlyList<LancamentoDaVersao>> ListarAsync(CancellationToken ct = default)
    {
        if (Falha is not null) throw Falha;
        return Task.FromResult<IReadOnlyList<LancamentoDaVersao>>(versoes.Select(v => new LancamentoDaVersao(v, $"Notas {v}", 1000)).ToList());
    }

    public Task BaixarAsync(string versao, IProgress<int> progresso, CancellationToken ct = default)
    {
        progresso.Report(100);
        Baixadas.Add(versao);
        return Task.CompletedTask;
    }

    public void AplicarEReiniciar(string versao) => Aplicada = versao;
}

sealed class FabricaFalsa(Func<string?, string?, IGerenteDeInstalacao?> cria) : IFabricaDeGerente
{
    public List<(string? Origem, string? Canal)> Chamadas { get; } = [];

    public IGerenteDeInstalacao? Criar(string? origem, string? canal)
    {
        Chamadas.Add((origem, canal));
        return cria(origem, canal);
    }
}

sealed class SemProgresso : IProgress<int>
{
    public void Report(int value) { }
}

public sealed class ServicoDeInstalacoesTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "mgi" + Guid.NewGuid().ToString("N")[..6]);
    string Arquivo => Path.Combine(_pasta, "atualizacao.json");

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    (ServicoDeInstalacoes Servico, GerenteFalso Gerente, FabricaFalsa Fabrica) Criar(string atual, string? origem = "C:\\feed", bool instalado = true, params string[] feed)
    {
        var gerente = new GerenteFalso(atual, instalado, feed);
        var fabrica = new FabricaFalsa((o, _) => string.IsNullOrWhiteSpace(o) ? null : gerente);
        return (new ServicoDeInstalacoes(fabrica, new OpcoesDeAtualizacao(origem, null), Arquivo, atual), gerente, fabrica);
    }

    [Fact]
    public async Task Sem_origem_nao_verifica_nada()
    {
        var (servico, _, _) = Criar("1.0.0", origem: null);
        Assert.Equal(EstadoDaVerificacao.SemOrigem, (await servico.VerificarAsync()).Estado);
    }

    [Fact]
    public async Task Fora_do_instalador_informa_sem_consultar_o_feed()
    {
        var (servico, gerente, _) = Criar("1.0.0", instalado: false, feed: ["1.0.1"]);
        gerente.Falha = new InvalidOperationException("não deveria ler");
        Assert.Equal(EstadoDaVerificacao.ForaDoInstalador, (await servico.VerificarAsync()).Estado);
    }

    [Fact]
    public async Task Detecta_versao_nova_com_notas()
    {
        var (servico, _, _) = Criar("1.0.0", feed: ["1.0.0", "1.1.0", "1.0.5"]);
        var r = await servico.VerificarAsync();
        Assert.Equal(EstadoDaVerificacao.NovaVersao, r.Estado);
        Assert.Equal("1.1.0", r.Versao);
        Assert.Equal("Notas 1.1.0", r.Notas);
    }

    [Fact]
    public async Task Feed_na_mesma_versao_esta_em_dia()
    {
        var (servico, _, _) = Criar("1.1.0", feed: ["1.0.0", "1.1.0"]);
        Assert.Equal(EstadoDaVerificacao.EmDia, (await servico.VerificarAsync()).Estado);
    }

    [Fact]
    public async Task Falha_do_feed_vira_estado_Falhou_sem_excecao()
    {
        var (servico, gerente, _) = Criar("1.0.0", feed: ["1.0.1"]);
        gerente.Falha = new IOException("rede fora");
        var r = await servico.VerificarAsync();
        Assert.Equal(EstadoDaVerificacao.Falhou, r.Estado);
        Assert.Contains("rede fora", r.Mensagem);
    }

    [Fact]
    public async Task Lista_ordena_da_mais_nova_e_marca_atual()
    {
        var (servico, _, _) = Criar("1.0.5", feed: ["1.0.0", "1.1.0", "1.0.5", "1.1.0-beta.2"]);
        var lista = (await servico.ListarAsync()).Valor;
        Assert.Equal(["1.1.0", "1.1.0-beta.2", "1.0.5", "1.0.0"], lista.Select(v => v.Versao));
        Assert.Equal("1.0.5", Assert.Single(lista, v => v.EhAtual).Versao);
        Assert.True(lista[0].EhMaisNova);
    }

    [Fact]
    public async Task Versao_anterior_e_a_maior_menor_que_a_atual()
    {
        var (servico, _, _) = Criar("1.1.0", feed: ["1.0.0", "1.0.5", "1.1.0", "1.2.0"]);
        var lista = (await servico.ListarAsync()).Valor;
        Assert.Equal("1.0.5", servico.VersaoAnterior(lista));
    }

    [Fact]
    public async Task Sem_versao_menor_nao_ha_anterior()
    {
        var (servico, _, _) = Criar("1.0.0", feed: ["1.0.0", "1.1.0"]);
        Assert.Null(servico.VersaoAnterior((await servico.ListarAsync()).Valor));
    }

    [Fact]
    public async Task Instalar_baixa_e_reinicia_na_versao_pedida()
    {
        var (servico, gerente, _) = Criar("1.1.0", feed: ["1.0.5", "1.1.0"]);
        var r = await servico.InstalarAsync("1.0.5", new SemProgresso());
        Assert.True(r.EhOk);
        Assert.Equal(["1.0.5"], gerente.Baixadas);
        Assert.Equal("1.0.5", gerente.Aplicada);
    }

    [Fact]
    public async Task Instalar_sem_reiniciar_so_baixa()
    {
        var (servico, gerente, _) = Criar("1.0.0", feed: ["1.0.0", "1.0.1"]);
        Assert.True((await servico.InstalarAsync("1.0.1", new SemProgresso(), reiniciar: false)).EhOk);
        Assert.Null(gerente.Aplicada);
    }

    [Fact]
    public async Task Instalar_versao_que_saiu_do_feed_falha_sem_baixar()
    {
        var (servico, gerente, _) = Criar("1.1.0", feed: ["1.1.0"]);
        var r = await servico.InstalarAsync("0.9.0", new SemProgresso());
        Assert.False(r.EhOk);
        Assert.Empty(gerente.Baixadas);
    }

    [Fact]
    public async Task Instalar_fora_do_instalador_e_recusado()
    {
        var (servico, gerente, _) = Criar("1.0.0", instalado: false, feed: ["1.0.1"]);
        Assert.False((await servico.InstalarAsync("1.0.1", new SemProgresso())).EhOk);
        Assert.Empty(gerente.Baixadas);
    }

    [Fact]
    public void Definir_canal_e_origem_grava_e_recria_o_gerente()
    {
        var (servico, _, fabrica) = Criar("1.0.0", feed: ["1.0.0"]);
        servico.DefinirCanal(CanalDeAtualizacao.Beta);
        servico.DefinirOrigem("  \\\\srv\\menage  ");
        Assert.Equal((@"\\srv\menage", "beta"), fabrica.Chamadas[^1]);
        var lida = OpcoesDeAtualizacao.Carregar(Arquivo);
        Assert.Equal("beta", lida.Canal);
        Assert.Equal(@"\\srv\menage", lida.Origem);
        Assert.Equal(CanalDeAtualizacao.Beta, servico.Canal);
    }

    [Fact]
    public void Voltar_ao_canal_estavel_limpa_o_canal()
    {
        var (servico, _, fabrica) = Criar("1.0.0", feed: ["1.0.0"]);
        servico.DefinirCanal(CanalDeAtualizacao.Beta);
        servico.DefinirCanal(CanalDeAtualizacao.Estavel);
        Assert.Null(OpcoesDeAtualizacao.Carregar(Arquivo).Canal);
        Assert.Null(fabrica.Chamadas[^1].Canal);
    }

    [Fact]
    public void Opcoes_antigas_sem_canal_continuam_validas()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Arquivo, """{ "Origem": "C:\\x", "VersaoIgnorada": "1.0.1" }""");
        var lida = OpcoesDeAtualizacao.Carregar(Arquivo);
        Assert.Equal("1.0.1", lida.VersaoIgnorada);
        Assert.Null(lida.Canal);
    }
}

public sealed class VersaoSemanticaTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.0-beta.1", "1.0.0", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", -1)]
    [InlineData("v2.0.0", "1.9.9", 1)]
    public void Compara(string a, string b, int esperado) =>
        Assert.Equal(esperado, Math.Sign(VersaoSemantica.Comparador.Compare(a, b)));
}

public sealed class DetectorDoAppAntigoTests : IDisposable
{
    readonly string _raiz = Path.Combine(Path.GetTempPath(), "mgd" + Guid.NewGuid().ToString("N")[..6]);

    public void Dispose()
    {
        if (Directory.Exists(_raiz)) Directory.Delete(_raiz, true);
    }

    [Fact]
    public void Lista_pastas_GMV_sem_alterar_nada()
    {
        Directory.CreateDirectory(Path.Combine(_raiz, "GMV10"));
        Directory.CreateDirectory(Path.Combine(_raiz, "GMV9"));
        Directory.CreateDirectory(Path.Combine(_raiz, "outra"));
        File.WriteAllText(Path.Combine(_raiz, "GMV10", "GitManager.exe"), "x");

        var achadas = new DetectorDoAppAntigo(_raiz).Detectar();

        Assert.Equal(["GMV10", "GMV9"], achadas.Select(a => Path.GetFileName(a.Pasta)));
        Assert.Equal("GitManager.exe", achadas[0].Executavel);
        Assert.Equal(2, Directory.GetDirectories(_raiz, "GMV*").Length + 0);
        Assert.True(File.Exists(Path.Combine(_raiz, "GMV10", "GitManager.exe")));
    }

    [Fact]
    public void Pasta_inexistente_nao_acha_nada() =>
        Assert.Empty(new DetectorDoAppAntigo(Path.Combine(_raiz, "nao-existe")).Detectar());
}
