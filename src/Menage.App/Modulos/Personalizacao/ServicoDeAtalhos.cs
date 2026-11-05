using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.Comandos;
using Menage.Core.Personalizacao;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>
/// Atalhos de teclado do app: o padrão vem de cada comando do catálogo e o usuário troca em Personalização.
/// As janelas só repassam a tecla (<see cref="Tratar"/>); nenhum atalho fica fixo em tela. O catálogo é pedido
/// na primeira tecla, porque os módulos que o formam também podem depender deste serviço.
/// </summary>
public sealed class ServicoDeAtalhos(IServiceProvider servicos, IPreferenciasDeAparencia preferencias)
{
    MapaDeAtalhos? _mapa;
    Dictionary<string, string>? _origem;

    ICatalogoDeComandos Catalogo => servicos.GetRequiredService<ICatalogoDeComandos>();

    /// <summary>Atalhos em vigor (padrões do catálogo + trocas salvas). Refeito quando as trocas gravadas mudam.</summary>
    public MapaDeAtalhos Mapa
    {
        get
        {
            var atuais = preferencias.Valor.Atalhos;
            if (_mapa is null || !ReferenceEquals(_origem, atuais))
            {
                _mapa = Montar();
                _origem = atuais;
            }

            return _mapa;
        }
    }

    /// <summary>Mapa novo para a tela de edição: alterar o dele não muda nada até gravar.</summary>
    public MapaDeAtalhos Montar() =>
        new(Catalogo.Todos.Select(c => (c.Id, c.Atalho)), preferencias.Valor.Atalhos);

    /// <summary>Executa o comando dono do gesto. Verdadeiro se tratou a tecla (a janela marca o evento como tratado).</summary>
    public bool Tratar(KeyEventArgs e)
    {
        if (e.Handled || De(e) is not { } gesto) return false;
        if (Mapa.ComandoDe(gesto) is not { } id || Catalogo.Obter(id) is not { } comando || !comando.PodeExecutar()) return false;
        new AsyncRelayCommand(() => comando.ExecutarAsync()).Execute(null);
        return true;
    }

    /// <summary>A tecla pressionada como gesto; nulo para teclas só de modificador.</summary>
    public static Gesto? De(KeyEventArgs e)
    {
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        if (tecla is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed or Key.ImeProcessed) return null;
        var nome = NomeDaTecla(tecla);
        if (nome is null) return null;
        var m = Keyboard.Modifiers;
        return new Gesto(m.HasFlag(ModifierKeys.Control), m.HasFlag(ModifierKeys.Shift), m.HasFlag(ModifierKeys.Alt), nome);
    }

    static string? NomeDaTecla(Key tecla) => tecla switch
    {
        >= Key.A and <= Key.Z => tecla.ToString(),
        >= Key.D0 and <= Key.D9 => ((int)tecla - (int)Key.D0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)tecla - (int)Key.NumPad0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        >= Key.F1 and <= Key.F12 => tecla.ToString(),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.Oem2 => "/",
        Key.Escape => "Esc",
        Key.Return => "Enter",
        Key.Space => "Space",
        Key.Tab => "Tab",
        Key.Delete => "Delete",
        Key.Insert => "Insert",
        Key.Back => "Backspace",
        Key.Home or Key.End or Key.Up or Key.Down or Key.Left or Key.Right => tecla.ToString(),
        Key.PageUp => "PageUp",
        Key.Next => "PageDown",
        _ => null,
    };
}
