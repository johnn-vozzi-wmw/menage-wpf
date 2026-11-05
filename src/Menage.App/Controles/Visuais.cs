using System.Windows;
using System.Windows.Media;

namespace Menage.App.Controles;

public static class Visuais
{
    public static IEnumerable<DependencyObject> Descendentes(DependencyObject raiz)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            yield return filho;
            foreach (var neto in Descendentes(filho)) yield return neto;
        }
    }
}
