namespace Menage.TestKit;

/// <summary>Diálogos que respondem sem tela e guardam o que foi mostrado.</summary>
public sealed class DialogosFalsos : IServicoDeDialogos
{
    public bool RespostaDaConfirmacao { get; set; } = true;

    public List<Previa> Previas { get; } = [];

    public List<(string Titulo, string Mensagem)> Informacoes { get; } = [];

    public List<Erro> Erros { get; } = [];

    public List<IResumoDoLote> Resumos { get; } = [];

    public Task<bool> ConfirmarAsync(Previa previa)
    {
        Previas.Add(previa);
        return Task.FromResult(RespostaDaConfirmacao);
    }

    public Task InformarAsync(string titulo, string mensagem)
    {
        Informacoes.Add((titulo, mensagem));
        return Task.CompletedTask;
    }

    public Task MostrarErroAsync(Erro erro)
    {
        Erros.Add(erro);
        return Task.CompletedTask;
    }

    public Task MostrarResumoAsync(IResumoDoLote resumo)
    {
        Resumos.Add(resumo);
        return Task.CompletedTask;
    }
}

public sealed class ProgressoGravador : IProgresso
{
    readonly object _trava = new();
    readonly List<EventoDeProgresso> _eventos = [];

    public IReadOnlyList<EventoDeProgresso> Eventos
    {
        get { lock (_trava) return [.. _eventos]; }
    }

    public void Relatar(EventoDeProgresso evento)
    {
        lock (_trava) _eventos.Add(evento);
    }
}

public sealed class JanelasFalsas : IServicoDeJanelas
{
    public List<(string Id, object? Parametro)> Abertas { get; } = [];

    public List<string> Fechadas { get; } = [];

    public object? RespostaDoDialogo { get; set; }

    public void Abrir(string janelaId, object? parametro = null) => Abertas.Add((janelaId, parametro));

    public Task<object?> AbrirDialogoAsync(string janelaId, object? parametro = null)
    {
        Abertas.Add((janelaId, parametro));
        return Task.FromResult(RespostaDoDialogo);
    }

    public void Fechar(string janelaId) => Fechadas.Add(janelaId);
}
