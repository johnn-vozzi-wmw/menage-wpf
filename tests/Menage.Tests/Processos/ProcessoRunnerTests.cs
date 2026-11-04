namespace Menage.Tests.Processos;

public class ProcessoRunnerTests
{
    readonly ProcessoRunner _runner = new();

    static ComandoProcesso Cmd(string script, TimeSpan? timeout = null, IReadOnlyDictionary<string, string?>? ambiente = null, byte[]? entrada = null) =>
        new("cmd.exe", ["/c", script], null, ambiente, timeout, entrada);

    [Fact]
    public async Task Captura_saida_erro_e_codigo()
    {
        var saida = await _runner.ExecutarAsync(Cmd("echo ola& echo falha 1>&2& exit /b 3"));

        saida.CodigoDeSaida.Should().Be(3);
        saida.Texto.Trim().Should().Be("ola");
        saida.TextoErro.Trim().Should().Be("falha");
        saida.Sucesso.Should().BeFalse();
    }

    [Fact]
    public async Task Devolve_bytes_sem_alterar()
    {
        var saida = await _runner.ExecutarAsync(Cmd("echo abc"));

        saida.Saida.Should().Equal("abc\r\n"u8.ToArray());
    }

    [Fact]
    public async Task Timeout_mata_o_processo_e_marca_expirou()
    {
        var saida = await _runner.ExecutarAsync(Cmd("ping -n 30 127.0.0.1 >nul", TimeSpan.FromMilliseconds(300)));

        saida.Expirou.Should().BeTrue();
        saida.Cancelado.Should().BeFalse();
        saida.Sucesso.Should().BeFalse();
        saida.Duracao.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Cancelamento_mata_o_processo_e_marca_cancelado()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var saida = await _runner.ExecutarAsync(Cmd("ping -n 30 127.0.0.1 >nul"), cts.Token);

        saida.Cancelado.Should().BeTrue();
        saida.Expirou.Should().BeFalse();
        saida.Duracao.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Ambiente_define_e_remove_variaveis()
    {
        Environment.SetEnvironmentVariable("MENAGE_TESTE_REMOVER", "x");
        var ambiente = new Dictionary<string, string?> { ["MENAGE_TESTE_DEFINIR"] = "valor", ["MENAGE_TESTE_REMOVER"] = null };

        var saida = await _runner.ExecutarAsync(Cmd("echo [%MENAGE_TESTE_DEFINIR%][%MENAGE_TESTE_REMOVER%]", ambiente: ambiente));

        saida.Texto.Trim().Should().Be("[valor][%MENAGE_TESTE_REMOVER%]");
    }

    [Fact]
    public async Task Envia_entrada_padrao()
    {
        var saida = await _runner.ExecutarAsync(Cmd("findstr .", entrada: "linha de entrada\r\n"u8.ToArray()));

        saida.Texto.Trim().Should().Be("linha de entrada");
    }

    [Fact]
    public async Task Programa_inexistente_nao_lanca_e_explica()
    {
        var saida = await _runner.ExecutarAsync(new ComandoProcesso("programa-que-nao-existe-xyz", []));

        saida.FalhaAoIniciar.Should().Contain("programa-que-nao-existe-xyz");
        saida.Sucesso.Should().BeFalse();
    }

    [Fact]
    public async Task Argumentos_com_espacos_e_aspas_chegam_inteiros()
    {
        var saida = await _runner.ExecutarAsync(new ComandoProcesso("cmd.exe", ["/c", "echo", "a b", "c"]));

        saida.Texto.Trim().Should().Be("\"a b\" c", "o argumento com espaço chega como um só (o cmd imprime as aspas)");
    }
}
