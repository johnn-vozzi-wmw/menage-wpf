using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Perfis;

namespace Menage.App.Controles;

public sealed partial class PaletaDeComandosViewModel(
    ICatalogoDeComandos catalogo, IPerfilAtual perfil, IServicoDeDialogos dialogos) : ObservableObject
{
    [ObservableProperty]
    string _busca = string.Empty;

    [ObservableProperty]
    IReadOnlyList<ItemDaPaleta> _itens = [];

    [ObservableProperty]
    ItemDaPaleta? _selecionado;

    /// <summary>Pedido de fechar depois de escolher um comando.</summary>
    public event Action? Concluido;

    public void Reiniciar()
    {
        Busca = string.Empty;
        Filtrar();
    }

    /// <summary>Move a seleção com as setas, dando a volta nas pontas.</summary>
    public void Mover(int passo)
    {
        if (Itens.Count == 0) return;
        var atual = Selecionado is null ? -1 : Itens.ToList().IndexOf(Selecionado);
        Selecionado = Itens[(atual + passo + Itens.Count) % Itens.Count];
    }

    partial void OnBuscaChanged(string value) => Filtrar();

    void Filtrar()
    {
        Itens = BuscaDeComandos.Filtrar(catalogo.Todos, Busca, perfil.Atual);
        Selecionado = Itens.Count > 0 ? Itens[0] : null;
    }

    [RelayCommand]
    async Task ExecutarAsync()
    {
        if (Selecionado is not { Bloqueado: false } item) return;
        Concluido?.Invoke();
        try
        {
            await item.Comando.ExecutarAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await dialogos.MostrarErroAsync(new Core.Erro($"Não foi possível executar \"{item.Comando.Texto}\"", ex.Message));
        }
    }
}
