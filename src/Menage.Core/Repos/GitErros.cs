namespace Menage.Core.Repos;

/// <summary>Transforma a saída de erro do git em causa, explicação e ação sugerida.</summary>
public static class GitErros
{
    public static Erro Classificar(GitArgs args, SaidaProcesso saida)
    {
        var comando = args.Argumentos.FirstOrDefault(a => !a.StartsWith('-') && !a.Contains('=')) ?? "git";
        if (saida.FalhaAoIniciar is not null)
            return new Erro("Git indisponível", saida.FalhaAoIniciar, "Instale o Git for Windows ou confira o PATH.");
        if (saida.Cancelado) return Erro.Cancelado;
        if (saida.Expirou) return new Erro($"git {comando} demorou demais", "O tempo limite foi atingido.", "Tente de novo ou verifique a rede.");

        var texto = saida.TextoErro;
        foreach (var (padrao, causa, explicacao, acao) in _regras)
            if (texto.Contains(padrao, StringComparison.OrdinalIgnoreCase))
                return new Erro(causa, explicacao, acao);

        var detalhe = texto.Trim();
        return new Erro($"git {comando} falhou (código {saida.CodigoDeSaida})", detalhe.Length == 0 ? null : detalhe);
    }

    // A ordem importa: a primeira regra que casar vence (SSH configurado errado vem antes de "rede").
    static readonly (string Padrao, string Causa, string Explicacao, string Acao)[] _regras =
    [
        ("getaddrinfo: atch", "Configuração de SSH incompatível",
            "O SSH recebeu argumentos de outro programa (GIT_SSH_VARIANT não combina com o ssh em uso); não é problema de rede.",
            "Confira core.sshCommand, GIT_SSH e GIT_SSH_VARIANT."),
        ("not a git repository", "Pasta não é um repositório Git", "Não há .git nesta pasta.", "Confira o caminho do projeto."),
        ("permission denied (publickey", "Chave SSH recusada", "O servidor não aceitou a chave carregada.", "Carregue a chave no Pageant e tente de novo."),
        ("authentication failed", "Autenticação recusada", "Usuário ou token inválido.", "Confira as credenciais do remoto."),
        ("could not resolve host", "Sem conexão com o remoto", "Não foi possível resolver o endereço do servidor.", "Verifique a rede ou a VPN."),
        ("connection timed out", "Sem conexão com o remoto", "O servidor não respondeu a tempo.", "Verifique a rede ou a VPN."),
        ("conflict", "Conflito de merge", "Há arquivos com conflito.", "Resolva os conflitos e continue."),
    ];
}
