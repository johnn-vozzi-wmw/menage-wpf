using Menage.App.Controles;

namespace Menage.App.Modulos.Instalacoes;

public partial class JanelaSobre : JanelaBase
{
    public const string Id = "app.sobre";

    public JanelaSobre(ContextoDeJanela contexto, SobreViewModel modelo) : base(contexto)
    {
        Modelo = modelo;
        DataContext = modelo;
        FechaComEsc = true;
        InitializeComponent();
    }

    public SobreViewModel Modelo { get; }
}
