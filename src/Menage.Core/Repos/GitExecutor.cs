namespace Menage.Core.Repos;

/// <summary>Roda o git.exe com o ambiente que impede qualquer pergunta interativa.</summary>
public sealed class GitExecutor(IProcessoRunner runner, IAmbienteSsh ssh, string executavel = "git") : IGit
{
    static readonly TimeSpan _timeoutPadrao = TimeSpan.FromMinutes(5);

    static readonly Dictionary<string, string?> _ambienteBase = new()
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_EDITOR"] = "true",
        ["GIT_SEQUENCE_EDITOR"] = "true",
        ["GIT_PAGER"] = "cat",
    };

    public async Task<SaidaProcesso> ExecutarBrutoAsync(GitArgs args, CancellationToken cancelar = default)
    {
        var ambiente = new Dictionary<string, string?>(await ssh.ObterAsync(cancelar));
        foreach (var (chave, valor) in _ambienteBase) ambiente[chave] = valor;

        var comando = new ComandoProcesso(
            executavel, args.Argumentos, args.Repositorio, ambiente, args.Timeout ?? _timeoutPadrao);
        return await runner.ExecutarAsync(comando, cancelar);
    }

    public async Task<Resultado<SaidaProcesso>> ExecutarAsync(GitArgs args, CancellationToken cancelar = default)
    {
        var saida = await ExecutarBrutoAsync(args, cancelar);
        return saida.Sucesso ? Resultado.Ok(saida) : Resultado.Falha<SaidaProcesso>(GitErros.Classificar(args, saida));
    }
}
