using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

#pragma warning disable SYSLIB1054, CA5392, CA1838 // Restart Manager: DllImport simples basta (só leitura, uso raro)

namespace Menage.Core.Melhorias;

/// <summary>Descobre quais programas estão com um arquivo aberto (Restart Manager do Windows). Só lê; nunca fecha nada.</summary>
public static class ArquivoEmUso
{
    const int ErroMaisDados = 234;

    /// <summary>Nomes dos programas que estão com o arquivo aberto (ex.: "DBeaver (dbeaver.exe)"); vazio se não der para saber.</summary>
    public static IReadOnlyList<string> Quem(string arquivo)
    {
        var nomes = new List<string>();
        if (!OperatingSystem.IsWindows() || !File.Exists(arquivo)) return nomes;
        if (RmStartSession(out var sessao, 0, new StringBuilder(33)) != 0) return nomes;
        try
        {
            if (RmRegisterResources(sessao, 1, [arquivo], 0, null, 0, null) != 0) return nomes;
            uint quantos = 0;
            if (RmGetList(sessao, out var necessarios, ref quantos, null, out _) != ErroMaisDados || necessarios == 0) return nomes;
            var info = new ProcessoRm[necessarios];
            quantos = necessarios;
            if (RmGetList(sessao, out _, ref quantos, info, out _) != 0) return nomes;
            for (var i = 0; i < quantos; i++)
            {
                var t = Descrever(info[i]);
                if (t.Length > 0 && !nomes.Contains(t)) nomes.Add(t);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            // sem Restart Manager: a mensagem só não cita o programa
        }
        finally
        {
            _ = RmEndSession(sessao);
        }

        return nomes;
    }

    /// <summary>Texto "DBeaver (dbeaver.exe)" ou vazio.</summary>
    public static string Resumo(IReadOnlyList<string> nomes) => string.Join(", ", nomes);

    static string Descrever(ProcessoRm p)
    {
        var exe = "";
        try { exe = Process.GetProcessById(p.Processo.IdDoProcesso).ProcessName + ".exe"; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { /* o processo já saiu */ }
        var app = (p.NomeDoApp ?? "").Trim();
        return app.Length > 0 && exe.Length > 0 && !app.Equals(exe, StringComparison.OrdinalIgnoreCase) ? $"{app} ({exe})" : app.Length > 0 ? app : exe;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessoUnico
    {
        public int IdDoProcesso;
        public System.Runtime.InteropServices.ComTypes.FILETIME Inicio;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessoRm
    {
        public ProcessoUnico Processo;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string NomeDoApp;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string NomeDoServico;
        public int Tipo;
        public uint Status;
        public uint Sessao;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Reiniciavel;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmStartSession(out uint sessao, int flags, StringBuilder chave);

    [DllImport("rstrtmgr.dll")]
    static extern int RmEndSession(uint sessao);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmRegisterResources(uint sessao, uint nArquivos, string[] arquivos, uint nApps, ProcessoUnico[]? apps, uint nServicos, string[]? servicos);

    [DllImport("rstrtmgr.dll")]
    static extern int RmGetList(uint sessao, out uint necessarios, ref uint quantos, [In, Out] ProcessoRm[]? info, out uint motivos);
}
