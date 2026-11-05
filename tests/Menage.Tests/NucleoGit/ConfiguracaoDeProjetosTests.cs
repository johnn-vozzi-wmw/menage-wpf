namespace Menage.Tests.NucleoGit;

public sealed class ConfiguracaoDeProjetosTests : IDisposable
{
    readonly string _raiz = Path.Combine(Path.GetTempPath(), "mc" + Guid.NewGuid().ToString("N")[..6]);

    public ConfiguracaoDeProjetosTests() => Directory.CreateDirectory(_raiz);

    PastaDeDados Pasta => new(Path.Combine(_raiz, "dados"));

    string ArquivoDeProjetos => Path.Combine(Pasta.Caminho, "projetos.json");

    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    string Repo(params string[] partes)
    {
        var caminho = Path.Combine([_raiz, "raiz", .. partes]);
        Directory.CreateDirectory(Path.Combine(caminho, ".git"));
        return caminho;
    }

    /// <summary>Grava o catálogo padrão apontando para a raiz de teste, para nunca olhar para pastas reais.</summary>
    ConfiguracaoDeProjetos Abrir(Func<CatalogoDeProjetos, CatalogoDeProjetos>? ajustar = null)
    {
        var catalogo = CatalogoDeProjetos.Padrao() with { Raiz = Path.Combine(_raiz, "raiz") };
        new Armazenamento<CatalogoDeProjetos>(Pasta, "projetos").Salvar(ajustar?.Invoke(catalogo) ?? catalogo);
        return new ConfiguracaoDeProjetos(Pasta);
    }

    [Fact]
    public void Primeira_leitura_sem_arquivo_cria_os_quatro_grupos_no_formato_proprio()
    {
        var config = new ConfiguracaoDeProjetos(new PastaDeDados(Path.Combine(_raiz, "novo")));

        config.Catalogo.Grupos.Select(g => g.Id).Should().Equal("WEB", "FRAMEWORK", "APP", "FRAMEWORK-TC");
        File.Exists(Path.Combine(_raiz, "novo", "projetos.json")).Should().BeTrue();
    }

    [Fact]
    public void Descobre_pela_pasta_raiz_web_framework_e_pda_na_ordem_dos_grupos()
    {
        Repo("web", "b-web");
        Repo("web", "a-web");
        Directory.CreateDirectory(Path.Combine(_raiz, "raiz", "web", "sem-git"));
        Repo("framework", "framework-x");
        Repo("pda", "lavenderepda");
        Repo("pda", "framework-tc");
        Repo("pda", "outro");

        var config = Abrir();

        config.Projetos.Select(p => p.Nome).Should().Equal("a-web", "b-web", "framework-x", "lavenderepda", "framework-tc");
        config.Projetos.Select(p => p.GrupoPrincipal).Should().Equal("WEB", "WEB", "FRAMEWORK", "APP", "FRAMEWORK-TC");
        config.Projetos.Single(p => p.Nome == "framework-x").EhFramework.Should().BeTrue();
    }

    [Fact]
    public void Grupo_com_padrao_soma_vinculos_e_o_principal_persistido_vence()
    {
        Repo("web", "lavendereweb");
        Repo("framework", "framework-core");
        var config = Abrir();
        var novo = new GrupoDeProjetos("novo", "Cliente Á") { Cor = "#3498AB", Padroes = ["LAVENDERE*"] };

        config.Salvar(config.Catalogo with { Grupos = [novo, .. config.Catalogo.Grupos] }).EhOk.Should().BeTrue();

        var web = config.Projetos.Single(p => p.Nome == "lavendereweb");
        web.Grupos.Should().BeEquivalentTo("novo", "WEB");
        web.GrupoPrincipal.Should().Be("WEB");

        var principais = new Dictionary<string, string>(config.Catalogo.Principais) { [web.Caminho] = "novo" };
        config.Salvar(config.Catalogo with { Principais = principais });
        new ConfiguracaoDeProjetos(Pasta).Projetos.Single(p => p.Nome == "lavendereweb").GrupoPrincipal.Should().Be("novo");
    }

    [Fact]
    public void Renomear_grupo_preserva_a_familia_tecnica()
    {
        Repo("framework", "framework-core");
        var config = Abrir();

        config.Salvar(config.Catalogo with
        {
            Grupos = [.. config.Catalogo.Grupos.Select(g => g.Id == "FRAMEWORK" ? g with { Nome = "Bibliotecas" } : g)],
        });

        var projeto = new ConfiguracaoDeProjetos(Pasta).Projetos.Single();
        projeto.Familia.Should().Be(Familias.Framework);
        config.Catalogo.NomeDoGrupo("FRAMEWORK").Should().Be("Bibliotecas");
    }

    [Fact]
    public void Excluir_grupos_preserva_os_repositorios_como_sem_grupo()
    {
        Repo("web", "lavendereweb");
        var config = Abrir();

        config.Salvar(config.Catalogo with { Grupos = [] });

        var projeto = config.Projetos.Single();
        projeto.Grupos.Should().BeEmpty();
        projeto.GrupoPrincipal.Should().Be(GruposEspeciais.SemGrupo);
        new ProjetosConfigurados(config).DoGrupo(GruposEspeciais.SemGrupo).Should().ContainSingle();
    }

