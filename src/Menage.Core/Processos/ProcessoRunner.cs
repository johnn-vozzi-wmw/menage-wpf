using System.ComponentModel;
using System.Diagnostics;

namespace Menage.Core.Processos;

public sealed class ProcessoRunner : IProcessoRunner
{
    public async Task<SaidaProcesso> ExecutarAsync(ComandoProcesso comando, CancellationToken cancelar = default)
    {
        using var processo = new Process { StartInfo = Montar(comando) };
        var relogio = Stopwatch.StartNew();
        try
        {
            processo.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or DirectoryNotFoundException)
        {
            return SaidaProcesso.NaoIniciou($"Não foi possível iniciar '{comando.Arquivo}': {ex.Message}");
        }

        var saida = LerTudoAsync(processo.StandardOutput.BaseStream);
        var erro = LerTudoAsync(processo.StandardError.BaseStream);
        await EscreverEntradaAsync(processo, comando.Entrada);

        using var limite = new CancellationTokenSource();
        if (comando.Timeout is { } t) limite.CancelAfter(t);
        using var junto = CancellationTokenSource.CreateLinkedTokenSource(cancelar, limite.Token);
        try
        {
            await processo.WaitForExitAsync(junto.Token);
        }
        catch (OperationCanceledException)
        {
            Encerrar(processo);
            await processo.WaitForExitAsync(CancellationToken.None);
        }

        var cancelado = cancelar.IsCancellationRequested;
        var expirou = !cancelado && limite.IsCancellationRequested;
        return new SaidaProcesso(
            processo.ExitCode, await saida, await erro, relogio.Elapsed, expirou, cancelado);
    }

    static ProcessStartInfo Montar(ComandoProcesso c)
    {
        var info = new ProcessStartInfo(c.Arquivo)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = c.Entrada is not null,
            WorkingDirectory = c.PastaDeTrabalho ?? string.Empty,
        };
        foreach (var argumento in c.Argumentos) info.ArgumentList.Add(argumento);
        foreach (var (chave, valor) in c.Ambiente ?? new Dictionary<string, string?>())
        {
            if (valor is null) info.Environment.Remove(chave);
            else info.Environment[chave] = valor;
        }

        return info;
    }

    static async Task EscreverEntradaAsync(Process processo, byte[]? entrada)
    {
        if (entrada is null) return;
        try
        {
            await processo.StandardInput.BaseStream.WriteAsync(entrada);
            processo.StandardInput.Close();
        }
        catch (IOException)
        {
            // o processo pode fechar a entrada antes de ler tudo; o código de saída diz o resto
        }
    }

    static void Encerrar(Process processo)
    {
        try { processo.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* já terminou */ }
    }

    static async Task<byte[]> LerTudoAsync(Stream fluxo)
    {
        using var memoria = new MemoryStream();
        await fluxo.CopyToAsync(memoria);
        return memoria.ToArray();
    }
}
