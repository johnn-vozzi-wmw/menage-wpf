using Menage.Core.Armazenamento;
using Menage.Core.Http;

namespace Menage.Core.Nexus;

/// <summary>Configuração do Nexus + senha protegida. Fábrica do <see cref="ClienteNexus"/>.</summary>
public sealed class RepositorioDoNexus(IArmazenamento<ConfiguracaoDoNexus> armazenamento, IProtetorDeSegredo protetor, IClienteHttp http)
{
    ConfiguracaoDoNexus? _cache;

    public ConfiguracaoDoNexus Configuracao => _cache ??= armazenamento.Carregar().ValorOu(ConfiguracaoDoNexus.Padrao());

    public Resultado<Nada> Salvar(ConfiguracaoDoNexus nova)
    {
        var limpa = nova with { Url = nova.Url.Trim().TrimEnd('/') };
        var resultado = armazenamento.Salvar(limpa);
        if (resultado.EhOk) _cache = limpa;
        return resultado;
    }

    /// <summary>Salva trocando a senha (vazia = mantém a atual). A senha em claro nunca é gravada.</summary>
    public Resultado<Nada> SalvarComSenha(ConfiguracaoDoNexus nova, string? senhaNova) =>
        Salvar(string.IsNullOrEmpty(senhaNova) ? nova with { SenhaProtegida = Configuracao.SenhaProtegida } : nova with { SenhaProtegida = protetor.Proteger(senhaNova) });

    public Resultado<ClienteNexus> Cliente(ConfiguracaoDoNexus? configuracao = null, string? senhaEmClaro = null)
    {
        var c = configuracao ?? Configuracao;
        var senha = senhaEmClaro ?? protetor.Revelar(c.SenhaProtegida);
        return c.Url.Length > 0 && c.Usuario.Length > 0 && senha.Length > 0
            ? Resultado.Ok(new ClienteNexus(http, c, senha))
            : Resultado.Falha<ClienteNexus>("O Nexus ainda não está configurado", "Faltam endereço, usuário ou senha.", "Abra Servidor e preencha.");
    }
}

/// <summary>Busca com vários names (a API filtra um por vez) e paginação: até 5 páginas por name a cada pedido.</summary>
public sealed class SessaoDeBuscaNexus(ClienteNexus cliente)
{
    const int PaginasPorPedido = 5;
    readonly List<ComponenteNexus> _componentes = [];
    readonly HashSet<string> _chaves = [];
    List<(string? Nome, string? Token)> _fila = [];
    ConsultaNexus _consulta = new();

    public IReadOnlyList<ComponenteNexus> Componentes => _componentes;

    public bool TemMais => _fila.Count > 0;

    public static IReadOnlyList<string> SepararNames(string texto) =>
        [.. texto.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase)];

    public async Task<Resultado<int>> IniciarAsync(ConsultaNexus consulta, CancellationToken cancelar = default)
    {
        if (consulta.Vazia) return Resultado.Falha<int>("Informe ao menos um filtro", "name, group, version ou repositório.", "Preencha um deles e busque de novo.");
        _componentes.Clear();
        _chaves.Clear();
        _consulta = consulta with { Repositorio = consulta.Repositorio.Trim() == "(todos)" ? "" : consulta.Repositorio.Trim() };
        var nomes = SepararNames(consulta.Names);
        _fila = nomes.Count == 0 ? [(null, null)] : [.. nomes.Select(n => ((string?)n, (string?)null))];
        return await CarregarMaisAsync(cancelar);
    }

    public async Task<Resultado<int>> CarregarMaisAsync(CancellationToken cancelar = default)
    {
        var versao = _consulta.Versao.Trim();
        if (versao.Length > 0 && !versao.Contains('*', StringComparison.Ordinal)) versao += "*";
        var restante = new List<(string?, string?)>();
        var novos = 0;
        foreach (var (nome, tokenInicial) in _fila)
        {
            var token = tokenInicial;
            for (var i = 0; i < PaginasPorPedido; i++)
            {
                var pagina = await cliente.BuscarAsync(_consulta.Repositorio, nome, _consulta.Grupo, versao, token, cancelar);
                if (!pagina.EhOk) return Resultado.Falha<int>(pagina.Erro);
                foreach (var c in pagina.Valor.Componentes)
                    if (_chaves.Add(c.Chave))
                    {
                        _componentes.Add(c);
                        novos++;
                    }

                token = pagina.Valor.ProximoToken;
                if (token is null) break;
            }

            if (token is not null) restante.Add((nome, token));
        }

        _fila = restante;
        return Resultado.Ok(novos);
    }
}