    [Fact]
    public void Busca_recursiva_inclui_aninhados_e_marcador_git_em_arquivo_sem_duplicar_raizes()
    {
        var pai = Path.Combine(_raiz, "aninhados");
        var filho = Path.Combine(pai, "modulos", "filho");
        var neto = Path.Combine(filho, "neto");
        foreach (var d in new[] { pai, filho, neto }) Directory.CreateDirectory(Path.Combine(d, ".git"));
        Directory.CreateDirectory(Path.Combine(pai, ".git", "interno", ".git"));
        var worktree = Path.Combine(pai, "worktree");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: x");

        var config = Abrir(c => c with { PastasDeBusca = [pai, filho] });

        config.Projetos.Select(p => p.Caminho).Should().BeEquivalentTo([pai, filho, neto, worktree]);
    }

    [Fact]
    public void Repositorio_adicional_e_incluido_uma_vez()
    {
        var avulso = Repo("avulso", "meu");

        Abrir(c => c with { Repositorios = [avulso, avulso] }).Projetos.Should().ContainSingle(p => p.Caminho == avulso);
    }

    [Theory]
    [InlineData("FRAMEWORK-X", "framework-?", true)]
    [InlineData("framework-xx", "framework-?", false)]
    [InlineData("Lavendereweb", "lavendere*", true)]
    public void Curingas_sem_diferenciar_caixa(string nome, string padrao, bool casa) =>
        ResolvedorDeProjetos.Casa(nome, padrao).Should().Be(casa);

    [Fact]
    public void Validacao_rejeita_cor_invalida_nome_repetido_e_caminho_relativo()
    {
        var ok = new GrupoDeProjetos("a", "A");
        new CatalogoDeProjetos { Grupos = [ok with { Cor = "cor inválida" }] }.Validar().Should().NotBeNull();
        new CatalogoDeProjetos { Grupos = [ok, ok with { Id = "b" }] }.Validar().Should().NotBeNull();
        new CatalogoDeProjetos { Grupos = [ok with { Id = "TODOS" }] }.Validar().Should().NotBeNull();
        new CatalogoDeProjetos { Repositorios = ["relativo"] }.Validar().Should().NotBeNull();
        new CatalogoDeProjetos { Grupos = [ok with { Cor = "#aabbcc" }] }.Validar().Should().BeNull();
    }

    [Fact]
    public void Arquivo_invalido_usa_o_backup_e_nunca_e_sobrescrito()
    {
        var config = Abrir();
        config.Salvar(config.Catalogo with { Grupos = [new GrupoDeProjetos("so", "Só este")] });
        config.Salvar(config.Catalogo with { Grupos = [new GrupoDeProjetos("so", "Só este"), new GrupoDeProjetos("dois", "Dois")] });
        File.WriteAllText(ArquivoDeProjetos, "invalido");

        var recuperada = new ConfiguracaoDeProjetos(Pasta);

        recuperada.Aviso.Should().Contain("Original preservado");
        recuperada.Catalogo.Grupos.Should().NotBeEmpty();
        File.ReadAllText(ArquivoDeProjetos).Should().Be("invalido");
    }

    [Fact]
    public void Modo_beta_e_gravado_e_lido_de_novo()
    {
        var config = Abrir();
        config.Ligado.Should().BeFalse();

        config.SalvarModoBeta(true).EhOk.Should().BeTrue();

        new ConfiguracaoDeProjetos(Pasta).Ligado.Should().BeTrue();
    }

    [Fact]
    public void Trocar_a_raiz_procura_de_novo_os_projetos_reconhecidos_e_mantem_os_demais()
    {
        var antiga = Repo("web", "lavendereweb");
        var config = Abrir(c => c with { Grupos = [.. c.Grupos.Select(g => g.Id == "WEB" ? g with { Projetos = [antiga, "meu-projeto"] } : g)] });

        var novaRaiz = Path.Combine(_raiz, "nova");
        var nova = Path.Combine(novaRaiz, "qualquer", "lavendereweb");
        Directory.CreateDirectory(Path.Combine(nova, ".git"));

        config.SalvarRaiz(novaRaiz).EhOk.Should().BeTrue();

        var projetosDoWeb = config.Catalogo.Obter("WEB")!.Projetos;
        projetosDoWeb.Should().Contain([nova, "meu-projeto"]).And.NotContain(antiga);
    }

    [Fact]
    public void Marcar_grupo_marca_so_os_projetos_dele()
    {
        Repo("web", "lavendereweb");
        Repo("framework", "framework-core");
        var projetos = new ProjetosConfigurados(Abrir());

        projetos.MarcarGrupo("WEB", true);

        projetos.Marcados.Select(p => p.Nome).Should().Equal("lavendereweb");
        projetos.Grupos.Should().Contain("WEB");
    }
}
