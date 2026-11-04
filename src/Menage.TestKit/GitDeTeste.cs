using Menage.Core.Repos;

namespace Menage.TestKit;

/// <summary>
/// <see cref="GitExecutor"/> real, mas isolado do ~/.gitconfig e do SSH da máquina (que mudam o comportamento:
/// por exemplo, <c>remote.origin.puttykeyfile</c> global faz o "origin" existir mesmo depois de removido).
/// </summary>
public static class GitDeTeste
{
    sealed class AmbienteIsolado : IAmbienteSsh
    {
        static readonly IReadOnlyDictionary<string, string?> _variaveis = new Dictionary<string, string?>
        {
            ["GIT_CONFIG_GLOBAL"] = "NUL",
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_SSH_COMMAND"] = null,
            ["GIT_AUTHOR_NAME"] = "Teste",
            ["GIT_AUTHOR_EMAIL"] = "teste@menage.local",
            ["GIT_COMMITTER_NAME"] = "Teste",
            ["GIT_COMMITTER_EMAIL"] = "teste@menage.local",
        };

        public Task<IReadOnlyDictionary<string, string?>> ObterAsync(CancellationToken cancelar = default) =>
            Task.FromResult(_variaveis);
    }

    public static GitExecutor Criar() => new(new ProcessoRunner(), new AmbienteIsolado());
}
