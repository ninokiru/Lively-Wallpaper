using Lively.Models;

namespace Lively.UI.Shared.Factories;

public interface IAppThemeFactory
{
    ThemeModel CreateFromDirectory(string themeDir);
    ThemeModel CreateFromFile(string filePath, string name, string description);
}