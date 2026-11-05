using Menage.Core.Armazenamento;

namespace Menage.Core.Configuracoes;

public static class FerramentasDeDiff
{
    public const string Interno = "interno";
    public const string Tortoise = "tortoise";
    public const string WinMerge = "winmerge";

    public static IReadOnlyList<string> Todas { get; } = [Interno, Tortoise, WinMerge];

    public static string Nome(string id) => id switch
    {
        Tortoise => "TortoiseGit",
        WinMerge => "WinMerge",
        _ => "Menage (interno)",
    };

    /// <summary>Valor desconhecido ou vazio vira o interno (mesma regra do Git Manager).</summary>
    public static string Normalizar(string? id)
    {
        var limpo = (id ?? "").Trim().ToLowerInvariant();
        return Todas.Contains(limpo) ? limpo : Interno;
    }
}

public sealed record ConfigDeGit
{
    public string FerramentaDeDiff { get; init; } = FerramentasDeDiff.Interno;

    /// <summary>Intervalo do fetch automático, de 1 a 240 minutos. Ligar e desligar fica na janela principal.</summary>
    public int FetchMinutos { get; init; } = 10;

    /// <summary>Última escolha da caixa "tags" do Fetch manual.</summary>
    public bool FetchComTags { get; init; }
}

/// <summary>Jira. O token nunca fica aqui: vai para o <see cref="CofreDeCredenciais"/> (DPAPI) ou para a variável de ambiente.</summary>
public sealed record ConfigDoJira
{
    public const string UrlPadrao = "https://jira2.wmw.com.br";

    public string Url { get; init; } = UrlPadrao;

    public string Usuario { get; init; } = "";

    /// <summary>Variável de ambiente lida quando não há token no cofre.</summary>
    public string VariavelDoToken { get; init; } = "JIRA_TOKEN";

    /// <summary>Chaves de projeto reconhecidas em mensagens e branches, separadas por vírgula; vazio = qualquer CHAVE-123.</summary>
    public string Projetos { get; init; } = "VENDAS";
}

public sealed record ConfigDeCaminhos
{
    /// <summary>Chave SSH (sem a extensão .ppk); vazio = a padrão do usuário.</summary>
    public string ChaveSsh { get; init; } = "";

    public string WinMerge { get; init; } = "";

    /// <summary>Pasta bin do TortoiseGit; vazio = procurar nos lugares de costume.</summary>
    public string TortoiseGit { get; init; } = "";
}

/// <summary>Merge e cherry-pick: o script de banco da atividade não vai junto para a develop.</summary>
public sealed record ConfigDoScriptDaAtividade
{
    public const string PadraoDoScript = "docs/modelo-er/scripts/script_lavendere_alteracoes_%NOME_BRANCH%*";

    public bool Excluir { get; init; } = true;

    public string Padrao { get; init; } = PadraoDoScript;

    /// <summary>Branches de destino em que o script é retirado; <c>*</c> vale como curinga.</summary>
    public IReadOnlyList<string> Destinos { get; init; } = ["develop"];
}

public sealed record ConfigDeComportamento
{
    public bool VerificarAtualizacaoAoAbrir { get; init; } = true;

    public ConfigDoScriptDaAtividade ScriptDaAtividade { get; init; } = new();
}

/// <summary>Conteúdo de <c>configuracoes.json</c>. Raiz dos projetos, proteção de branches e perfis têm arquivo próprio.</summary>
public sealed record ConfiguracoesGerais
{
    public ConfigDeGit Git { get; init; } = new();

    public ConfigDoJira Jira { get; init; } = new();

    public ConfigDeCaminhos Caminhos { get; init; } = new();

    public ConfigDeComportamento Comportamento { get; init; } = new();

    /// <summary>Nulo se pode gravar; senão, o motivo.</summary>
    public Erro? Validar()
    {
        if (Git.FetchMinutos is < 1 or > 240) return new Erro("Intervalo do fetch inválido", $"{Git.FetchMinutos} minutos.", "Use de 1 a 240 minutos.");
        if (!FerramentasDeDiff.Todas.Contains(Git.FerramentaDeDiff)) return new Erro("Ferramenta de diff desconhecida", Git.FerramentaDeDiff);
        if (Jira.Url.Length > 0 && !(Uri.TryCreate(Jira.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            return new Erro("Endereço do Jira inválido", Jira.Url, "Use o endereço completo, começando por https://.");
        var script = Comportamento.ScriptDaAtividade;
        if (script.Excluir && string.IsNullOrWhiteSpace(script.Padrao)) return new Erro("Informe o arquivo do script da atividade", null, "Ou desligue a exclusão do script.");
        if (script.Excluir && script.Destinos.Count == 0) return new Erro("Informe ao menos uma branch de destino do script da atividade", null, "Ou desligue a exclusão do script.");
        return null;
    }
}

/// <summary>Guarda as configurações gerais e avisa quem depende delas. Arquivo ilegível não impede o app: vale o padrão.</summary>
public sealed class ServicoDeConfiguracoes(IArmazenamento<ConfiguracoesGerais> armazenamento)
{
    public ServicoDeConfiguracoes(PastaDeDados pasta)
        : this(new Armazenamento<ConfiguracoesGerais>(pasta, "configuracoes", padrao: () => new ConfiguracoesGerais()))
    {
    }

    public event EventHandler? Alterado;

    public ConfiguracoesGerais Atual { get; private set; } = armazenamento.Carregar().ValorOu(new ConfiguracoesGerais());

    public Resultado<Nada> Salvar(ConfiguracoesGerais novas)
    {
        if (novas.Validar() is { } invalido) return Resultado.Falha<Nada>(invalido);
        var gravou = armazenamento.Salvar(novas);
        if (!gravou.EhOk) return gravou;
        Atual = novas;
        Alterado?.Invoke(this, EventArgs.Empty);
        return gravou;
    }
}
