namespace Menage.Core.Repos;

/// <summary>O que a barra de status mostra do ambiente: versão do git e como o SSH foi resolvido.</summary>
public sealed class InformacoesDoAmbiente(IGit git, IAmbienteSsh ssh)
{
    public async Task<string> VersaoDoGitAsync(CancellationToken cancelar = default)
    {
        var saida = await git.ExecutarBrutoAsync(Git.Em(Environment.CurrentDirectory).Opcao("--version"), cancelar);
        var linha = saida.Sucesso ? saida.PrimeiraLinha() : "";
        return linha.Length == 0 ? "git não encontrado" : linha.Replace("git version ", "git ", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> ModoSshAsync(CancellationToken cancelar = default)
    {
        var variaveis = await ssh.ObterAsync(cancelar);
        if (variaveis.TryGetValue("GIT_SSH", out var plink) && !string.IsNullOrEmpty(plink)) return "SSH: Pageant (TortoiseGitPlink)";
        return variaveis.TryGetValue("GIT_SSH_COMMAND", out var comando) && !string.IsNullOrEmpty(comando)
            ? "SSH: OpenSSH com chave id_wmw"
            : "SSH: configuração do usuário";
    }
}
