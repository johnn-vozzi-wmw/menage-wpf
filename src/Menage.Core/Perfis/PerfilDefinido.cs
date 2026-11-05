namespace Menage.Core.Perfis;

/// <summary>
/// Um perfil de uso. <see cref="Base"/> decide o que o restante do app (comandos do catálogo, regras especiais de Melhorias,
/// Diário e Beta automática) enxerga; <see cref="Permitidas"/> é a lista editável: ids liberados, <c>*</c> libera tudo e
/// <c>-id</c> tira um item de um <c>*</c> (ex.: <c>["*", "-gerador-deploy"]</c>).
/// </summary>
public sealed record PerfilDefinido(string Id, string Nome, string Descricao, PerfilDeUso Base, IReadOnlyList<string> Permitidas)
{
    public const string IdGeral = "completo";
    public const string IdDesenvolvedor = "dev";
    public const string IdTester = "tester";

    public bool Embutido => Id is IdGeral or IdDesenvolvedor or IdTester;

    public static PerfilDefinido Geral { get; } = new(IdGeral, "Geral", DescricaoGeral, PerfilDeUso.Geral, ["*"]);

    public static IReadOnlyList<PerfilDefinido> Padroes { get; } = [Desenvolvedor(), Tester()];

    const string DescricaoGeral =
        "Funcionalidades de Git, Gerador de Beta e Nexus (padrão). Melhorias, Diário de trabalho e Beta automática ficam nos perfis Desenvolvedor e Tester.";

    public static PerfilDefinido Desenvolvedor() => new(IdDesenvolvedor, "Desenvolvedor",
        "Todas as funcionalidades de Git, Gerador de Beta e Nexus, e o gerenciador de Melhorias em andamento.", PerfilDeUso.Desenvolvedor, ["*"]);

    public static PerfilDefinido Tester() => new(IdTester, "Tester",
        "Atualizar, trocar de branch, stash, consultar histórico, Nexus, Gerador de Beta só com install (sem deploy) e a Beta automática.",
        PerfilDeUso.Tester,
        ["fetch", "pull", "sync", "checkout", "stash", "stash-pop", "status", "historico", "buscar-commit", "diagnostico",
         "gerar-beta", "gerador-install", "gerador-entregas", "gerador-config", "nexus", "nao-enviados", "copiar", "fetch-auto"]);

    public static PerfilDefinido Padrao(string id) => id switch
    {
        IdDesenvolvedor => Desenvolvedor(),
        IdTester => Tester(),
        _ => Geral,
    };

    /// <summary>Mesmas regras do Git Manager antigo; o perfil Geral sempre libera tudo, menos o que é só de Desenvolvedor/Tester.</summary>
    public bool Permite(string funcao)
    {
        if (funcao == Funcionalidades.BetaAutomatica && Base == PerfilDeUso.Tester) return true;
        if (Funcionalidades.ExigeBaseDesenvolvedor(funcao) && Base != PerfilDeUso.Desenvolvedor) return false;
        if (Id == IdGeral || string.IsNullOrEmpty(funcao)) return true;

        var liberada = false;
        foreach (var regra in Permitidas)
        {
            if (regra == "*" || string.Equals(regra, funcao, StringComparison.OrdinalIgnoreCase)) liberada = true;
            else if (regra.StartsWith('-') && string.Equals(regra[1..], funcao, StringComparison.OrdinalIgnoreCase)) liberada = false;
        }

        return liberada;
    }

    /// <summary>Texto da dica "Bloqueado no perfil X" com os perfis que liberam a funcionalidade.</summary>
    public string Motivo(string funcao, IEnumerable<PerfilDefinido> todos) =>
        $"Bloqueado no perfil {Nome}. Disponível em: {string.Join(", ", todos.Where(p => p.Permite(funcao)).Select(p => p.Nome))}.";
}
