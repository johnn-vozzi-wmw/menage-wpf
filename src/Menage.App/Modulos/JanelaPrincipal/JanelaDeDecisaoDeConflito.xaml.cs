using System.Windows;
using Menage.App.Controles;
using Menage.App.Modulos.Diff.Conflito;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>
/// Pergunta ao usuário o que fazer quando merge ou rebase param num conflito: resolver (editor do Menage), continuar, pular o
/// projeto ou abortar. Fechar a janela ou Esc aborta, que é o caminho que deixa o repositório como estava.
/// </summary>
public partial class JanelaDeDecisaoDeConflito : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    readonly JanelaConflito.Fabrica _editor;
    string _repo = "";

    public JanelaDeDecisaoDeConflito(ContextoDeJanela contexto, JanelaConflito.Fabrica editor) : base(contexto)
    {
        _editor = editor;
        InitializeComponent();
        Resultado = DecisaoDeConflito.Abortar;
    }

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is not ConflitoDoGit conflito) return;
        _repo = conflito.Projeto.Caminho;
        Title = $"{conflito.Operacao}: conflito em {conflito.Projeto.Nome}";
        Descricao.Text = conflito.Descricao;
        Aviso.Text = conflito.Aviso ?? "";
        Aviso.Visibility = string.IsNullOrWhiteSpace(conflito.Aviso) ? Visibility.Collapsed : Visibility.Visible;
        Arquivos.Text = conflito.Status.Length > 0 ? conflito.Status : "(nenhum arquivo listado)";
        BotaoContinuar.Content = conflito.TextoContinuar;
        BotaoPular.Content = conflito.TextoPular;
        BotaoAbortar.Content = conflito.TextoAbortar;
    }

    void AoResolver(object sender, RoutedEventArgs e)
    {
        var janela = _editor.Criar(_repo);
        janela.Owner = this;
        janela.ShowDialog();
    }

    void AoContinuar(object sender, RoutedEventArgs e) => Decidir(DecisaoDeConflito.Continuar);

    void AoPular(object sender, RoutedEventArgs e) => Decidir(DecisaoDeConflito.Pular);

    void AoAbortar(object sender, RoutedEventArgs e) => Decidir(DecisaoDeConflito.Abortar);

    void Decidir(DecisaoDeConflito decisao)
    {
        Resultado = decisao;
        Close();
    }
}
