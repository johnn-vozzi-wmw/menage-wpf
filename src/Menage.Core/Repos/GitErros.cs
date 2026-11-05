namespace Menage.Core.Repos;

/// <summary>Transforma a saída de erro do git em causa, explicação e ação sugerida.</summary>
public static class GitErros
{
    /// <param name="hostPadrao">Servidor do origin, para as mensagens que não dizem o host.</param>
    public static Erro Classificar(GitArgs args, SaidaProcesso saida, string hostPadrao = "")
    {
        var comando = args.Argumentos.FirstOrDefault(a => !a.StartsWith('-') && !a.Contains('=')) ?? "git";
        if (saida.FalhaAoIniciar is not null)
            return new Erro("Git indisponível", saida.FalhaAoIniciar, "Instale o Git for Windows ou confira o PATH.");
        if (saida.Cancelado) return Erro.Cancelado;
        if (saida.Expirou) return new Erro($"git {comando} demorou demais", "O tempo limite foi atingido.", "Tente de novo ou verifique a rede.");

        var erro = FalhasDoGit.DecodificarOctal(saida.TextoErro);
        return Conhecido(erro + "\n" + saida.Texto, hostPadrao) ?? new Erro($"git {comando} falhou (código {saida.CodigoDeSaida})", Detalhe(erro));
    }

    static string? Detalhe(string erro) => erro.Trim() is { Length: > 0 } detalhe ? detalhe : null;

    // A ordem importa: SSH configurado errado vem antes de "rede", e "conflito" só depois de todas as falhas de remoto.
    static Erro? Conhecido(string texto, string hostPadrao)
    {
        if (texto.Contains("getaddrinfo: atch", StringComparison.OrdinalIgnoreCase)) return SshIncompativel();
        if (texto.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
            return new Erro("Pasta não é um repositório Git", "Não há .git nesta pasta.", "Confira o caminho do projeto.");

        var falha = FalhasDoGit.Reconhecer(texto, hostPadrao);
        return falha.Tipo switch
        {
            TipoDeFalhaGit.ConfiguracaoSsh => SshIncompativel(),
            TipoDeFalhaGit.ChaveDoHost => new Erro("Chave do servidor desconhecida", falha.Mensagem, "Confirme a chave conectando uma vez por um terminal."),
            TipoDeFalhaGit.Acesso => Acesso(texto, falha),
            TipoDeFalhaGit.Repositorio => new Erro("Repositório remoto não encontrado", falha.Mensagem, "Confira o endereço do origin."),
            TipoDeFalhaGit.Rede => new Erro("Sem conexão com o remoto", falha.Mensagem, "Verifique a rede ou a VPN."),
            _ => texto.Contains("conflict", StringComparison.OrdinalIgnoreCase)
                ? new Erro("Conflito de merge", "Há arquivos com conflito.", "Resolva os conflitos e continue.")
                : null,
        };
    }

    static Erro Acesso(string texto, FalhaGit falha) =>
        texto.Contains("permission denied (publickey", StringComparison.OrdinalIgnoreCase) || texto.Contains("server refused our key", StringComparison.OrdinalIgnoreCase)
            ? new Erro("Chave SSH recusada", falha.Mensagem, "Carregue a chave no Pageant e tente de novo.")
            : new Erro("Autenticação recusada", falha.Mensagem, "Confira as credenciais do remoto.");

    static Erro SshIncompativel() => new(
        "Configuração de SSH incompatível",
        "O SSH recebeu argumentos de outro programa (GIT_SSH_VARIANT não combina com o ssh em uso); não é problema de rede.",
        "Confira core.sshCommand, GIT_SSH e GIT_SSH_VARIANT.");
}
