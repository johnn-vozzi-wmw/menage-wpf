namespace Menage.Core.Melhorias;

public sealed record VisaoPronta(string Nome, string Consulta, int Total);

/// <summary>Guarda o cadastro (<c>melhorias.json</c>): CRUD, tags, visões salvas e a última busca. Cada alteração grava na hora.</summary>
public sealed class RepositorioDeMelhorias(IArmazenamento<ConfigMelhorias> armazenamento, IContextoDeBanco banco)
{
    static readonly (string Nome, string Consulta)[] Visoes =
    [
        ("Todas", ""), ("Ativa", "ativa:sim"), ("Em andamento", "status:andamento"), ("Pausadas", "status:pausada"), ("Concluídas", "status:concluida"),
        ("Recentes", "usada:7"), ("Sem tags", "sem:tag"), ("Sem banco", "sem:banco"), ("Oracle", "banco:oracle"), ("PostgreSQL", "banco:postgres"), ("SQL Server", "banco:sqlserver"),
    ];

    ConfigMelhorias? _atual;

    public ConfigMelhorias Config => _atual ??= Normalizar(armazenamento.Carregar().ValorOu(new ConfigMelhorias()));

    public event Action? Alterado;

    public Resultado<Nada> Salvar()
    {
        var r = armazenamento.Salvar(Config);
        Alterado?.Invoke();
        return r;
    }

    static ConfigMelhorias Normalizar(ConfigMelhorias c)
    {
        if (string.IsNullOrWhiteSpace(c.VersaoConf.Caminho)) c.VersaoConf.Caminho = new ConfigVersaoConf().Caminho;
        return c;
    }

    public Melhoria? Achar(string id) => Config.Achar(id);

    /// <summary>Cria ou atualiza (pelo id). Id vazio ou repetido na criação é erro.</summary>
    public Resultado<Melhoria> Gravar(Melhoria m, bool nova)
    {
        m.Id = m.Id.Trim();
        if (m.Id.Length == 0) return Resultado.Falha<Melhoria>("Informe o id da melhoria", null, "Ex.: VENDAS-24574.");
        if (m.Branches.Select(b => b.Projeto).Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Branches.Count)
            return Resultado.Falha<Melhoria>("Há projeto repetido na melhoria");
        if (m.Branches.Any(b => b.Branch.Trim().Length == 0)) return Resultado.Falha<Melhoria>("Todo projeto precisa de uma branch");

        var existente = Config.Achar(m.Id);
        if (nova && existente is not null) return Resultado.Falha<Melhoria>($"Já existe a melhoria {m.Id}");
        if (!nova && existente is null) return Resultado.Falha<Melhoria>($"A melhoria {m.Id} não existe");

        m.Tags = [.. m.Tags.Select(Melhoria.NormalizarTag).Where(t => t.Length > 0).Distinct()];
        if (existente is null)
        {
            if (m.CriadaEm.Length == 0) m.CriadaEm = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            Config.Lista.Add(m);
        }
        else Config.Lista[Config.Lista.IndexOf(existente)] = m;

        var r = Salvar();
        return r.EhOk ? Resultado.Ok(m) : Resultado.Falha<Melhoria>(r.Erro);
    }

    /// <summary>Excluir a ativa não é permitido: desvincule antes.</summary>
    public Resultado<Nada> Excluir(string id)
    {
        if (Config.Achar(id) is not { } m) return Resultado.Falha<Nada>($"A melhoria {id} não existe");
        if (Config.Ativa.Equals(id, StringComparison.OrdinalIgnoreCase)) return Resultado.Falha<Nada>("A melhoria está ativa", null, "Desvincule antes de excluir.");
        Config.Lista.Remove(m);
        return Salvar();
    }

    public List<Melhoria> Buscar(string? consulta) => FiltroDeMelhorias.Parse(consulta).Aplicar(Config.Lista, Config.Ativa, banco);

    /// <summary>Visões prontas com contagem; a que não tem resultado some (menos "Todas").</summary>
    public IReadOnlyList<VisaoPronta> VisoesProntas() =>
        [.. Visoes.Select(v => new VisaoPronta(v.Nome, v.Consulta, Buscar(v.Consulta).Count)).Where(v => v.Consulta.Length == 0 || v.Total > 0)];

    public Resultado<Nada> SalvarVisao(string nome, string consulta)
    {
        nome = nome.Trim();
        if (nome.Length == 0 || consulta.Trim().Length == 0) return Resultado.Falha<Nada>("Dê um nome e escreva uma busca");
        Config.Filtros.RemoveAll(f => f.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
        Config.Filtros.Add(new FiltroSalvo { Nome = nome, Consulta = consulta.Trim() });
        return Salvar();
    }

    public Resultado<Nada> RemoverVisao(string nome)
    {
        Config.Filtros.RemoveAll(f => f.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
        return Salvar();
    }

    public Resultado<Nada> LembrarConsulta(string consulta)
    {
        if (Config.Consulta == consulta) return Resultado.Ok();
        Config.Consulta = consulta;
        return Salvar();
    }

    public Resultado<Nada> RenomearTag(string de, string para)
    {
        Config.RenomearTag(de, para);
        return Salvar();
    }

    public Resultado<Nada> RemoverTag(string tag)
    {
        Config.RemoverTag(tag);
        return Salvar();
    }

    public Resultado<Nada> DefinirCorDaTag(string tag, string? cor)
    {
        Config.DefinirCor(tag, cor);
        return Salvar();
    }

    public Resultado<Nada> ConfigurarVersaoConf(bool ativo, string? caminho = null)
    {
        Config.VersaoConf.Ativo = ativo;
        if (!string.IsNullOrWhiteSpace(caminho)) Config.VersaoConf.Caminho = caminho.Trim();
        return Salvar();
    }

    /// <summary>Ligar sem caminho é erro (o seletor de arquivo abre na tela).</summary>
    public Resultado<Nada> ConfigurarSqlite(bool ativo, string? caminho = null)
    {
        if (!string.IsNullOrWhiteSpace(caminho)) Config.Sqlite.Caminho = caminho.Trim();
        if (ativo && Config.Sqlite.Caminho.Length == 0)
            return Resultado.Falha<Nada>("Falta o caminho do wmwvendas.sqlite3", null, "Escolha o arquivo antes de ligar.");
        Config.Sqlite.Ativo = ativo;
        return Salvar();
    }
}
