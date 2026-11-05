using Microsoft.Win32;

namespace Menage.Core.Diff;

/// <summary>Onde estão e como chamar o TortoiseGit e o WinMerge (as mesmas regras do Git Manager), para quem não usa o diff interno.</summary>
public static class FerramentasExternasDeDiff
{
    static readonly string[] PastasDeProgramas = ["ProgramW6432", "ProgramFiles", "ProgramFiles(x86)"];

    /// <summary>TortoiseGitProc.exe na pasta configurada ou nos lugares de costume; nulo se não achar.</summary>
    public static string? TortoiseProc(string pastaBinConfigurada)
    {
        var candidatas = new List<string>();
        if (pastaBinConfigurada.Trim().Length > 0) candidatas.Add(pastaBinConfigurada.Trim());
        candidatas.AddRange(PastasDeProgramas.Select(Environment.GetEnvironmentVariable).Where(p => !string.IsNullOrEmpty(p)).Select(p => Path.Combine(p!, "TortoiseGit", "bin")));
        return candidatas.Select(p => Path.Combine(p, "TortoiseGitProc.exe")).FirstOrDefault(File.Exists);
    }

    /// <summary>WinMergeU.exe no caminho configurado, nas pastas padrão do instalador ou no registro; nulo se não achar.</summary>
    public static string? WinMergeExe(string caminhoConfigurado = "")
    {
        if (caminhoConfigurado.Trim().Length > 0 && File.Exists(caminhoConfigurado.Trim())) return caminhoConfigurado.Trim();
        var pastas = PastasDeProgramas.Select(Environment.GetEnvironmentVariable)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
        foreach (var pasta in pastas.Where(p => !string.IsNullOrEmpty(p)))
            if (Path.Combine(pasta!, "WinMerge", "WinMergeU.exe") is var exe && File.Exists(exe)) return exe;

        return OperatingSystem.IsWindows() ? NoRegistro(Registry.CurrentUser) ?? NoRegistro(Registry.LocalMachine) : null;
    }

    /// <summary>TortoiseGit: a janela "Verificar alterações" do projeto (lista também os arquivos novos).</summary>
    public static string ArgumentosDoTortoise(string repo) => $"/command:repostatus /path:\"{repo}\"";

    /// <summary>
    /// Valor de <c>difftool.menage-winmerge.cmd</c> (usado com <c>git difftool --dir-diff --tool=menage-winmerge HEAD</c>): compara o HEAD (staged e não staged), comparação de pastas recursiva; a esquerda
    /// (HEAD) é só leitura e o que for editado na direita volta para os arquivos do projeto quando o WinMerge fecha.
    /// </summary>
    public static string ComandoDoWinMerge(string exe)
    {
        var caminho = exe.Replace('\\', '/');
        return $"\"{caminho}\" -e -u -r -wl -dl \"HEAD (commit atual)\" -dr \"Seus arquivos\" \"$LOCAL\" \"$REMOTE\"";
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static string? NoRegistro(RegistryKey raiz)
    {
        try
        {
            using var chave = raiz.OpenSubKey(@"Software\Thingamahoochie\WinMerge");
            return chave?.GetValue("Executable") is string exe && File.Exists(exe) ? exe : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
