using System.Security.Cryptography;
using Menage.Core.Perfis;
using Menage.Core.Projetos;
using Menage.Core.Protecao;

namespace Menage.Core.Configuracoes;

public enum ItemDeMigracao { Perfil, Preferencias, Protecao, Raiz, ChaveSsh, ScriptDaAtividade, Atualizacao, Jira, Nexus, Tema, ModoBeta }

/// <param name="Encontrado">O arquivo existe e foi entendido; só estes podem ser aplicados.</param>
public sealed record AchadoDeMigracao(ItemDeMigracao Item, string Titulo, string Arquivo, bool Encontrado, string Resumo);

/// <summary>Valores que só o app sabe aplicar (tema e preferências da janela principal) vão no relatório.</summary>
public sealed record RelatorioDeMigracao(
    IReadOnlyList<string> Aplicados, IReadOnlyList<string> Falhas, string? TemaId, bool? FetchAutomatico, bool? SaidaRecolhida);

/// <summary>
/// Importa as configurações do Git Manager antigo para o Menage. Só LÊ a pasta antiga (nada é gravado, movido ou apagado lá);
/// tudo vai para a pasta de dados do Menage. É uma ação explícita do usuário, item por item.
/// </summary>
public sealed class MigradorDoGitManager(
    ServicoDePerfis perfis, ServicoDeConfiguracoes configuracoes, ProtecaoDeBranches protecao,
    IConfiguracaoDeProjetos projetos, CofreDeCredenciais cofre)
{
    /// <summary>O que há na pasta e o que cada item faria. Não altera nada.</summary>
    public IReadOnlyList<AchadoDeMigracao> Analisar(string pasta)
    {
        return
        [
            AnalisarPerfil(pasta), AnalisarPreferencias(pasta), AnalisarProtecao(pasta), AnalisarRaiz(pasta), AnalisarSsh(pasta),
            AnalisarScript(pasta), AnalisarAtualizacao(pasta), AnalisarJira(pasta), AnalisarNexus(pasta), AnalisarTema(pasta), AnalisarModoBeta(pasta),
        ];
    }

    public RelatorioDeMigracao Aplicar(string pasta, IReadOnlyCollection<ItemDeMigracao> itens)
    {
        var aplicados = new List<string>();
        var falhas = new List<string>();
        string? tema = null;
        bool? fetch = null, saida = null;
        var achados = Analisar(pasta).Where(a => a.Encontrado && itens.Contains(a.Item));

        foreach (var achado in achados)
        {
            try
            {
                var erro = AplicarItem(pasta, achado.Item, ref tema, ref fetch, ref saida);
                if (erro is null) aplicados.Add(achado.Titulo);
                else falhas.Add($"{achado.Titulo}: {erro}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException)
            {
                falhas.Add($"{achado.Titulo}: {ex.Message}");
            }
        }

        return new RelatorioDeMigracao(aplicados, falhas, tema, fetch, saida);
    }

    string? AplicarItem(string pasta, ItemDeMigracao item, ref string? tema, ref bool? fetch, ref bool? saida)
    {
        switch (item)
        {
            case ItemDeMigracao.Perfil:
                var (ativo, listas) = LerPerfil(pasta);
                return Texto(perfis.Aplicar(ativo, listas));
            case ItemDeMigracao.Preferencias:
                var p = LerPreferencias(pasta);
                fetch = p.FetchAutomatico;
                saida = p.SaidaRecolhida;
                return Salvar(c => c with { Git = c.Git with { FerramentaDeDiff = p.Diff, FetchMinutos = p.Minutos, FetchComTags = p.Tags } });
            case ItemDeMigracao.ModoBeta: return Texto(projetos.SalvarModoBeta(LerModoBeta(pasta)!.Value));
            case ItemDeMigracao.Protecao: return Texto(protecao.Salvar(LerProtecao(pasta)!));
            case ItemDeMigracao.Raiz: return Texto(projetos.SalvarRaiz(LerRaiz(pasta)!));
            case ItemDeMigracao.ChaveSsh:
                var chave = LerChave(pasta);
                return Salvar(c => c with { Caminhos = c.Caminhos with { ChaveSsh = chave } });
            case ItemDeMigracao.ScriptDaAtividade:
                var script = LerScript(pasta)!;
                return Salvar(c => c with { Comportamento = c.Comportamento with { ScriptDaAtividade = script } });
            case ItemDeMigracao.Atualizacao:
                var verificar = LerAtualizacao(pasta)!.Value;
                return Salvar(c => c with { Comportamento = c.Comportamento with { VerificarAtualizacaoAoAbrir = verificar } });
            case ItemDeMigracao.Jira:
                var jira = LerJira(pasta)!;
                return Salvar(c => c with { Jira = jira });
            case ItemDeMigracao.Nexus:
                var nexus = LerNexus(pasta)!;
                return Texto(cofre.Guardar(ServicosDeCredencial.Nexus, nexus!.Value.Usuario, nexus.Value.Senha));
            default:
                tema = LerTema(pasta);
                return null;
        }
    }

    string? Salvar(Func<ConfiguracoesGerais, ConfiguracoesGerais> mudanca) => Texto(configuracoes.Salvar(mudanca(configuracoes.Atual)));

    static string? Texto(Resultado<Nada> r) => r.EhOk ? null : r.Erro.ToString();

    // ---- análise de cada item ----

    static AchadoDeMigracao Achado(ItemDeMigracao item, string titulo, string arquivo, string pasta, string? resumo) =>
        new(item, titulo, arquivo, resumo is not null,
            resumo ?? (File.Exists(Path.Combine(pasta, arquivo)) ? "Arquivo encontrado, mas não deu para entender." : "Arquivo não encontrado."));

    static AchadoDeMigracao AnalisarPerfil(string pasta)
    {
        var (ativo, listas) = LerPerfil(pasta);
        var nomes = ativo switch { PerfilDefinido.IdDesenvolvedor => "Desenvolvedor", PerfilDefinido.IdTester => "Tester", _ => "Geral" };
        var resumo = ativo is null && listas.Count == 0 ? null : $"Perfil ativo: {nomes}; listas editadas: {listas.Count}";
        return Achado(ItemDeMigracao.Perfil, "Perfil de uso e listas dos perfis", "perfil.json", pasta, resumo);
    }

    static (string? Ativo, Dictionary<string, IReadOnlyList<string>> Listas) LerPerfil(string pasta)
    {
        var listas = new Dictionary<string, IReadOnlyList<string>>();
        if (LeitorDoGitManager.Json(pasta, "perfil.json") is not { } raiz) return (null, listas);
        var ativo = raiz.Texto("perfil") is { } bruto ? NormalizarPerfil(bruto) : null;
        if (raiz.Campo("perfis") is { ValueKind: System.Text.Json.JsonValueKind.Object } definicoes)
            foreach (var d in definicoes.EnumerateObject())
            {
                var id = NormalizarPerfil(d.Name);
                if (id == PerfilDefinido.IdGeral || d.Value.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                var regras = d.Value.Lista("permitidas")?.Where(r => Funcionalidades.Existe(r.TrimStart('-')) || r == "*").ToList();
                if (regras is { Count: > 0 }) listas[id] = regras;
            }

        return (ativo, listas);
    }

    static string NormalizarPerfil(string perfil) => perfil.Trim().ToLowerInvariant() switch
    {
        "dev" or "desenvolvedor" or "developer" or "desenv" => PerfilDefinido.IdDesenvolvedor,
        "tester" => PerfilDefinido.IdTester,
        _ => PerfilDefinido.IdGeral,
    };

    static AchadoDeMigracao AnalisarPreferencias(string pasta)
    {
        var existe = LeitorDoGitManager.Json(pasta, "preferencias.json") is not null;
        var p = LerPreferencias(pasta);
        return Achado(ItemDeMigracao.Preferencias, "Preferências (fetch automático, saída, ferramenta de diff)", "preferencias.json", pasta,
            existe ? $"Fetch automático {(p.FetchAutomatico ? $"a cada {p.Minutos} min" : "desligado")}; diff: {FerramentasDeDiff.Nome(p.Diff)}" : null);
    }

    static (bool FetchAutomatico, int Minutos, bool Tags, bool SaidaRecolhida, string Diff) LerPreferencias(string pasta)
    {
        var raiz = LeitorDoGitManager.Json(pasta, "preferencias.json");
        if (raiz is not { } r) return (true, 10, false, true, FerramentasDeDiff.Interno);
        return (r.Booleano("fetchAutomatico") ?? true, Math.Clamp(r.Inteiro("fetchMinutos") ?? 10, 1, 240), r.Booleano("fetchTags") ?? false,
            r.Booleano("saidaRecolhida") ?? true, FerramentasDeDiff.Normalizar(r.Texto("ferramentaDiff")));
    }

    static AchadoDeMigracao AnalisarProtecao(string pasta)
    {
        var c = LerProtecao(pasta);
        return Achado(ItemDeMigracao.Protecao, "Proteção de branches e convenção de commit", "protecao.json", pasta,
            c is null ? null : $"{c.Branches.Count} branch(es) protegida(s); convenção de commit {(c.ValidarCommit ? "ligada" : "desligada")}");
    }

    static ConfigDeProtecao? LerProtecao(string pasta)
    {
        if (LeitorDoGitManager.Json(pasta, "protecao.json") is not { } r) return null;
        var tipos = r.Lista("tipos");
        return new ConfigDeProtecao
        {
            BloquearPush = r.Booleano("bloquearPush") ?? true,
            BloquearExclusao = r.Booleano("bloquearExclusao") ?? true,
            Branches = r.Lista("branches") ?? ConfigDeProtecao.BranchesPadrao,
            ValidarCommit = r.Booleano("validarCommit") ?? false,
            Tipos = tipos is { Count: > 0 } ? tipos : ConfigDeProtecao.TiposPadrao,
            AceitarTicket = r.Booleano("aceitarTicket") ?? true,
        };
    }

    static AchadoDeMigracao AnalisarRaiz(string pasta) =>
        Achado(ItemDeMigracao.Raiz, "Pasta raiz dos projetos", "config-geral.cfg", pasta, LerRaiz(pasta) is { } r ? r : null);

    static string? LerRaiz(string pasta) => LeitorDoGitManager.PrimeiraLinha(pasta, "config-geral.cfg");

    static AchadoDeMigracao AnalisarSsh(string pasta) =>
        Achado(ItemDeMigracao.ChaveSsh, "Chave SSH", "config-ssh.cfg", pasta,
            LeitorDoGitManager.PrimeiraLinha(pasta, "config-ssh.cfg") is null ? null : LerChave(pasta) is { Length: > 0 } c ? c : "Sem chave (NENHUMA)");

    /// <summary>"NENHUMA" no arquivo antigo quer dizer sem chave própria.</summary>
    static string LerChave(string pasta)
    {
        var linha = LeitorDoGitManager.PrimeiraLinha(pasta, "config-ssh.cfg") ?? "";
        return linha.Equals("NENHUMA", StringComparison.OrdinalIgnoreCase) ? "" : linha;
    }

    static AchadoDeMigracao AnalisarScript(string pasta)
    {
        var s = LerScript(pasta);
        return Achado(ItemDeMigracao.ScriptDaAtividade, "Script da atividade (merge e cherry-pick)", "merge.json", pasta,
            s is null ? null : $"{(s.Excluir ? "Retira" : "Não retira")} {s.Padrao} em: {string.Join(", ", s.Destinos)}");
    }

    static ConfigDoScriptDaAtividade? LerScript(string pasta)
    {
        if (LeitorDoGitManager.Json(pasta, "merge.json") is not { } r) return null;
        var padrao = r.Texto("padrao")?.Trim();
        var destinos = r.Lista("destinos");
        return new ConfigDoScriptDaAtividade
        {
            Excluir = r.Booleano("excluirScript") ?? true,
            Padrao = string.IsNullOrEmpty(padrao) ? ConfigDoScriptDaAtividade.PadraoDoScript : padrao,
            Destinos = destinos is { Count: > 0 } ? destinos : ["develop"],
        };
    }

    static AchadoDeMigracao AnalisarAtualizacao(string pasta) =>
        Achado(ItemDeMigracao.Atualizacao, "Verificar atualização ao abrir", "config-update.cfg", pasta,
            LerAtualizacao(pasta) is { } v ? (v ? "Ligada" : "Desligada") : null);

    static bool? LerAtualizacao(string pasta)
    {
        var texto = LeitorDoGitManager.Texto(pasta, "config-update.cfg");
        var linha = texto?.Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("verificar=", StringComparison.OrdinalIgnoreCase));
        return linha is null ? null : linha["verificar=".Length..].Trim() != "0";
    }

    AchadoDeMigracao AnalisarJira(string pasta)
    {
        var j = LerJira(pasta);
        return Achado(ItemDeMigracao.Jira, "Endereço e projetos do Jira (token não é migrado)", "diario.json", pasta,
            j is null ? null : $"{j.Url}; projetos {(j.Projetos.Length == 0 ? "quaisquer" : j.Projetos)}; token em {j.VariavelDoToken}");
    }

    ConfigDoJira? LerJira(string pasta)
    {
        if (LeitorDoGitManager.Json(pasta, "diario.json") is not { } r) return null;
        var url = r.Texto("jiraUrl")?.Trim().TrimEnd('/');
        var atual = configuracoes.Atual.Jira;
        if (string.IsNullOrEmpty(url) && r.Texto("projetosJira") is null && r.Texto("varTokenJira") is null) return null;
        return atual with
        {
            Url = string.IsNullOrEmpty(url) ? atual.Url : url,
            Projetos = r.Texto("projetosJira")?.Trim() ?? atual.Projetos,
            VariavelDoToken = string.IsNullOrWhiteSpace(r.Texto("varTokenJira")) ? atual.VariavelDoToken : r.Texto("varTokenJira")!.Trim(),
        };
    }

    static AchadoDeMigracao AnalisarNexus(string pasta)
    {
        var n = LerNexus(pasta);
        return Achado(ItemDeMigracao.Nexus, "Credencial do Nexus (senha reprotegida para este usuário do Windows)", "config-nexus.json", pasta,
            n is null ? null : $"Usuário {n.Value.Usuario}");
    }

    static (string Usuario, string Senha)? LerNexus(string pasta)
    {
        if (LeitorDoGitManager.Json(pasta, "config-nexus.json") is not { } r) return null;
        var usuario = r.Texto("usuario") ?? "";
        var protegida = r.Texto("senha");
        if (string.IsNullOrEmpty(protegida)) return null;
        try
        {
            // Mesmo formato do Git Manager antigo (DPAPI do usuário atual): só abre se for o mesmo usuário do Windows.
            return OperatingSystem.IsWindows() ? (usuario, new ProtetorDpapi().Revelar(protegida)) : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    static AchadoDeMigracao AnalisarTema(string pasta) =>
        Achado(ItemDeMigracao.Tema, "Tema", "config-tema.cfg", pasta, LerTema(pasta));

    static string? LerTema(string pasta) => LeitorDoGitManager.PrimeiraLinha(pasta, "config-tema.cfg");

    static AchadoDeMigracao AnalisarModoBeta(string pasta) =>
        Achado(ItemDeMigracao.ModoBeta, "Modo beta (frameworks seguem a versão dos poms)", "config-beta.cfg", pasta,
            LerModoBeta(pasta) is { } ligado ? (ligado ? "Ligado" : "Desligado") : null);

    /// <summary>"1" liga, "0" desliga (mesma regra do <c>config-beta.cfg</c> antigo).</summary>
    static bool? LerModoBeta(string pasta) =>
        LeitorDoGitManager.PrimeiraLinha(pasta, "config-beta.cfg") is { } linha ? linha != "0" : null;
}
