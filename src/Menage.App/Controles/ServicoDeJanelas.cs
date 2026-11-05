using System.Windows;
using System.Windows.Threading;
using Menage.Core.Interacao;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Controles;

/// <summary>Janela que recebe o parâmetro de <see cref="IServicoDeJanelas.Abrir"/>.</summary>
public interface IJanelaComParametro
{
    void Receber(object? parametro);
}

/// <summary>Janela de diálogo que entrega um resultado ao fechar (nulo se cancelada).</summary>
public interface IJanelaComResultado
{
    object? Resultado { get; }
}

/// <summary>Liga um id de janela ao tipo que o DI cria. Registrada com <c>AddJanela&lt;T&gt;("id")</c>.</summary>
public sealed record RegistroDeJanela(string Id, Type Tipo);

public static class JanelasExtensions
{
    public static IServiceCollection AddJanela<TJanela>(this IServiceCollection servicos, string id)
        where TJanela : Window =>
        servicos.AddTransient<TJanela>().AddSingleton(new RegistroDeJanela(id, typeof(TJanela)));
}

/// <summary>Abre janelas pelo id. Só conhece as janelas que ele mesmo abriu; cada id tem no máximo uma aberta.</summary>
public sealed class ServicoDeJanelas(IServiceProvider servicos, IEnumerable<RegistroDeJanela> registros) : IServicoDeJanelas
{
    readonly Dictionary<string, Type> _tipos = registros.ToDictionary(r => r.Id, r => r.Tipo);
    readonly Dictionary<string, Window> _abertas = [];

    static Dispatcher Tela => Application.Current.Dispatcher;

    public void Abrir(string janelaId, object? parametro = null) => Tela.Invoke(() =>
    {
        if (_abertas.TryGetValue(janelaId, out var aberta))
        {
            (aberta as IJanelaComParametro)?.Receber(parametro);
            aberta.Activate();
            return;
        }

        Criar(janelaId, parametro).Show();
    });

    public async Task<object?> AbrirDialogoAsync(string janelaId, object? parametro = null) =>
        await await Tela.InvokeAsync(async () =>
        {
            var janela = Criar(janelaId, parametro);
            janela.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(j => j.IsActive) ?? Application.Current.MainWindow;
            if (janela is JanelaBase baseDaJanela) baseDaJanela.FechaComEsc = true;
            // Pela fila do Dispatcher para não prender quem chamou dentro do laço modal do ShowDialog.
            await Tela.InvokeAsync(() => janela.ShowDialog());
            return (janela as IJanelaComResultado)?.Resultado;
        });

    public void Fechar(string janelaId) => Tela.Invoke(() =>
    {
        if (_abertas.TryGetValue(janelaId, out var janela)) janela.Close();
    });

    Window Criar(string janelaId, object? parametro)
    {
        if (!_tipos.TryGetValue(janelaId, out var tipo))
            throw new InvalidOperationException($"Janela '{janelaId}' não registrada.");

        var janela = (Window)servicos.GetRequiredService(tipo);
        (janela as IJanelaComParametro)?.Receber(parametro);
        _abertas[janelaId] = janela;
        janela.Closed += (_, _) => _abertas.Remove(janelaId);
        return janela;
    }
}
