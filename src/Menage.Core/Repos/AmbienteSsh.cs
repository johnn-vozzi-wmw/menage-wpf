namespace Menage.Core.Repos;

/// <summary>Variáveis de ambiente de SSH a juntar às do git; valor nulo remove a variável.</summary>
public interface IAmbienteSsh
{
    Task<IReadOnlyDictionary<string, string?>> ObterAsync(CancellationToken cancelar = default);
}

/// <param name="ChaveSsh">Caminho da chave (sem .ppk). Padrão: <c>~\.ssh\id_wmw</c>.</param>
/// <param name="PastaTortoiseGit">Pasta <c>bin</c> do TortoiseGit. Padrão: <c>%ProgramW6432%\TortoiseGit\bin</c>.</param>
public sealed record OpcoesSsh(string? ChaveSsh = null, string? PastaTortoiseGit = null);

/// <summary>
/// Regras herdadas do Git Manager: o SSH do usuário (<c>GIT_SSH_COMMAND</c> ou <c>core.sshCommand</c>) sempre vence;
/// sem ele, Pageant + TortoiseGitPlink; sem Tortoise, OpenSSH com a chave. O resultado é calculado uma vez.
/// </summary>
public sealed class AmbienteSsh : IAmbienteSsh
{
    static readonly IReadOnlyDictionary<string, string?> _nenhum = new Dictionary<string, string?>();

    readonly IProcessoRunner _runner;
    readonly string _chave;
    readonly string _plink;
    readonly string _git;
    readonly Func<string, string?> _variavel;
    readonly Func<string, bool> _existe;
    readonly object _trava = new();
    Task<IReadOnlyDictionary<string, string?>>? _cache;

    public AmbienteSsh(
        IProcessoRunner runner,
        OpcoesSsh? opcoes = null,
        Func<string, string?>? variavel = null,
        Func<string, bool>? existe = null,
        string executavelGit = "git")
    {
        _runner = runner;
        _git = executavelGit;
        _variavel = variavel ?? Environment.GetEnvironmentVariable;
        _existe = existe ?? File.Exists;
        _chave = opcoes?.ChaveSsh ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_wmw");
        var programas = _variavel("ProgramW6432") ?? _variavel("ProgramFiles") ?? @"C:\Program Files";
        var tortoise = opcoes?.PastaTortoiseGit ?? Path.Combine(programas, "TortoiseGit", "bin");
        _plink = Path.Combine(tortoise, "TortoiseGitPlink.exe");
    }

    public Task<IReadOnlyDictionary<string, string?>> ObterAsync(CancellationToken cancelar = default)
    {
        lock (_trava) return _cache ??= Calcular();
    }

    async Task<IReadOnlyDictionary<string, string?>> Calcular()
    {
        if (!string.IsNullOrEmpty(_variavel("GIT_SSH_COMMAND")) || await SshCommandDoGitAsync()) return _nenhum;

        if (_existe(_plink))
            return new Dictionary<string, string?>
            {
                ["GIT_SSH"] = _plink,
                ["GIT_SSH_VARIANT"] = "tortoiseplink",
                ["GIT_SSH_COMMAND"] = null,
            };

        if (_existe(_chave))
            return new Dictionary<string, string?>
            {
                ["GIT_SSH_COMMAND"] = $"ssh -i \"{_chave.Replace('\\', '/')}\" -o IdentitiesOnly=yes",
            };

        return _nenhum;
    }

    async Task<bool> SshCommandDoGitAsync()
    {
        var pasta = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var comando = new ComandoProcesso(_git, ["config", "--get", "core.sshCommand"], pasta, null, TimeSpan.FromSeconds(5));
        var saida = await _runner.ExecutarAsync(comando);
        return saida.Sucesso && saida.Texto.Trim().Length > 0;
    }
}
