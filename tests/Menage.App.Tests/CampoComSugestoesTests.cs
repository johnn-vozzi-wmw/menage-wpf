using System.Windows;
using Menage.App.Controles;

namespace Menage.App.Tests;

public sealed class CampoComSugestoesTests
{
    [Fact]
    public void Monta_mede_e_aceita_texto_e_itens_sem_foco()
    {
        WpfDeTeste.Executar(() =>
        {
            var campo = new CampoComSugestoes { ItemsSource = new List<string> { "develop", "main" }, Text = "dev", Placeholder = "Branch" };
            campo.Measure(new Size(300, 100));
            campo.Arrange(new Rect(0, 0, 300, 40));

            campo.Text.Should().Be("dev");
            campo.ActualWidth.Should().BeGreaterThan(0);
        });
    }
}
