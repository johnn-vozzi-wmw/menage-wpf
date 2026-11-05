using System.IO;
using Menage.App.Inicializacao;
using Microsoft.Extensions.Logging;

namespace Menage.App.Tests;

public sealed class InicializacaoTests
{
    [Fact]
    public void Segunda_instancia_na_mesma_pasta_nao_e_a_primeira_e_aciona_o_sinal()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "mg-inst-" + Guid.NewGuid().ToString("N")[..6]);
        var sufixo = "_teste" + Guid.NewGuid().ToString("N")[..6];
        using var primeira = InstanciaUnica.Adquirir(pasta, sufixo);
        using var pediu = new ManualResetEventSlim();
        primeira.AoPedirJanela = pediu.Set;
        primeira.IniciarEscuta();

        using var segunda = InstanciaUnica.Adquirir(pasta, sufixo);
        segunda.PedirParaMostrar();

        primeira.EhPrimeira.Should().BeTrue();
        segunda.EhPrimeira.Should().BeFalse();
        pediu.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue("a primeira instância tem de receber o pedido para mostrar a janela");
    }

    [Fact]
    public void Pastas_de_dados_diferentes_convivem()
    {
        var sufixo = "_teste" + Guid.NewGuid().ToString("N")[..6];
        using var a = InstanciaUnica.Adquirir(Path.Combine(Path.GetTempPath(), "mg-a"), sufixo);
        using var b = InstanciaUnica.Adquirir(Path.Combine(Path.GetTempPath(), "mg-b"), sufixo);
        (a.EhPrimeira && b.EhPrimeira).Should().BeTrue();
    }

    sealed class LogGravador : ILogger
    {
        public List<(LogLevel Nivel, Exception? Erro)> Linhas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Linhas.Add((logLevel, exception));
    }

    [Fact]
    public void Excecao_nao_tratada_vai_para_o_log_e_o_usuario_ve_uma_mensagem_com_o_caminho()
    {
        var log = new LogGravador();
        string? mostrada = null;
        var tratador = new TratadorGlobalDeErros(log, @"C:\x\logs", m => mostrada = m);
        var erro = new InvalidOperationException("quebrou");

        tratador.Tratar(erro, "tela").Should().BeTrue();

        log.Linhas.Should().ContainSingle().Which.Should().Be((LogLevel.Error, erro));
        mostrada.Should().Contain("quebrou").And.Contain(@"C:\x\logs");
    }

    [Fact]
    public void Log_em_arquivo_nao_grava_segredos()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "mg-log-" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            using (var registro = RegistroEmArquivo.Abrir(pasta))
            {
                var log = registro.Fabrica.CreateLogger("Teste");
                log.LogInformation("clone https://ana:S3NHA-secreta@github.com/x.git token={Token}", "ghp_abcdefghijklmnopqrstuvwxyz0123456789");
                log.LogError(new InvalidOperationException("Authorization: Bearer SEGREDO-XYZ"), "falhou");
            }

            var texto = string.Join('\n', Directory.GetFiles(Path.Combine(pasta, "logs"), "menage-*.log").Select(File.ReadAllText));
            texto.Should().Contain("clone").And.Contain("falhou");
            texto.Should().NotContain("S3NHA-secreta").And.NotContain("ghp_abcdef").And.NotContain("SEGREDO-XYZ");
        }
        finally
        {
            if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true);
        }
    }
}
