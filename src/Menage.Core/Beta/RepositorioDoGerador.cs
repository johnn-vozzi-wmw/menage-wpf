namespace Menage.Core.Beta;

/// <summary>
/// Guarda a configuração do Gerador (<c>gerador-beta.json</c>) e as operações simples sobre ela: favoritos, "limpar target",
/// comandos personalizados. Mantém uma cópia em memória; cada alteração grava na hora.
/// </summary>
public sealed class RepositorioDoGerador(IArmazenamento<ConfiguracaoDoGerador> armazenamento)
{
    ConfiguracaoDoGerador? _atual;

    public ConfiguracaoDoGerador Configuracao => _atual ??= armazenamento.Carregar().ValorOu(new ConfiguracaoDoGerador());

    public event Action? Alterado;

    public Resultado<Nada> Salvar(ConfiguracaoDoGerador nova)
    {
        var r = armazenamento.Salvar(nova);
        if (r.EhOk) _atual = nova;
        Alterado?.Invoke();
        return r;
    }

    public Resultado<Nada> LembrarEscolha(string pastaBase, bool deploy, IReadOnlyList<string> selecao) =>
        Configuracao.UltimaBase == pastaBase && Configuracao.UltimoDeploy == deploy && Configuracao.UltimaSelecao.SequenceEqual(selecao)
            ? Resultado.Ok()
            : Salvar(Configuracao with { UltimaBase = pastaBase, UltimoDeploy = deploy, UltimaSelecao = [.. selecao] });

    public Resultado<Nada> DefinirComando(string projeto, string? comando)
    {
        var mapa = new Dictionary<string, string>(Configuracao.ComandosPersonalizados, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(comando)) mapa.Remove(projeto);
        else mapa[projeto] = comando.Trim();
        return Salvar(Configuracao with { ComandosPersonalizados = mapa });
    }

    // ---- limpar target

    /// <summary>Ativo sem nenhum projeto não faz sentido: devolve erro e não salva. Desligado mantém a marcação guardada.</summary>
    public Resultado<Nada> DefinirLimparTarget(bool ativo, IEnumerable<string> projetos)
    {
        var lista = projetos.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ativo && lista.Count == 0)
            return Resultado.Falha<Nada>("Nenhum projeto escolhido para limpar o target", null, "Escolha ao menos um projeto ou deixe como Desabilitado.");
        return Salvar(Configuracao with { LimparTarget = new LimparTargetConfig { Ativo = ativo, Projetos = lista } });
    }

    // ---- favoritos

    public Resultado<Nada> SalvarFavorito(string nome, bool deploy, IEnumerable<string> projetos)
    {
        nome = nome.Trim();
        var lista = projetos.ToList();
        if (nome.Length == 0) return Resultado.Falha<Nada>("Dê um nome ao favorito");
        if (lista.Count == 0) return Resultado.Falha<Nada>("Marque ao menos um projeto antes de salvar o favorito");
        if (Achar(nome) is not null) return Resultado.Falha<Nada>($"Já existe um favorito chamado \"{nome}\"", null, "Escolha outro nome ou use Atualizar.");
        return Salvar(Configuracao with { Favoritos = [.. Configuracao.Favoritos, new FavoritoDoGerador { Nome = nome, Deploy = deploy, Projetos = lista }] });
    }

    public Resultado<Nada> AtualizarFavorito(string nome, bool deploy, IEnumerable<string> projetos) =>
        Alterar(nome, f => f with { Deploy = deploy, Projetos = [.. projetos] });

    public Resultado<Nada> RenomearFavorito(string nome, string novoNome)
    {
        novoNome = novoNome.Trim();
        if (novoNome.Length == 0) return Resultado.Falha<Nada>("Dê um nome ao favorito");
        if (!nome.Equals(novoNome, StringComparison.OrdinalIgnoreCase) && Achar(novoNome) is not null)
            return Resultado.Falha<Nada>($"Já existe um favorito chamado \"{novoNome}\"");
        return Alterar(nome, f => f with { Nome = novoNome });
    }

    public Resultado<Nada> ExcluirFavorito(string nome) =>
        Achar(nome) is null
            ? Resultado.Falha<Nada>($"Favorito \"{nome}\" não encontrado")
            : Salvar(Configuracao with { Favoritos = [.. Configuracao.Favoritos.Where(f => !Igual(f.Nome, nome))] });

    public FavoritoDoGerador? Achar(string nome) => Configuracao.Favoritos.FirstOrDefault(f => Igual(f.Nome, nome));

    Resultado<Nada> Alterar(string nome, Func<FavoritoDoGerador, FavoritoDoGerador> mudar) =>
        Achar(nome) is { } atual
            ? Salvar(Configuracao with { Favoritos = [.. Configuracao.Favoritos.Select(f => ReferenceEquals(f, atual) ? mudar(f) : f)] })
            : Resultado.Falha<Nada>($"Favorito \"{nome}\" não encontrado");

    static bool Igual(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
}
