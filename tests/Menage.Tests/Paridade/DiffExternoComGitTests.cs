using Menage.Core.Diff;
using Menage.Tests.NucleoGit;

namespace Menage.Tests.Paridade;

/// <summary>Prova que o comando montado para o WinMerge funciona no git de verdade, com um "WinMerge" falso que só anota o que recebeu.</summary>
public sealed class DiffExternoComGitTests : NucleoGitBase
{
    [Fact]
    public async Task Difftool_chama_o_programa_com_as_duas_pastas_e_o_arquivo_alterado_aparece_na_direita()
    {
        var anotacao = Path.Combine(Repo.Raiz, "chamada.txt");
        var falso = Path.Combine(Repo.Raiz, "falso.cmd");
        File.WriteAllText(falso, "@echo off\r\necho %* > \"" + anotacao + "\"\r\n");
        Escrever("README.md", "alterado\n");

        var comando = Git.Em(Repo.Caminho).Config("difftool.menage-winmerge.cmd", FerramentasExternasDeDiff.ComandoDoWinMerge(falso))
            .Comando("difftool").Opcao("--dir-diff").Opcao("-y").Opcao("--tool=menage-winmerge").Argumento("HEAD");
        var saida = await GitExec.ExecutarBrutoAsync(comando);

        saida.Sucesso.Should().BeTrue(saida.TextoErro);
        File.Exists(anotacao).Should().BeTrue("o difftool deve ter chamado o programa");
        var chamada = File.ReadAllText(anotacao);
        chamada.Should().Contain("HEAD (commit atual)").And.Contain("Seus arquivos");
    }
}
