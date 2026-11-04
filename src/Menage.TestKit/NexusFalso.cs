using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Menage.TestKit;

/// <summary>
/// Servidor Nexus de mentira em http://127.0.0.1:porta/ (HttpListener, sem precisar de administrador).
/// GET/HEAD devolvem o que foi publicado; PUT guarda (deploy). Falhas, lentidão e conexão cortada são simuláveis.
/// </summary>
public sealed class NexusFalso : IDisposable
{
    sealed class Falha(int status, int restantes, string? caminhoContem)
    {
        public int Status { get; } = status;

        public int Restantes { get; set; } = restantes;

        public string? CaminhoContem { get; } = caminhoContem;
    }

    readonly HttpListener _listener = new();
    readonly ConcurrentDictionary<string, byte[]> _artefatos = new();
    readonly ConcurrentQueue<(string Metodo, string Caminho)> _requisicoes = new();
    readonly List<Falha> _falhas = [];
    readonly Task _laco;
    TimeSpan _atraso = TimeSpan.Zero;

    NexusFalso(int porta)
    {
        Url = $"http://127.0.0.1:{porta}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _laco = Task.Run(AtenderAsync);
    }

    /// <summary>Endereço base, com barra final.</summary>
    public string Url { get; }

    public IReadOnlyList<(string Metodo, string Caminho)> Requisicoes => [.. _requisicoes];

    public static NexusFalso Iniciar()
    {
        for (var tentativa = 0; ; tentativa++)
        {
            try { return new NexusFalso(PortaLivre()); }
            catch (HttpListenerException) when (tentativa < 5) { /* outra aplicação pegou a porta: tenta outra */ }
        }
    }

    public void Publicar(string caminho, byte[] conteudo) => _artefatos[Normalizar(caminho)] = conteudo;

    public void Publicar(string caminho, string conteudo) => Publicar(caminho, System.Text.Encoding.UTF8.GetBytes(conteudo));

    public byte[]? Obter(string caminho) => _artefatos.GetValueOrDefault(Normalizar(caminho));

    /// <summary>As próximas <paramref name="vezes"/> requisições (só as que contêm o trecho, se informado) respondem com este status.</summary>
    public void SimularFalha(int status, int vezes = 1, string? caminhoContem = null)
    {
        lock (_falhas) _falhas.Add(new Falha(status, vezes, caminhoContem));
    }

    /// <summary>Cada requisição passa a demorar este tempo antes de responder.</summary>
    public void SimularLentidao(TimeSpan atraso) => _atraso = atraso;

    /// <summary>As próximas requisições terminam com a conexão cortada, sem resposta.</summary>
    public void SimularConexaoCortada(int vezes = 1) => SimularFalha(0, vezes);

    public void Dispose()
    {
        _listener.Close();
        try { _laco.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { /* o laço termina com erro ao fechar o listener */ }
    }

    static string Normalizar(string caminho) => "/" + caminho.TrimStart('/');

    static int PortaLivre()
    {
        var teste = new TcpListener(IPAddress.Loopback, 0);
        teste.Start();
        var porta = ((IPEndPoint)teste.LocalEndpoint).Port;
        teste.Stop();
        return porta;
    }

    async Task AtenderAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext contexto;
            try { contexto = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
            _ = Task.Run(() => ResponderAsync(contexto));
        }
    }

    async Task ResponderAsync(HttpListenerContext contexto)
    {
        var pedido = contexto.Request;
        var resposta = contexto.Response;
        var caminho = pedido.Url!.AbsolutePath;
        _requisicoes.Enqueue((pedido.HttpMethod, caminho));
        try
        {
            if (_atraso > TimeSpan.Zero) await Task.Delay(_atraso);
            if (SortearFalha(caminho) is { } falha)
            {
                if (falha.Status == 0) { resposta.Abort(); return; }
                resposta.StatusCode = falha.Status;
            }
            else
            {
                await AtenderNormalAsync(pedido, resposta, caminho);
            }

            resposta.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            resposta.Abort();
        }
    }

    Falha? SortearFalha(string caminho)
    {
        lock (_falhas)
        {
            var falha = _falhas.Find(f => f.Restantes > 0 && (f.CaminhoContem is null || caminho.Contains(f.CaminhoContem, StringComparison.Ordinal)));
            if (falha is not null) falha.Restantes--;
            return falha;
        }
    }

    async Task AtenderNormalAsync(HttpListenerRequest pedido, HttpListenerResponse resposta, string caminho)
    {
        switch (pedido.HttpMethod)
        {
            case "GET" or "HEAD":
                if (_artefatos.TryGetValue(caminho, out var conteudo))
                {
                    resposta.ContentLength64 = conteudo.Length;
                    if (pedido.HttpMethod == "GET") await resposta.OutputStream.WriteAsync(conteudo);
                }
                else
                {
                    resposta.StatusCode = 404;
                }

                break;
            case "PUT":
                using (var memoria = new MemoryStream())
                {
                    await pedido.InputStream.CopyToAsync(memoria);
                    _artefatos[caminho] = memoria.ToArray();
                }

                resposta.StatusCode = 201;
                break;
            default:
                resposta.StatusCode = 405;
                break;
        }
    }
}
