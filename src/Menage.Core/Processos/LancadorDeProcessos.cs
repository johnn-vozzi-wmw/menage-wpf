using System.Diagnostics;
using System.Text;

namespace Menage.Core.Processos;

/// <summary>Processo de longa duração (ex.: Tomcat) com a saída chegando linha a linha.</summary>
public interface IProcessoLongo
{
    int Id { get; }

    long InicioTicks { get; }

    bool Vivo { get; }

    /// <summary>Encerra o processo e todos os filhos.</summary>
    Task EncerrarArvoreAsync();
}

/// <summary>
/// Ao lado do <see cref="IProcessoRunner"/>, que só serve para comandos que terminam: este inicia processos que ficam rodando
/// e entrega a saída bruta (Latin1, um caractere por byte) para quem decide a codificação de cada linha.
/// </summary>
public interface ILancadorDeProcessos
{
    Resultado<IProcessoLongo> Iniciar(ComandoProcesso comando, Action<string> aoReceberLinha, Action<int> aoSair);

    /// <summary>Reencontra um processo que um uso anterior subiu (mesmo PID e mesma hora de início).</summary>
    IProcessoLongo? Reencontrar(int pid, long inicioTicks);
}

public sealed class LancadorDeProcessos : ILancadorDeProcessos
{
    public Resultado<IProcessoLongo> Iniciar(ComandoProcesso comando, Action<string> aoReceberLinha, Action<int> aoSair)
    {
        var psi = new ProcessStartInfo(comando.Arquivo)
        {
            WorkingDirectory = comando.PastaDeTrabalho ?? "",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Latin1,
            StandardErrorEncoding = Encoding.Latin1,
        };
        foreach (var a in comando.Argumentos) psi.ArgumentList.Add(a);
        if (comando.Ambiente is not null)
            foreach (var (chave, valor) in comando.Ambiente)
            {
                if (valor is null) psi.Environment.Remove(chave);
                else psi.Environment[chave] = valor;
            }

        var processo = new Process { StartInfo = psi, EnableRaisingEvents = true };
        processo.OutputDataReceived += (_, e) => { if (e.Data is not null) aoReceberLinha(e.Data); };
        processo.ErrorDataReceived += (_, e) => { if (e.Data is not null) aoReceberLinha(e.Data); };
        processo.Exited += (_, _) =>
        {
            var codigo = -1;
            try { codigo = processo.ExitCode; }
            catch (InvalidOperationException) { /* processo já descartado */ }
            aoSair(codigo);
        };
        try
        {
            processo.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Resultado.Falha<IProcessoLongo>("Não consegui iniciar o processo", ex.Message, "Confira se a pasta está acessível e se o programa existe.");
        }

        processo.BeginOutputReadLine();
        processo.BeginErrorReadLine();
        return Resultado.Ok<IProcessoLongo>(new ProcessoDoWindows(processo));
    }

    public IProcessoLongo? Reencontrar(int pid, long inicioTicks)
    {
        if (pid <= 0) return null;
        try
        {
            var p = Process.GetProcessById(pid);
            return !p.HasExited && Math.Abs(p.StartTime.Ticks - inicioTicks) < TimeSpan.TicksPerSecond * 2 ? new ProcessoDoWindows(p) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    sealed class ProcessoDoWindows(Process processo) : IProcessoLongo
    {
        public int Id { get; } = processo.Id;

        public long InicioTicks { get; } = Inicio(processo);

        public bool Vivo
        {
            get
            {
                try { return !processo.HasExited; }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
            }
        }

        public async Task EncerrarArvoreAsync()
        {
            try
            {
                using var kill = Process.Start(new ProcessStartInfo("taskkill.exe") { ArgumentList = { "/PID", Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "/T", "/F" }, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                if (kill is not null) await kill.WaitForExitAsync();
                await processo.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                // já terminou ou não deixou encerrar: quem chamou confere Vivo
            }
        }

        static long Inicio(Process p)
        {
            try { return p.StartTime.Ticks; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return 0; }
        }
    }
}
