using Menage.Core.Diff;

namespace Menage.Tests.Paridade;

public sealed class FerramentasExternasDeDiffTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "mgd" + Guid.NewGuid().ToString("N")[..6]);

    public FerramentasExternasDeDiffTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, true);

    [Fact]
    public void TortoiseProc_usa_a_pasta_configurada_quando_o_exe_existe()
    {
        File.WriteAllText(Path.Combine(_pasta, "TortoiseGitProc.exe"), "");

        FerramentasExternasDeDiff.TortoiseProc(_pasta).Should().Be(Path.Combine(_pasta, "TortoiseGitProc.exe"));
    }

    [Fact]
    public void WinMerge_configurado_inexistente_nao_e_aceito_nem_quebra()
    {
        var existente = Path.Combine(_pasta, "WinMergeU.exe");
        File.WriteAllText(existente, "");

        FerramentasExternasDeDiff.WinMergeExe(existente).Should().Be(existente);
        FerramentasExternasDeDiff.WinMergeExe(Path.Combine(_pasta, "nao-existe.exe")).Should().NotBe(Path.Combine(_pasta, "nao-existe.exe"));
    }

    [Fact]
    public void Argumentos_seguem_o_formato_do_git_manager()
    {
        FerramentasExternasDeDiff.ArgumentosDoTortoise(@"C:\x\repo").Should().Be("/command:repostatus /path:\"" + @"C:\x\repo" + "\"");
        FerramentasExternasDeDiff.ComandoDoWinMerge(@"C:\Arquivos\WinMergeU.exe")
            .Should().StartWith("\"C:/Arquivos/WinMergeU.exe\" -e -u -r").And.Contain("\"$LOCAL\" \"$REMOTE\"");
    }
}
