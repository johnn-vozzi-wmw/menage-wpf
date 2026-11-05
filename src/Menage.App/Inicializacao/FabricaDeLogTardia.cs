using Microsoft.Extensions.Logging;

namespace Menage.App.Inicializacao;

/// <summary>
/// Entrega loggers na hora, mas só toca o Serilog (carregar assemblies, criar o arquivo) quando a primeira linha for escrita:
/// o <see cref="RegistroEmArquivo"/> nasce numa tarefa em segundo plano e não pesa na abertura da janela.
/// </summary>
public sealed class FabricaDeLogTardia(Task<RegistroEmArquivo> registro) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new LoggerTardio(registro, categoryName);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    sealed class LoggerTardio(Task<RegistroEmArquivo> registro, string categoria) : ILogger
    {
        readonly Lazy<ILogger> _real = new(() => registro.GetAwaiter().GetResult().Fabrica.CreateLogger(categoria));

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => _real.Value.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _real.Value.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _real.Value.Log(logLevel, eventId, state, exception, formatter);
    }
}
