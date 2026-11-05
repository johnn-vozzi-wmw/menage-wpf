using System.Security.Cryptography;
using System.Text;
using Menage.Core.Armazenamento;

namespace Menage.Core.Configuracoes;

/// <summary>Serviços que guardam usuário e senha ou token por usuário do Windows.</summary>
public static class ServicosDeCredencial
{
    public const string Jira = "jira";
    public const string Nexus = "nexus";
    public const string GitHub = "github";

    public static IReadOnlyList<(string Id, string Nome, string RotuloDoSegredo)> Todos { get; } =
    [
        (Jira, "Jira", "Token de acesso"),
        (Nexus, "Nexus", "Senha"),
        (GitHub, "GitHub (atualizações)", "Token de acesso"),
    ];
}

/// <summary>Protege e revela segredos. A implementação real usa DPAPI; testes usam uma falsa.</summary>
public interface IProtetorDeSegredos
{
    string Proteger(string texto);

    string Revelar(string protegido);
}

/// <summary>DPAPI do usuário atual: só o mesmo usuário, no mesmo PC, consegue revelar. Mesmo formato do Git Manager antigo.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ProtetorDpapi : IProtetorDeSegredos
{
    public string Proteger(string texto) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(texto), null, DataProtectionScope.CurrentUser));

    public string Revelar(string protegido) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protegido), null, DataProtectionScope.CurrentUser));
}

public sealed record CredencialGuardada(string Usuario, string Protegido);

public sealed record ArquivoDeCredenciais
{
    public Dictionary<string, CredencialGuardada> Itens { get; init; } = [];
}

/// <summary>Credenciais em <c>credenciais.json</c>: o usuário aparece em claro, o segredo só protegido. Nunca vão para logs nem telas.</summary>
public sealed class CofreDeCredenciais(IArmazenamento<ArquivoDeCredenciais> armazenamento, IProtetorDeSegredos protetor)
{
    public CofreDeCredenciais(PastaDeDados pasta, IProtetorDeSegredos protetor)
        : this(new Armazenamento<ArquivoDeCredenciais>(pasta, "credenciais", padrao: () => new ArquivoDeCredenciais()), protetor)
    {
    }

    public bool Tem(string servico) => Ler().Itens.ContainsKey(servico);

    public string Usuario(string servico) => Ler().Itens.TryGetValue(servico, out var c) ? c.Usuario : "";

    /// <summary>Nunca lança: segredo de outro usuário ou corrompido devolve erro com a ação sugerida.</summary>
    public Resultado<string> Segredo(string servico)
    {
        if (!Ler().Itens.TryGetValue(servico, out var c)) return Resultado.Falha<string>($"Não há credencial de {servico}", null, "Cadastre em Configurações > Credenciais.");
        try
        {
            return Resultado.Ok(protetor.Revelar(c.Protegido));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return Resultado.Falha<string>($"Não foi possível abrir a credencial de {servico}", "Ela foi gravada por outro usuário do Windows ou está corrompida.", "Cadastre de novo.");
        }
    }

    public Resultado<Nada> Guardar(string servico, string usuario, string segredo)
    {
        if (string.IsNullOrWhiteSpace(segredo)) return Resultado.Falha<Nada>("Informe o segredo", null, "Para apagar, use Remover.");
        var arquivo = Ler();
        var itens = new Dictionary<string, CredencialGuardada>(arquivo.Itens) { [servico] = new CredencialGuardada(usuario.Trim(), protetor.Proteger(segredo)) };
        return armazenamento.Salvar(new ArquivoDeCredenciais { Itens = itens });
    }

    /// <summary>Muda só o usuário, mantendo o segredo guardado.</summary>
    public Resultado<Nada> TrocarUsuario(string servico, string usuario)
    {
        var arquivo = Ler();
        if (!arquivo.Itens.TryGetValue(servico, out var atual)) return Resultado.Ok();
        var itens = new Dictionary<string, CredencialGuardada>(arquivo.Itens) { [servico] = atual with { Usuario = usuario.Trim() } };
        return armazenamento.Salvar(new ArquivoDeCredenciais { Itens = itens });
    }

    public Resultado<Nada> Remover(string servico)
    {
        var arquivo = Ler();
        if (!arquivo.Itens.ContainsKey(servico)) return Resultado.Ok();
        var itens = arquivo.Itens.Where(p => p.Key != servico).ToDictionary(p => p.Key, p => p.Value);
        return armazenamento.Salvar(new ArquivoDeCredenciais { Itens = itens });
    }

    ArquivoDeCredenciais Ler() => armazenamento.Carregar().ValorOu(new ArquivoDeCredenciais());
}
