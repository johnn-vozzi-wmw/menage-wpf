namespace Menage.Core.Perfis;

/// <summary>Uma funcionalidade que um perfil pode liberar ou bloquear. O id é o mesmo do Git Manager antigo.</summary>
public sealed record Funcionalidade(string Id, string Nome, string Grupo);

public static class Funcionalidades
{
    public const string Melhorias = "melhorias";
    public const string Diario = "diario";
    public const string BetaAutomatica = "beta-auto";

    const string Barra = "Barra de ações";
    const string Ferramentas = "Ferramentas";
    const string Gerador = "Gerador de Beta";
    const string Cherry = "Cherry-pick";

    public static IReadOnlyList<Funcionalidade> Catalogo { get; } =
    [
        new("fetch", "Fetch", Barra), new("pull", "Pull", Barra), new("sync", "Sync", Barra), new("checkout", "Checkout", Barra),
        new("nova-branch", "Nova branch", Barra), new("stash", "Stash", Barra), new("stash-pop", "Stash pop", Barra),
        new("rebase", "Rebase", Barra), new("com-develop", "Com develop", Barra), new("commit", "Commit", Barra),
        new("commit-pom", "Commit pom", Barra), new("push", "Push", Barra), new("status", "Status", Barra),
        new("historico", "Cherry-pick (abrir e consultar o histórico)", Barra), new("buscar-commit", "Buscar commit", Barra),
        new("excluir", "Excluir branches/tags", Barra), new("limpeza", "Limpeza de branches", Barra),
        new("desfazer-commit", "Desfazer commit", Barra), new("renomear-branch", "Renomear branch", Barra),
        new("diagnostico", "Diagnóstico", Barra), new("gerar-beta", "Gerador de Beta (abrir)", Barra),
        new("protecao", "Proteção de branches e convenção de commit", Ferramentas),
        new("merge-config", "Merge e cherry-pick: script da atividade", Ferramentas),
        new("push-multi", "Push em vários remotos", Ferramentas), new("tag-massa", "Criar tag em massa", Ferramentas),
        new("definir-origin", "Definir o origin da branch", Ferramentas), new("mudar-head", "Mudar a branch de todos os projetos", Ferramentas),
        new("nexus", "Nexus: buscar e baixar", Ferramentas), new("tomcat-express", "Tomcat Express: subir ambientes", Ferramentas),
        new("nao-enviados", "Commits não enviados", Ferramentas), new("git-doctor", "Git Doctor", Ferramentas),
        new("copiar", "Copiar dos projetos marcados", Ferramentas), new("fetch-auto", "Fetch automático", Ferramentas),
        new("historico-cherry", "Cherry-pick: aplicar os marcados", Cherry), new("cherry-fila", "Fila de cherry-pick em lote", Ferramentas),
        new("historico-stash", "Cherry-pick: stash dos marcados", Cherry),
        new(Melhorias, "Melhorias em andamento", Ferramentas), new(Diario, "Diário de trabalho (worklogs do Jira)", Ferramentas),
        new(BetaAutomatica, "Beta automática (monitor do Jira)", Ferramentas),
        new("gerador-install", "Gerador de Beta: install", Gerador), new("gerador-deploy", "Gerador de Beta: deploy (publica no Nexus)", Gerador),
        new("gerador-entregas", "Gerador de Beta: entregas (pasta/zip)", Gerador), new("gerador-config", "Gerador de Beta: configurações", Gerador),
    ];

    /// <summary>Só o perfil de base Desenvolvedor pode ter estas (a Beta automática também vale para base Tester).</summary>
    public static bool ExigeBaseDesenvolvedor(string id) => id is Melhorias or Diario or BetaAutomatica;

    public static bool Existe(string id) => Catalogo.Any(f => f.Id == id);

    /// <summary>
    /// Converte o id de uma operação ("git.fetch", "ferramentas.tag-em-massa") no id da funcionalidade;
    /// nulo se a operação não é controlada por perfil.
    /// </summary>
    public static string? DaOperacao(string idDaOperacao)
    {
        var parte = idDaOperacao[(idDaOperacao.IndexOf('.') + 1)..];
        parte = parte switch
        {
            "tag-em-massa" => "tag-massa",
            "criar-branch" => "nova-branch",
            "excluir-branches" => "excluir",
            "cherry-pick" => "historico-cherry",
            "arquivos-alterados" or "divergencia" => "diagnostico",
            _ => parte,
        };
        return Existe(parte) ? parte : null;
    }
}
