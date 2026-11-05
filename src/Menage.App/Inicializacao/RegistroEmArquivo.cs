using System.IO;
using Menage.Core.Inicializacao;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Menage.App.Inicializacao;

/// <summary>Formatador de texto que passa cada linha pelo <see cref="RedatorDeSegredos"/> antes de gravar.</summary>
public sealed class FormatadorSemSegredos(string modelo) : ITextFormatter
{
    readonly MessageTemplateTextFormatter _base = new(modelo);

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        _base.Format(logEvent, buffer);
        output.Write(RedatorDeSegredos.Limpar(buffer.ToString()));
    }
}

/// <summary>Log em <c>&lt;dados&gt;\logs\menage-AAAAMMDD.log</c>: um arquivo por dia, 7 dias, sem segredos.</summary>
public sealed class RegistroEmArquivo : IDisposable
{
    public const string Modelo = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
    readonly Serilog.Core.Logger _logger;

    public static RegistroEmArquivo Abrir(string pastaDeDados) => new(pastaDeDados);

    RegistroEmArquivo(string pastaDeDados)
    {
        Pasta = Path.Combine(pastaDeDados, "logs");
        _logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(new FormatadorSemSegredos(Modelo), Path.Combine(Pasta, "menage-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true,
                restrictedToMinimumLevel: LogEventLevel.Information)
            .CreateLogger();
        Fabrica = new SerilogLoggerFactory(_logger, dispose: false);
    }

    public string Pasta { get; }

    public ILoggerFactory Fabrica { get; }

    public void Dispose()
    {
        Fabrica.Dispose();
        _logger.Dispose();
    }
}
