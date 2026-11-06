using System.Windows;
using Menage.App.Controles;
using Menage.Core.Banco;
using Menage.Core.Interacao;
using Microsoft.Win32;

namespace Menage.App.Modulos.Banco;

/// <summary>Só liga eventos ao <see cref="BancoViewModel"/>; nenhuma regra de negócio mora aqui.</summary>
public partial class JanelaDoBanco : JanelaBase
{
    public const string Id = "banco.gerenciar";

    readonly BancoViewModel _vm;
    readonly IServicoDeJanelas _janelas;

    public JanelaDoBanco(ContextoDeJanela contexto, BancoViewModel vm, IServicoDeJanelas janelas) : base(contexto)
    {
        (_vm, _janelas) = (vm, janelas);
        DataContext = vm;
        InitializeComponent();
    }

    void AoProcurar(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog { Title = "context.xml do Tomcat", Filter = "context.xml|*.xml|Todos|*.*", CheckFileExists = true };
        if (dialogo.ShowDialog(this) == true) _vm.Caminho = dialogo.FileName;
    }

    void AoCopiarPrevia(object sender, RoutedEventArgs e)
    {
        if (_vm.Previa.Length == 0) return;
        Clipboard.SetText(_vm.Previa);
        Avisar("Prévia copiada", "O XML (com senhas ocultas) está na área de transferência.");
    }

    async void AoImportar(object sender, RoutedEventArgs e)
    {
        var itens = _vm.LerDoArquivo();
        if (itens.Count == 0) return;
        if (await _janelas.AbrirDialogoAsync(JanelaImportarBanco.Id, itens) is IReadOnlyList<PerfilDeBanco> escolhidos && escolhidos.Count > 0)
            _vm.ImportarMarcados(escolhidos);
    }
}

/// <summary>Lista com todos os bancos do context.xml (desmarcados): só entra o que for marcado.</summary>
public partial class JanelaImportarBanco : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public const string Id = "banco.importar";

    public JanelaImportarBanco(ContextoDeJanela contexto) : base(contexto) => InitializeComponent();

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is IReadOnlyList<ItemDeImportacao> itens) Lista.ItemsSource = itens;
    }

    void AoConfirmar(object sender, RoutedEventArgs e)
    {
        Resultado = ((IEnumerable<ItemDeImportacao>)Lista.ItemsSource).Where(i => i.Marcado).Select(i => i.Perfil).ToList();
        Close();
    }
}
