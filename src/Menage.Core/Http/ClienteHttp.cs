using System.Net.Http.Headers;
using System.Text;

namespace Menage.Core.Http;

/// <summary>Usuário e senha enviados como Basic ou token enviado como Bearer. Nunca vai para log.</summary>
public sealed record CredencialHttp(string? Usuario, string Segredo, bool Bearer = false)
{
    public override string ToString() => "CredencialHttp(***)";
}

/// <summary>Resposta de um GET: status e corpo em texto.</summary>
public sealed record RespostaHttp(int Status, string Corpo)
{
    public bool Sucesso => Status is >= 200 and < 300;
}

/// <summary>HTTP atrás de uma interface: o motor do Nexus e do GitHub nunca cria <c>HttpClient</c> sozinho (testes usam servidor falso).</summary>
public interface IClienteHttp
{
    /// <summary>GET em texto. Falha de rede (sem resposta) vira <see cref="Erro"/>; qualquer status HTTP vira <see cref="RespostaHttp"/>.</summary>
    Task<Resultado<RespostaHttp>> ObterTextoAsync(Uri url, CredencialHttp? credencial = null, CancellationToken cancelar = default);

    /// <summary>
    /// GET gravando em <paramref name="destino"/> (arquivo parcial <c>.parte</c> trocado no fim). Devolve o status; fora de 2xx nada é gravado.
    /// <paramref name="progresso"/> recebe os bytes de cada bloco recebido.
    /// </summary>
    Task<Resultado<int>> BaixarAsync(Uri url, string destino, CredencialHttp? credencial = null, Action<long>? progresso = null, CancellationToken cancelar = default);
}

public sealed class ClienteHttp : IClienteHttp, IDisposable
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public async Task<Resultado<RespostaHttp>> ObterTextoAsync(Uri url, CredencialHttp? credencial = null, CancellationToken cancelar = default)
    {
        try
        {
            using var pedido = Montar(url, credencial);
            using var resposta = await _http.SendAsync(pedido, cancelar);
            return Resultado.Ok(new RespostaHttp((int)resposta.StatusCode, await resposta.Content.ReadAsStringAsync(cancelar)));
        }
        catch (OperationCanceledException) when (cancelar.IsCancellationRequested)
        {
            return Resultado.Falha<RespostaHttp>(Erro.Cancelado);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return Resultado.Falha<RespostaHttp>($"Não foi possível falar com {url.Host}", ex.Message, "Confira a rede/VPN e o endereço do servidor.");
        }
    }

    public async Task<Resultado<int>> BaixarAsync(Uri url, string destino, CredencialHttp? credencial = null, Action<long>? progresso = null, CancellationToken cancelar = default)
    {
        var parcial = destino + ".parte";
        try
        {
            using var pedido = Montar(url, credencial);
            using var resposta = await _http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, cancelar);
            if (!resposta.IsSuccessStatusCode) return Resultado.Ok((int)resposta.StatusCode);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destino))!);
            await using (var entrada = await resposta.Content.ReadAsStreamAsync(cancelar))
            await using (var saida = new FileStream(parcial, FileMode.Create, FileAccess.Write))
            {
                var buffer = new byte[81920];
                int lidos;
                while ((lidos = await entrada.ReadAsync(buffer, cancelar)) > 0)
                {
                    await saida.WriteAsync(buffer.AsMemory(0, lidos), cancelar);
                    progresso?.Invoke(lidos);
                }
            }

            File.Move(parcial, destino, overwrite: true);
            return Resultado.Ok((int)resposta.StatusCode);
        }
        catch (OperationCanceledException) when (cancelar.IsCancellationRequested)
        {
            Apagar(parcial);
            return Resultado.Falha<int>(Erro.Cancelado);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Apagar(parcial);
            var disco = ex is IOException && ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase);
            return Resultado.Falha<int>(disco ? "O disco encheu durante o download" : $"Falha ao baixar de {url.Host}", ex.Message,
                disco ? "Libere espaço e tente de novo." : "Confira a rede/VPN e tente de novo.");
        }
    }

    public void Dispose() => _http.Dispose();

    static HttpRequestMessage Montar(Uri url, CredencialHttp? credencial)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        pedido.Headers.UserAgent.ParseAdd("Menage");
        if (credencial is not null)
            pedido.Headers.Authorization = credencial.Bearer
                ? new AuthenticationHeaderValue("Bearer", credencial.Segredo)
                : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credencial.Usuario}:{credencial.Segredo}")));
        return pedido;
    }

    static void Apagar(string arquivo)
    {
        try { File.Delete(arquivo); }
        catch (IOException) { /* sobra um .parte, sem importância */ }
    }
}

/// <summary>Protege segredos por usuário do Windows (DPAPI no app; testes usam uma versão reversível simples).</summary>
public interface IProtetorDeSegredo
{
    string Proteger(string texto);

    /// <summary>Devolve vazio se não conseguir abrir (outro usuário/máquina).</summary>
    string Revelar(string protegido);
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ProtetorDpapi : IProtetorDeSegredo
{
    public string Proteger(string texto) => string.IsNullOrEmpty(texto) ? "" :
        Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(texto), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));

    public string Revelar(string protegido)
    {
        if (string.IsNullOrEmpty(protegido)) return "";
        try
        {
            return Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(protegido), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            return "";
        }
    }
}
