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
        ["GIT_MERGE_AUTOEDIT"] = "no",
    };

    public async Task<SaidaProcesso> ExecutarBrutoAsync(GitArgs args, CancellationToken cancelar = default)
    {
        var ambiente = new Dictionary<string, string?>(await ssh.ObterAsync(cancelar));
        foreach (var (chave, valor) in _ambienteBase) ambiente[chave] = valor;

        var comando = new ComandoProcesso(
            executavel, ["-c", "color.ui=false", "-c", "core.quotepath=false", .. args.Argumentos], args.Repositorio, ambiente, args.Timeout ?? _timeoutPadrao, args.Entrada);
        return await runner.ExecutarAsync(comando, cancelar);
    }

    public async Task<Resultado<SaidaProcesso>> ExecutarAsync(GitArgs args, CancellationToken cancelar = default)
    {
        var saida = await ExecutarBrutoAsync(args, cancelar);
        if (saida.Sucesso) return Resultado.Ok(saida);
        return Resultado.Falha<SaidaProcesso>(GitErros.Classificar(args, saida, await HostSeFaltarAsync(args, saida, cancelar)));
    }

    // O plink não diz o servidor na mensagem: nesse caso ele vem do origin do projeto.
    async Task<string> HostSeFaltarAsync(GitArgs args, SaidaProcesso saida, CancellationToken cancelar)
    {
        var falha = FalhasDoGit.Reconhecer(saida.TextoErro + "\n" + saida.Texto);
        if (falha.Tipo == TipoDeFalhaGit.Desconhecida || falha.Host.Length > 0) return "";

        var origin = await ExecutarBrutoAsync(Git.Em(args.Repositorio).Comando("remote").Argumento("get-url").Argumento("origin"), cancelar);
        return origin.Sucesso ? FalhasDoGit.HostDaUrl(origin.Texto.Trim()) : "";
    }
}
